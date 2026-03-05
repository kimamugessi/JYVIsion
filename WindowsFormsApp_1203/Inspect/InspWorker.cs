using JYVision.Algorithm;
using JYVision.Core;
using JYVision.Teach;
using JYVision.Util;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms.VisualStyles;

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
        private List<Rect> _lastMatchedKeyRects = new List<Rect>();
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

            // 1. 기존에 저장된 좌표 데이터 초기화
            _lastMatchedKeyRects.Clear();

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

                // X축 순서대로 정렬하여 리스트에 추가
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

            // ── Step 2. 각 매칭 위치에서 실제 건반 높이 탐지 및 저장 ────────────
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            foreach (var matched in matchedRects)
            {
                // 알고리즘으로 실제 건반의 경계를 찾아 ROI 보정
                Rect keyRect = FindActualKeyRect(colorMat, grayMat, matched);
                
                // [핵심] 보정된 ROI를 멤버 변수에 저장 (RunOnlyBoltMatch에서 사용됨)
                _lastMatchedKeyRects.Add(keyRect);

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
            int imgH = colorMat.Height;
            int imgW = colorMat.Width;

            // 1. 기준색 샘플링: 볼트가 없는 중앙 영역(40~60%) 여러 점 평균
            int sampleY = matched.Y + (int)(matched.Height * 0.5f);
            sampleY = Math.Max(0, Math.Min(sampleY, imgH - 1));

            int[] scanCols = new int[5];
            for (int i = 0; i < 5; i++)
            {
                float ratio = 0.2f + i * 0.15f;
                scanCols[i] = Math.Max(0, Math.Min(
                    matched.X + (int)(matched.Width * ratio), imgW - 1));
            }

            // 5열 BGR 평균으로 기준색 산출
            int sumB = 0, sumG = 0, sumR = 0, cnt = 0;
            foreach (int x in scanCols)
            {
                // 볼트 회피: 해당 열이 너무 어두우면 샘플 제외
                Vec3b px = colorMat.At<Vec3b>(sampleY, x);
                int brightness = (px.Item0 + px.Item1 + px.Item2) / 3;
                if (brightness < 40) continue; // 볼트(어두운 원) 제외
                sumB += px.Item0; sumG += px.Item1; sumR += px.Item2;
                cnt++;
            }
            if (cnt == 0) return new Rect(matched.X, matched.Y, matched.Width, matched.Height);

            Vec3b refColor = new Vec3b(
                (byte)(sumB / cnt), (byte)(sumG / cnt), (byte)(sumR / cnt));
            SLogger.Write($"  [기준색] B={refColor.Item0} G={refColor.Item1} R={refColor.Item2}");

            // 2. 위로 스캔 (볼트 크기만큼 gap 허용 = 80px)
            int topY = sampleY;
            int gap = 0;
            for (int y = sampleY; y >= 0; y--)
            {
                int matchCount = 0;
                foreach (int x in scanCols)
                    if (IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, 45)) matchCount++;

                if (matchCount >= 3) { topY = y; gap = 0; }
                else if (++gap > 80) break;
            }

            // 3. 아래로 스캔 (볼트 크기만큼 gap 허용 = 80px)
            int bottomY = sampleY;
            gap = 0;
            for (int y = sampleY; y < imgH; y++)
            {
                int matchCount = 0;
                foreach (int x in scanCols)
                    if (IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, 45)) matchCount++;

                if (matchCount >= 3) { bottomY = y; gap = 0; }
                else if (++gap > 80) break;
            }

            int finalH = bottomY - topY;
            SLogger.Write($"  [결과] top={topY} bottom={bottomY} H={finalH}");
            return new Rect(matched.X, topY, matched.Width, finalH);
        }

        private bool IsColorMatch(Vec3b px, Vec3b refColor, int tolerance)
        {
            return Math.Abs(px.Item0 - refColor.Item0) <= tolerance &&
                   Math.Abs(px.Item1 - refColor.Item1) <= tolerance &&
                   Math.Abs(px.Item2 - refColor.Item2) <= tolerance;
        }

        public void RunOnlyBoltMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            SLogger.Write("==== 건반 볼트/각인 검사 시작 ====");

            // ── Step 1. RunKeyMatch에서 저장했던 리스트 가져오기 ─────────────
            // 새로 찾지 않고 이전에 저장된 _lastMatchedKeyRects를 그대로 사용
            List<Rect> keyRects = _lastMatchedKeyRects;

            if (keyRects.Count == 0)
            {
                SLogger.Write("[Error] 감지된 건반 데이터가 없습니다. 건반 매칭을 먼저 실행하세요.");
                cameraForm?.ResetDisplay();
                return;
            }

            SLogger.Write($"[건반 로드] {keyRects.Count}개의 위치 정보를 기반으로 검사를 시작합니다.");

            // ── Step 2. 저장된 각 건반 좌표마다 볼트 & 각인 확인 ─────────────────────
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            int pass = 0;
            int fail = 0;

            foreach (Rect key in keyRects)
            {
                // 건반 ROI 표시 (파란 박스)
                displayList.Add(new DrawInspectInfo(
                    key, $"Key ROI", InspectType.InspNone, DecisionType.Good));

                // 실제 검사 수행 (CheckBolt, CheckMark는 내부 정의된 함수 사용)
                bool topBoltOk = CheckBolt(grayMat, key, BoltPosition.Top, displayList);
                bool bottomBoltOk = CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                bool markOk = CheckMark(grayMat, key, displayList);

                bool keyOk = topBoltOk && bottomBoltOk && markOk;

                SLogger.Write($"[검사 X={key.X}] 상단:{(topBoltOk ? "OK" : "NG")} " +
                              $"하단:{(bottomBoltOk ? "OK" : "NG")} " +
                              $"각인:{(markOk ? "OK" : "NG")}");

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
            float yRatioCenter = (pos == BoltPosition.Top) ? 0.15f : 0.85f;
            float yRatioHalf = 0.1f;  // 위아래로 7% 범위

            int roiX = key.X + (int)(key.Width * 0.20f);
            int roiW = (int)(key.Width * 0.60f);
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
            int roiX = key.X + (int)(key.Width * 0.20f);
            int roiW = (int)(key.Width * 0.60f);
            int roiY = key.Y + (int)(key.Height * 0.50f);
            int roiH = (int)(key.Height * 0.25f);

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
    }
}