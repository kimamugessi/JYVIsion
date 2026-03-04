using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JYVision.Algorithm;
using JYVision.Core;
using JYVision.Teach;
using JYVision.Util;
using OpenCvSharp;

namespace JYVision.Inspect
{
    public class InspWorker
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private InspectBoard _inspectBoard = new InspectBoard();

        public bool IsRunning { get; set; } = false;

        // ── 볼트 행 기준 데이터 ──────────────────────────────────────────
        public class BoltRowReference
        {
            public float CenterY { get; set; }  // 행 Y 중심
            public List<float> ExpectedXList { get; set; }  // 실제 볼트 X 좌표 목록
            public float MatchTolerance { get; set; }  // 매칭 허용 오차
        }

        public List<BoltRowReference> BoltRowReferences { get; set; }

        public InspWorker() { }

        public void Stop() { _cts.Cancel(); }

        public void StartCycleInspectImage()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => InspectionLoop(this, _cts.Token));
        }

        private void InspectionLoop(InspWorker inspWorker, CancellationToken token)
        {
            Global.Inst.InspStage.SetWorkingState(WorkingState.INSPECT);
            IsRunning = true;
            while (!token.IsCancellationRequested)
            {
                Global.Inst.InspStage.OneCycle();
            }
            IsRunning = false;
        }

        // ── [양산용] RunInspect ──────────────────────────────────────────
        public bool RunInspect(out bool isDefect)
        {
            isDefect = false;
            Model curMode = Global.Inst.InspStage.CurModel;
            List<InspWindow> inspWindowList = curMode.InspWindowList;

            foreach (var window in inspWindowList)
                UpdateInspData(window);

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> finalDisplayList = new List<DrawInspectInfo>();

            foreach (var inspWindow in inspWindowList)
            {
                foreach (var algo in inspWindow.AlgorithmList)
                {
                    algo.DoInspect();
                    List<DrawInspectInfo> results;
                    algo.GetResultRect(out results);
                    finalDisplayList.AddRange(results);
                }
            }

            RunCheckMarkContrast();

            if (cameraForm != null)
            {
                cameraForm.ResetDisplay();
                if (finalDisplayList.Count > 0) cameraForm.AddRect(finalDisplayList);
            }

            return true;
        }

        // ── Teaching 시 호출: 실제 감지된 X 좌표를 기준으로 저장 ─────────
        public void SaveBoltReference(List<DrawInspectInfo> results)
        {
            var yGroups = ClusterAxis(
                results.Select(r => (float)r.rect.Y).ToList(), 100f);

            BoltRowReferences = new List<BoltRowReference>();

            foreach (float cy in yGroups)
            {
                var rowBolts = results
                    .Where(r => Math.Abs((float)r.rect.Y - cy) < 100f)
                    .OrderBy(r => r.rect.X)
                    .ToList();

                var gaps = new List<float>();
                for (int i = 1; i < rowBolts.Count; i++)
                    gaps.Add((float)(rowBolts[i].rect.X - rowBolts[i - 1].rect.X));

                float minGap = gaps.Count > 0 ? gaps.Min() : 100f;
                float tolerance = minGap * 0.45f;

                var expectedXList = rowBolts.Select(r => (float)r.rect.X).ToList();

                BoltRowReferences.Add(new BoltRowReference
                {
                    CenterY = cy,
                    ExpectedXList = expectedXList,
                    MatchTolerance = tolerance
                });

                SLogger.Write($"[Teaching] Row Y≈{cy:F0} → " +
                              $"기준 볼트 {expectedXList.Count}개, " +
                              $"X위치: [{string.Join(", ", expectedXList.Select(x => $"{x:F0}"))}], " +
                              $"허용오차: {tolerance:F0}px");
            }

            SLogger.Write($"[Teaching] 총 {BoltRowReferences.Count}개 행 저장 완료");
        }

        // ── 볼트 매칭 + 누락 탐지 ───────────────────────────────────────
        public void RunOnlyBoltMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                var boltAlgo = window.AlgorithmList
                    .FirstOrDefault(a => a is MatchAlgorithm) as MatchAlgorithm;

                if (boltAlgo != null && boltAlgo.IsUse)
                {
                    boltAlgo.DoInspect();
                    List<DrawInspectInfo> results;
                    boltAlgo.GetResultRect(out results);

                    if (results != null && results.Count > 0)
                    {
                        // ✅ Teaching 체크 없이 바로 간격 분석 실행
                        DetectMissingBolts(results, displayList);
                    }
                    else
                    {
                        SLogger.Write("[Info] 볼트 감지 결과 없음");
                    }
                }
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        // ── Teaching 기준 위치 근처에 있는 결과만 통과 (오탐 제거) ─────────
        private List<DrawInspectInfo> FilterByReference(List<DrawInspectInfo> results)
        {
            var filtered = new List<DrawInspectInfo>();

            foreach (var r in results)
            {
                float rx = (float)r.rect.X;
                float ry = (float)r.rect.Y;

                bool isNearReference = BoltRowReferences.Any(row =>
                    Math.Abs(ry - row.CenterY) < 100f &&
                    row.ExpectedXList.Any(ex =>
                        Math.Abs(rx - ex) < row.MatchTolerance
                    )
                );

                if (isNearReference)
                    filtered.Add(r);
                else
                    SLogger.Write($"  [오탐제거] X≈{rx:F0}, Y≈{ry:F0} → 기준 위치 아님");
            }

            SLogger.Write($"[FilterByReference] {results.Count}개 → {filtered.Count}개 (제거: {results.Count - filtered.Count}개)");
            return filtered;
        }

        // ── 누락 볼트 탐지 ───────────────────────────────────────────────
        private void DetectMissingBolts(List<DrawInspectInfo> results, List<DrawInspectInfo> displayList)
        {
            SLogger.Write("==== Missing Bolt Detection Start ====");

            int boltSize = results[0].rect.Width > 0 ? results[0].rect.Width : 60;

            // ── 1. 전체 Y값 출력 ─────────────────────────────────────────────
            var allYValues = results.Select(r => (float)r.rect.Y).OrderBy(y => y).ToList();
            SLogger.Write($"[RAW Y값] {string.Join(", ", allYValues.Select(y => $"{y:F0}"))}");

            // ── 2. Y로 상단/하단 완전 분리 (큰 gap 기준) ─────────────────────
            var sortedY = allYValues.Distinct().OrderBy(y => y).ToList();
            float biggestYGap = 0;
            float splitY = 0;
            for (int i = 1; i < sortedY.Count; i++)
            {
                float gap = sortedY[i] - sortedY[i - 1];
                if (gap > biggestYGap)
                {
                    biggestYGap = gap;
                    splitY = (sortedY[i] + sortedY[i - 1]) / 2f;
                }
            }
            SLogger.Write($"[Y 분할 기준] splitY={splitY:F0}, 최대 Y간격={biggestYGap:F0}");

            // 상단 그룹 / 하단 그룹으로 완전 분리
            var topGroup = results.Where(r => (float)r.rect.Y < splitY).ToList();
            var bottomGroup = results.Where(r => (float)r.rect.Y >= splitY).ToList();

            SLogger.Write($"[상단 그룹] {topGroup.Count}개, [하단 그룹] {bottomGroup.Count}개");

            // ── 3. 각 그룹 독립 처리 ─────────────────────────────────────────
            ProcessGroup("상단(작은건반)", topGroup, displayList, boltSize);
            ProcessGroup("하단(긴건반)", bottomGroup, displayList, boltSize);

            SLogger.Write("======================================");
        }

        private void ProcessGroup(string label, List<DrawInspectInfo> groupBolts,
                           List<DrawInspectInfo> displayList, int boltSize)
        {
            if (groupBolts.Count == 0) return;
            SLogger.Write($"\n── [{label}] 처리 시작 ──");

            var yRows = ClusterAxis(
                groupBolts.Select(r => (float)r.rect.Y).ToList(), 80f);

            var detectedCols = ClusterAxis(
                groupBolts.Select(r => (float)r.rect.X).ToList(), 80f);

            SLogger.Write($"  감지된 열: {detectedCols.Count}개 → {string.Join(", ", detectedCols.Select(x => $"{x:F0}"))}");
            SLogger.Write($"  Y행: {yRows.Count}개 → {string.Join(", ", yRows.Select(y => $"{y:F0}"))}");

            if (detectedCols.Count < 2)
            {
                displayList.AddRange(groupBolts);
                return;
            }

            var colGaps = new List<float>();
            for (int i = 1; i < detectedCols.Count; i++)
                colGaps.Add(detectedCols[i] - detectedCols[i - 1]);

            float medianGap = GetMedian(colGaps);
            // ✅ 매칭 오차: 간격의 45% (넉넉하게)
            float xTol = medianGap * 0.45f;
            float yTol = 120f;

            SLogger.Write($"  기준 열 간격: {medianGap:F0}px, X오차: {xTol:F0}px, Y오차: {yTol:F0}px");

            // ── 빠진 열 예측 ─────────────────────────────────────────────────
            var allCols = new List<float> { detectedCols[0] };
            for (int i = 1; i < detectedCols.Count; i++)
            {
                float gap = detectedCols[i] - detectedCols[i - 1];
                int missingCnt = (int)Math.Round(gap / medianGap) - 1;
                for (int m = 1; m <= missingCnt; m++)
                {
                    float px = detectedCols[i - 1] + medianGap * m;
                    allCols.Add(px);
                    SLogger.Write($"  ★ 예측 열 삽입 X≈{px:F0}");
                }
                allCols.Add(detectedCols[i]);
            }
            allCols = allCols.OrderBy(x => x).ToList();

            // ── 유효 행 판단 (볼트 존재 비율 50% 이상) ───────────────────────
            var validYRows = new List<float>();
            foreach (float ey in yRows)
            {
                int foundCount = allCols.Count(cx =>
                    groupBolts.Any(r =>
                        Math.Abs((float)r.rect.X - cx) < xTol &&
                        Math.Abs((float)r.rect.Y - ey) < yTol));

                float ratio = (float)foundCount / allCols.Count;
                SLogger.Write($"  Y≈{ey:F0} 존재 비율: {foundCount}/{allCols.Count} = {ratio:P0}");

                if (ratio >= 0.5f)
                    validYRows.Add(ey);
            }

            SLogger.Write($"  유효 Y행: {validYRows.Count}개 → {string.Join(", ", validYRows.Select(y => $"{y:F0}"))}");

            // ── 각 열 × 유효 행 확인 ─────────────────────────────────────────
            var alreadyAdded = new HashSet<DrawInspectInfo>();

            foreach (float cx in allCols)
            {
                // ✅ 실제 볼트 매칭 시 xTol 사용 (넉넉하게)
                var colBolts = groupBolts
                    .Where(r => Math.Abs((float)r.rect.X - cx) < xTol)
                    .ToList();

                foreach (float ey in validYRows)
                {
                    // ✅ 가장 가까운 볼트로 매칭 (tolerance 대신 nearest 방식)
                    var nearest = colBolts
                        .OrderBy(r => Math.Abs((float)r.rect.Y - ey))
                        .FirstOrDefault();

                    bool found = nearest != null && Math.Abs((float)nearest.rect.Y - ey) < yTol;

                    if (!found)
                    {
                        SLogger.Write($"  [MISSING] X≈{cx:F0}, Y≈{ey:F0}");
                        displayList.Add(new DrawInspectInfo(
                            new Rect(
                                (int)cx - boltSize / 2,
                                (int)ey - boltSize / 2,
                                boltSize, boltSize),
                            "MISSING",
                            InspectType.InspNone,
                            DecisionType.Defect));
                    }
                    else
                    {
                        SLogger.Write($"  [OK] X≈{cx:F0}, Y≈{ey:F0} (실제 Y={nearest.rect.Y})");
                    }
                }

                foreach (var b in colBolts.Where(b => !alreadyAdded.Contains(b)))
                {
                    displayList.Add(b);
                    alreadyAdded.Add(b);
                }
            }
        }
        // ── 볼트 짝지기 ROI 검사 ─────────────────────────────────────────
        public void RunBoltPairInspect()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                var boltAlgo = window.AlgorithmList
                    .FirstOrDefault(a => a is MatchAlgorithm) as MatchAlgorithm;

                if (boltAlgo != null && boltAlgo.IsUse)
                {
                    boltAlgo.DoInspect();
                    List<Rect> pairROIs = boltAlgo.GetBoltPairROIs();

                    foreach (var roi in pairROIs)
                    {
                        displayList.Add(new DrawInspectInfo(
                            roi, "                   PairArea",
                            InspectType.InspNone, DecisionType.Good));

                        foreach (var other in window.AlgorithmList
                            .Where(a => !(a is MatchAlgorithm)))
                        {
                            other.InspRect = roi;
                            other.DoInspect();
                            List<DrawInspectInfo> res;
                            other.GetResultRect(out res);
                            displayList.AddRange(res);
                        }
                    }
                }
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        // ── 각인 대조 검사 ───────────────────────────────────────────────
        public void RunCheckMarkContrast()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            foreach (var window in curMode.InspWindowList)
            {
                var boltAlgo = window.AlgorithmList
                    .FirstOrDefault(a => a is MatchAlgorithm) as MatchAlgorithm;

                if (boltAlgo != null && boltAlgo.IsUse)
                {
                    boltAlgo.DoInspect();
                    List<Rect> pairROIs = boltAlgo.GetBoltPairROIs();

                    foreach (var roi in pairROIs)
                    {
                        int paddingX = (int)(roi.Width * 0.1);
                        int innerWidth = roi.Width - (paddingX * 2);
                        int innerHeight = roi.Height;
                        int markHeight = (int)(innerHeight * 0.5);

                        Rect markRoi = new Rect(
                            roi.X + paddingX,
                            roi.Y + (innerHeight - markHeight),
                            innerWidth,
                            markHeight - 250);

                        Mat scene = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
                        using (Mat roiImg = new Mat(scene, markRoi))
                        {
                            Cv2.MeanStdDev(roiImg, out Scalar mean, out Scalar stddev);
                            double score = stddev.Val0;
                            bool isExist = score > 5.0;
                            DecisionType dec = isExist ? DecisionType.Good : DecisionType.Defect;
                            string resultText = $"Mark: {(isExist ? "OK" : "NG")} ({score:F1})";

                            displayList.Add(new DrawInspectInfo(
                                markRoi, resultText, InspectType.InspNone, dec));
                        }
                    }
                }
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        public bool TryInspect(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj != null)
            {
                if (!UpdateInspData(inspObj)) return false;
                _inspectBoard.Inspect(inspObj);
                DisplayResult(inspObj, inspType);
            }
            else
            {
                bool isDefect = false;
                RunInspect(out isDefect);
            }
            return true;
        }

        // ── 공통 유틸 ────────────────────────────────────────────────────

        public bool UpdateInspData(InspWindow inspWindow)
        {
            if (inspWindow is null) return false;
            Rect windowArea = inspWindow.WindowArea;
            inspWindow.PatternLearn();
            foreach (var inspAlgo in inspWindow.AlgorithmList)
            {
                inspAlgo.TeachRect = windowArea;
                inspAlgo.InspRect = windowArea;
                Mat srcImage = Global.Inst.InspStage.GetMat(0, inspAlgo.ImageChannel);
                inspAlgo.SetInspData(srcImage);
            }
            return true;
        }

        private bool DisplayResult(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj is null) return false;
            List<DrawInspectInfo> totalArea = new List<DrawInspectInfo>();
            foreach (var algorithm in inspObj.AlgorithmList)
            {
                if (algorithm.InspectType != inspType && inspType != InspectType.InspNone) continue;
                List<DrawInspectInfo> resultArea;
                if (algorithm.GetResultRect(out resultArea) > 0) totalArea.AddRange(resultArea);
            }
            if (totalArea.Count > 0)
                MainForm.GetDockForm<CameraForm>()?.AddRect(totalArea);
            return true;
        }

        /// <summary>1D 좌표를 tolerance 기준으로 클러스터링 → 각 클러스터 중심 반환</summary>
        private List<float> ClusterAxis(List<float> values, float tolerance)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var clusters = new List<List<float>>();

            foreach (float val in sorted)
            {
                var cluster = clusters.FirstOrDefault(c =>
                    Math.Abs(c.Average() - val) < tolerance);

                if (cluster != null)
                    cluster.Add(val);
                else
                    clusters.Add(new List<float> { val });
            }

            return clusters
                .Select(c => c.Average())
                .OrderBy(v => v)
                .ToList();
        }

        /// <summary>float 리스트의 중앙값 반환</summary>
        private float GetMedian(List<float> values)
        {
            if (values == null || values.Count == 0) return 0f;
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[mid - 1] + sorted[mid]) / 2f
                : sorted[mid];
        }
    }
}