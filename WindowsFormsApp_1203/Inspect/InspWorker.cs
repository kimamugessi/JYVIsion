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
                Global.Inst.InspStage.OneCycle();
            IsRunning = false;
        }

        // ── [양산용] RunInspect ──────────────────────────────────────────
        public bool RunInspect(out bool isDefect)
        {
            isDefect = false;
            Model curMode = Global.Inst.InspStage.CurModel;

            foreach (var window in curMode.InspWindowList)
                UpdateInspData(window);

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> finalDisplayList = new List<DrawInspectInfo>();

            foreach (var inspWindow in curMode.InspWindowList)
            {
                foreach (var algo in inspWindow.AlgorithmList)
                {
                    algo.DoInspect();
                    List<DrawInspectInfo> results;
                    algo.GetResultRect(out results);
                    finalDisplayList.AddRange(results);
                }
            }

            if (cameraForm != null)
            {
                cameraForm.ResetDisplay();
                if (finalDisplayList.Count > 0) cameraForm.AddRect(finalDisplayList);
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        // 메인 검사: 건반 찾기 → 볼트 확인 → 각인 확인
        // ════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════
        // 건반 매칭: MatchAlgorithm + 세로 길이 자동 보정
        // ════════════════════════════════════════════════════════════════
        public void RunKeyMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            SLogger.Write("==== 건반 매칭 시작 ====");

            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty())
            {
                SLogger.Write("[Error] 이미지 없음");
                return;
            }

            // ── Step 1. MatchAlgorithm으로 건반 상단 위치 찾기 ──────────
            var matchedRects = new List<Rect>();
            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                var matchAlgo = window.AlgorithmList
                    .FirstOrDefault(a => a is MatchAlgorithm) as MatchAlgorithm;
                if (matchAlgo == null || !matchAlgo.IsUse) continue;

                matchAlgo.DoInspect();
                matchAlgo.GetResultRect(out List<DrawInspectInfo> results);
                if (results == null) continue;

                foreach (var r in results.OrderBy(r => r.rect.X))
                    matchedRects.Add(r.rect);
            }

            SLogger.Write($"[매칭] {matchedRects.Count}개 검출");

            if (matchedRects.Count == 0)
            {
                SLogger.Write("[Error] 건반 템플릿 매칭 결과 없음");
                cameraForm?.ResetDisplay();
                return;
            }

            // ── Step 2. 각 매칭 위치에서 실제 건반 높이 탐지 ────────────
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            foreach (var matched in matchedRects)
            {
                Rect keyRect = FindActualKeyRect(colorMat, grayMat, matched);
                SLogger.Write($"[건반] X={keyRect.X} W={keyRect.Width} H={keyRect.Height}");

                displayList.Add(new DrawInspectInfo(
                    keyRect,
                    $"Key H={keyRect.Height}",
                    InspectType.InspNone,
                    DecisionType.Good));
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
            SLogger.Write($"==== 건반 {displayList.Count}개 완료 ====");
        }

        // ── 매칭된 위치에서 실제 건반 Rect 탐지 ─────────────────────────
        // 방법: 매칭된 rect 중앙 픽셀의 BGR 색상을 기준으로
        //       위아래로 스캔하여 같은 색상 영역의 실제 경계를 찾음
        // ── 매칭된 X 범위에서 Canny 엣지로 실제 건반 상/하단 탐지 ────
        private Rect FindActualKeyRect(Mat colorMat, Mat grayMat, Rect matched)
        {
            const int EXPECTED_HEIGHT = 1500; // 기준 높이
            int imgH = grayMat.Height;

            // 1. ROI 설정: 건반 하단이 충분히 포함되도록 하되, 너무 바닥까지는 가지 않음
            int roiY = Math.Max(0, matched.Y - 100);
            int roiH = Math.Min(imgH - roiY, matched.Height + 250);
            Rect searchRect = new Rect(matched.X + (int)(matched.Width * 0.2), roiY, (int)(matched.Width * 0.6), roiH);

            using (Mat roiGray = new Mat(grayMat, searchRect))
            using (Mat edges = new Mat())
            {
                Cv2.Canny(roiGray, edges, 40, 120);
                int[] edgeCounts = new int[edges.Rows];
                for (int y = 0; y < edges.Rows; y++)
                {
                    for (int x = 0; x < edges.Cols; x++)
                        if (edges.At<byte>(y, x) > 0) edgeCounts[y]++;
                }

                int topY = -1;
                int bottomY = -1;
                int threshold = (int)(edges.Cols * 0.08f);

                // [핵심 수정] 하단 엣지 탐색: ROI의 최하단이 아니라 60%~90% 지점 사이에서만 탐색
                // 이렇게 하면 바닥 케이스 노이즈를 피하고 실제 스펀지 라인만 잡습니다.
                int searchStart = (int)(edges.Rows * 0.95);
                int searchEnd = (int)(edges.Rows * 0.6);
                for (int y = searchStart; y > searchEnd; y--)
                {
                    if (edgeCounts[y] >= threshold) { bottomY = y; break; }
                }

                // [상단 엣지 탐색]
                for (int y = 0; y < edges.Rows * 0.4; y++)
                {
                    if (edgeCounts[y] >= threshold) { topY = y; break; }
                }

                // [안전장치] 하단을 못 찾거나 너무 위에서 잡히면 매칭 데이터 하단값 사용
                if (bottomY == -1)
                    bottomY = edges.Rows - 100; // 기본값 강제 할당

                // 상단은 하단 기준으로 역산
                if (topY == -1) topY = bottomY - EXPECTED_HEIGHT;

                int finalTop = roiY + topY;
                int finalH = bottomY - topY;

                // 최종 높이가 너무 작아지는 것 방지
                if (finalH < 1000) finalH = EXPECTED_HEIGHT;

                return new Rect(matched.X, finalTop, matched.Width, finalH);
            }
        }

        public void RunOnlyBoltMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            SLogger.Write("==== 건반 검사 시작 ====");

            // ── Step 1. MatchAlgorithm으로 건반 위치 찾기 ────────────────
            List<Rect> keyRects = FindKeysByMatchAlgorithm(curMode);

            if (keyRects.Count == 0)
            {
                SLogger.Write("[Error] 건반을 감지하지 못했습니다.");
                cameraForm?.ResetDisplay();
                return;
            }

            SLogger.Write($"[건반] {keyRects.Count}개 감지: {string.Join(", ", keyRects.Select(r => $"X={r.X}"))}");

            // ── Step 2. 각 건반마다 볼트 & 각인 확인 ─────────────────────
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            int pass = 0;
            int fail = 0;

            foreach (Rect key in keyRects)
            {
                // 건반 ROI 표시 (파란 박스)
                displayList.Add(new DrawInspectInfo(
                    key, $"Key", InspectType.InspNone, DecisionType.Good));

                bool topBoltOk = CheckBolt(grayMat, key, BoltPosition.Top, displayList);
                bool bottomBoltOk = CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                bool markOk = CheckMark(grayMat, key, displayList);

                bool keyOk = topBoltOk && bottomBoltOk && markOk;

                SLogger.Write($"[건반 X={key.X}] 상단볼트:{(topBoltOk ? "OK" : "FAIL")} " +
                              $"하단볼트:{(bottomBoltOk ? "OK" : "FAIL")} " +
                              $"각인:{(markOk ? "OK" : "FAIL")}");

                if (keyOk) pass++;
                else fail++;
            }

            SLogger.Write($"==== 결과: PASS {pass}개 / FAIL {fail}개 ====");

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        // ── Step 1: MatchAlgorithm으로 건반 위치 찾기 ───────────────────
        private List<Rect> FindKeysByMatchAlgorithm(Model curMode)
        {
            List<Rect> keyRects = new List<Rect>();

            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);

                var matchAlgo = window.AlgorithmList
                    .FirstOrDefault(a => a is MatchAlgorithm) as MatchAlgorithm;

                if (matchAlgo == null || !matchAlgo.IsUse) continue;

                matchAlgo.DoInspect();
                matchAlgo.GetResultRect(out List<DrawInspectInfo> results);

                if (results == null || results.Count == 0) continue;

                // 결과 rect를 건반 ROI로 사용 (X 오름차순 정렬)
                foreach (var r in results.OrderBy(r => r.rect.X))
                    keyRects.Add(r.rect);
            }

            return keyRects;
        }

        // ── 볼트 위치 enum ───────────────────────────────────────────────
        private enum BoltPosition { Top, Bottom }

        // ── Step 2-A: 볼트 확인 ─────────────────────────────────────────
        // 건반 상단(15~25%) 또는 하단(75~85%) 영역의 픽셀 분석
        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos,
                                List<DrawInspectInfo> displayList)
        {
            // 볼트 ROI 비율 설정
            float yRatioCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            float yRatioHalf = 0.07f;  // 위아래로 7% 범위

            int roiX = key.X + (int)(key.Width * 0.15f);
            int roiW = (int)(key.Width * 0.70f);
            int roiY = key.Y + (int)(key.Height * (yRatioCenter - yRatioHalf));
            int roiH = (int)(key.Height * yRatioHalf * 2f);

            // 이미지 경계 클램핑
            roiX = Math.Max(0, roiX);
            roiY = Math.Max(0, roiY);
            roiW = Math.Min(roiW, grayMat.Width - roiX);
            roiH = Math.Min(roiH, grayMat.Height - roiY);

            if (roiW <= 0 || roiH <= 0) return false;

            Rect boltRoi = new Rect(roiX, roiY, roiW, roiH);

            using (Mat roiMat = new Mat(grayMat, boltRoi))
            {
                // 볼트는 어두운 원형 → 평균 밝기가 낮고 표준편차가 큼
                Cv2.MeanStdDev(roiMat, out Scalar mean, out Scalar stddev);

                double avgBrightness = mean.Val0;
                double stdDev = stddev.Val0;

                // 볼트 판단 기준:
                // - 평균 밝기 < 180 (볼트의 어두운 금속)
                // - 표준편차 > 15  (밝기 변화 있음 = 볼트 형태)
                bool boltFound = avgBrightness < 180.0 && stdDev > 15.0;

                string label = pos == BoltPosition.Top ? "상단볼트" : "하단볼트";
                string resultText = boltFound
                    ? $"{label} OK ({avgBrightness:F0}/{stdDev:F1})"
                    : $"{label} MISSING ({avgBrightness:F0}/{stdDev:F1})";

                DecisionType dec = boltFound ? DecisionType.Good : DecisionType.Defect;
                displayList.Add(new DrawInspectInfo(boltRoi, resultText, InspectType.InspNone, dec));

                SLogger.Write($"  [{label}] avg={avgBrightness:F1}, std={stdDev:F1} → {(boltFound ? "OK" : "MISSING")}");
                return boltFound;
            }
        }

        // ── Step 2-B: 각인 확인 ─────────────────────────────────────────
        // 건반 하단 60~80% 영역의 픽셀 분석
        private bool CheckMark(Mat grayMat, Rect key,
                                List<DrawInspectInfo> displayList)
        {
            int roiX = key.X + (int)(key.Width * 0.10f);
            int roiW = (int)(key.Width * 0.80f);
            int roiY = key.Y + (int)(key.Height * 0.60f);
            int roiH = (int)(key.Height * 0.20f);

            roiX = Math.Max(0, roiX);
            roiY = Math.Max(0, roiY);
            roiW = Math.Min(roiW, grayMat.Width - roiX);
            roiH = Math.Min(roiH, grayMat.Height - roiY);

            if (roiW <= 0 || roiH <= 0) return false;

            Rect markRoi = new Rect(roiX, roiY, roiW, roiH);

            using (Mat roiMat = new Mat(grayMat, markRoi))
            {
                // 각인은 표면에 눌린 자국 → 표준편차가 일정 수준 이상
                Cv2.MeanStdDev(roiMat, out Scalar mean, out Scalar stddev);

                double stdDev = stddev.Val0;
                bool markFound = stdDev > 5.0;  // 각인 있으면 명암 변화 큼

                string resultText = markFound
                    ? $"각인 OK ({stdDev:F1})"
                    : $"각인 NG ({stdDev:F1})";

                DecisionType dec = markFound ? DecisionType.Good : DecisionType.Defect;
                displayList.Add(new DrawInspectInfo(markRoi, resultText, InspectType.InspNone, dec));

                SLogger.Write($"  [각인] std={stdDev:F1} → {(markFound ? "OK" : "NG")}");
                return markFound;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // 기존 기능 유지
        // ════════════════════════════════════════════════════════════════
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
                            roi, "PairArea", InspectType.InspNone, DecisionType.Good));

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

                            displayList.Add(new DrawInspectInfo(
                                markRoi,
                                $"Mark: {(isExist ? "OK" : "NG")} ({score:F1})",
                                InspectType.InspNone, dec));
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