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
        // 건반 매칭
        // ════════════════════════════════════════════════════════════════
        public void RunKeyMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            SLogger.Write("==== 건반 매칭 시작 ====");
            _lastMatchedKeyRects.Clear();

            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty())
            {
                SLogger.Write("[Error] 이미지 없음");
                return;
            }

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

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            foreach (var matched in matchedRects)
            {
                Rect keyRect = FindActualKeyRect(colorMat, grayMat, matched);
                _lastMatchedKeyRects.Add(keyRect);

                SLogger.Write($"[건반] X={keyRect.X} W={keyRect.Width} H={keyRect.Height}");
                displayList.Add(new DrawInspectInfo(
                    keyRect, $"Key H={keyRect.Height}",
                    InspectType.InspNone, DecisionType.Good));
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
            SLogger.Write($"==== 건반 {displayList.Count}개 완료 ====");
        }

        // ════════════════════════════════════════════════════════════════
        // FindActualKeyRect
        // - 노란 건반: tolerance 80, gap 40
        // - 스캔 범위: matched.Height * 1.5 이내
        // - 경계 패딩 15px
        // ════════════════════════════════════════════════════════════════
        private Rect FindActualKeyRect(Mat colorMat, Mat grayMat, Rect matched)
        {
            int imgH = colorMat.Height;
            int imgW = colorMat.Width;

            int sampleY = matched.Y + (int)(matched.Height * 0.5f);
            sampleY = Math.Max(0, Math.Min(sampleY, imgH - 1));

            int[] scanCols = new int[5];
            for (int i = 0; i < 5; i++)
            {
                float ratio = 0.2f + i * 0.15f;
                scanCols[i] = Math.Max(0, Math.Min(
                    matched.X + (int)(matched.Width * ratio), imgW - 1));
            }

            int sumB = 0, sumG = 0, sumR = 0, cnt = 0;
            foreach (int x in scanCols)
            {
                Vec3b px = colorMat.At<Vec3b>(sampleY, x);
                int brightness = (px.Item0 + px.Item1 + px.Item2) / 3;
                if (brightness < 40) continue;
                sumB += px.Item0; sumG += px.Item1; sumR += px.Item2;
                cnt++;
            }
            if (cnt == 0) return new Rect(matched.X, matched.Y, matched.Width, matched.Height);

            Vec3b refColor = new Vec3b(
                (byte)(sumB / cnt), (byte)(sumG / cnt), (byte)(sumR / cnt));
            SLogger.Write($"  [기준색] B={refColor.Item0} G={refColor.Item1} R={refColor.Item2}");

            // 노란색 감지 → tolerance/gap 확대
            bool isYellow      = refColor.Item2 > 150 && refColor.Item1 > 150 && refColor.Item0 < 100;
            int colorTolerance = isYellow ? 80 : 60;
            int gapLimit       = isYellow ? 40 : 25;

            SLogger.Write($"  [색상판단] isYellow={isYellow} tol={colorTolerance} gap={gapLimit}");

            int maxScan    = (int)(matched.Height * 1.5f);
            int scanTop    = Math.Max(0,        matched.Y      - maxScan);
            int scanBottom = Math.Min(imgH - 1, matched.Bottom + maxScan);

            // 위로 스캔
            int topY = sampleY, gap = 0;
            for (int y = sampleY; y >= scanTop; y--)
            {
                int matchCount = 0;
                foreach (int x in scanCols)
                    if (IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) matchCount++;

                if (matchCount >= 3) { topY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            // 아래로 스캔
            int bottomY = sampleY;
            gap = 0;
            for (int y = sampleY; y <= scanBottom; y++)
            {
                int matchCount = 0;
                foreach (int x in scanCols)
                    if (IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) matchCount++;

                if (matchCount >= 3) { bottomY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            // 경계 패딩
            int padding = 15;
            topY    = Math.Max(0,        topY    - padding);
            bottomY = Math.Min(imgH - 1, bottomY + padding);
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

            List<Rect> keyRects = _lastMatchedKeyRects;

            if (keyRects.Count == 0)
            {
                SLogger.Write("[Error] 감지된 건반 데이터가 없습니다. 건반 매칭을 먼저 실행하세요.");
                cameraForm?.ResetDisplay();
                return;
            }

            SLogger.Write($"[건반 로드] {keyRects.Count}개의 위치 정보를 기반으로 검사를 시작합니다.");

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            int pass = 0, fail = 0;

            foreach (Rect key in keyRects)
            {
                displayList.Add(new DrawInspectInfo(
                    key, $"Key ROI", InspectType.InspNone, DecisionType.Good));

                bool topBoltOk    = CheckBolt(grayMat, key, BoltPosition.Top,    displayList);
                bool bottomBoltOk = CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                bool markOk       = CheckMark(grayMat, key, displayList);
                bool keyOk        = topBoltOk && bottomBoltOk && markOk;

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

        private enum BoltPosition { Top, Bottom }

        // ════════════════════════════════════════════════════════════════
        // CheckBolt
        // - ROI: 상단 20%, 하단 80% / ±12%
        // - HoughCircles로 원형 감지
        // - 볼트 판단: ratio > 1.6 AND innerMax > 80
        //   (빈 구멍: ratio ≈ 1.0, innerMax 낮음)
        // ════════════════════════════════════════════════════════════════
        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos,
                                List<DrawInspectInfo> displayList)
        {
            float yRatioCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            float yRatioHalf   = 0.12f;

            int roiX = key.X + (int)(key.Width  * 0.20f);
            int roiW = (int)(key.Width  * 0.60f);
            int roiY = key.Y + (int)(key.Height * (yRatioCenter - yRatioHalf));
            int roiH = (int)(key.Height * yRatioHalf * 2f);

            roiX = Math.Max(0, roiX);
            roiY = Math.Max(0, roiY);
            roiW = Math.Min(roiW, grayMat.Width  - roiX);
            roiH = Math.Min(roiH, grayMat.Height - roiY);

            if (roiW <= 0 || roiH <= 0) return false;

            Rect boltRoi = new Rect(roiX, roiY, roiW, roiH);

            using (Mat roiMat = new Mat(grayMat, boltRoi))
            {
                Cv2.MeanStdDev(roiMat, out Scalar mean, out Scalar stddev);
                double avgBrightness = mean.Val0;
                double stdDev        = stddev.Val0;

                CircleSegment[] circles = Cv2.HoughCircles(
                    roiMat,
                    HoughModes.Gradient,
                    dp: 1.0,
                    minDist: roiMat.Width,
                    param1: 50,
                    param2: 18,
                    minRadius: roiMat.Width / 6,
                    maxRadius: roiMat.Width / 2);

                bool circleFound = circles != null && circles.Length > 0;
                bool boltFound   = false;

                if (circleFound)
                {
                    var c  = circles[0];
                    int r  = (int)(c.Radius * 0.6f);
                    int cx = (int)c.Center.X;
                    int cy = (int)c.Center.Y;

                    int innerX = Math.Max(0, cx - r);
                    int innerY = Math.Max(0, cy - r);
                    int innerW = Math.Min(r * 2, roiMat.Width  - innerX);
                    int innerH = Math.Min(r * 2, roiMat.Height - innerY);

                    if (innerW > 0 && innerH > 0)
                    {
                        using (Mat inner = new Mat(roiMat, new Rect(innerX, innerY, innerW, innerH)))
                        {
                            Cv2.MinMaxLoc(inner, out _, out double innerMax);
                            Cv2.MeanStdDev(inner, out Scalar iMean, out _);
                            double innerAvg = iMean.Val0;
                            double ratio    = (innerAvg > 1) ? innerMax / innerAvg : 1.0;

                            // 볼트: 반사점 비율 높고 절대값도 충분
                            // 구멍: 균일하게 어두워서 ratio ≈ 1.0, innerMax 낮음
                            boltFound = ratio > 1.6 && innerMax > 80.0;

                            SLogger.Write($"  innerAvg={innerAvg:F1} innerMax={innerMax:F1} ratio={ratio:F2}");
                        }
                    }
                }

                string label      = pos == BoltPosition.Top ? "상단볼트" : "하단볼트";
                string resultText = boltFound
                    ? $"{label} OK ({avgBrightness:F0}/{stdDev:F1})"
                    : $"{label} MISSING ({avgBrightness:F0}/{stdDev:F1})";

                DecisionType dec = boltFound ? DecisionType.Good : DecisionType.Defect;
                displayList.Add(new DrawInspectInfo(boltRoi, resultText, InspectType.InspNone, dec));
                SLogger.Write($"  [{label}] circle={circleFound}, bolt={boltFound} → {(boltFound ? "OK" : "MISSING")}");
                return boltFound;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // CheckMark
        // - ROI X: 30~70% (배경/노란봉 침범 방지)
        // - Laplacian 엣지 감지
        // - 판단: stdDev > 3.0 AND lapScore > 1.5
        // ════════════════════════════════════════════════════════════════
        private bool CheckMark(Mat grayMat, Rect key,
                                List<DrawInspectInfo> displayList)
        {
            int roiX = key.X + (int)(key.Width  * 0.30f);
            int roiW = (int)(key.Width  * 0.40f);
            int roiY = key.Y + (int)(key.Height * 0.50f);
            int roiH = (int)(key.Height * 0.25f);

            roiX = Math.Max(0, roiX);
            roiY = Math.Max(0, roiY);
            roiW = Math.Min(roiW, grayMat.Width  - roiX);
            roiH = Math.Min(roiH, grayMat.Height - roiY);

            if (roiW <= 0 || roiH <= 0) return false;

            Rect markRoi = new Rect(roiX, roiY, roiW, roiH);

            using (Mat roiMat = new Mat(grayMat, markRoi))
            {
                Cv2.MeanStdDev(roiMat, out Scalar mean, out Scalar stddev);
                double stdDev = stddev.Val0;

                Mat lap = new Mat();
                Cv2.Laplacian(roiMat, lap, MatType.CV_64F);
                Cv2.MeanStdDev(lap, out Scalar lapMean, out Scalar lapStd);
                double lapScore = lapStd.Val0;
                lap.Dispose();

                // AND 조건: OR이면 배경 노이즈 오판정 가능
                bool markFound = stdDev > 3.0 && lapScore > 1.5;

                string resultText = markFound
                    ? $"각인 OK ({stdDev:F1}/{lapScore:F1})"
                    : $"각인 NG ({stdDev:F1}/{lapScore:F1})";

                DecisionType dec = markFound ? DecisionType.Good : DecisionType.Defect;
                displayList.Add(new DrawInspectInfo(markRoi, resultText, InspectType.InspNone, dec));
                SLogger.Write($"  [각인] std={stdDev:F1} lap={lapScore:F1} → {(markFound ? "OK" : "NG")}");
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
                        int paddingX    = (int)(roi.Width * 0.1);
                        int innerWidth  = roi.Width - (paddingX * 2);
                        int innerHeight = roi.Height;
                        int markHeight  = (int)(innerHeight * 0.5);

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
                inspAlgo.InspRect  = windowArea;
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