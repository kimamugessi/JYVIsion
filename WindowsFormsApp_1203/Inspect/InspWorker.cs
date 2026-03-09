using JYVision.Algorithm;
using JYVision.Core;
using JYVision.Teach;
using JYVision.Util;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JYVision.Inspect
{
    //===== 시각 검사 실행 및 결과 처리 워커 클래스 =====
    public class InspWorker
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly InspectBoard _inspectBoard = new InspectBoard();
        private readonly List<Rect> _lastMatchedKeyRects = new List<Rect>();

        public bool IsRunning { get; set; } = false;
        private int _boltNgCount = 0;
        private int _markNgCount = 0;

        public InspWorker() { }

        //===== [그룹 1] 엔진 및 루프 제어 =====

        public void StartCycleInspectImage()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            Task.Run(() => InspectionLoop(this, _cts.Token));
        }

        public void Stop() => _cts.Cancel();

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

        //===== [그룹 2] 검사 실행 메인 흐름 =====

        // ✅ 잔상 방지: 화면 그리기 제거, 결과 리스트만 반환
        public List<DrawInspectInfo> RunInspect(out bool isDefect)
        {
            isDefect = false;
            Model curMode = Global.Inst.InspStage.CurModel;
            List<DrawInspectInfo> finalDisplayList = new List<DrawInspectInfo>();

            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                foreach (var algo in window.AlgorithmList)
                {
                    algo.DoInspect();
                    if (algo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                        finalDisplayList.AddRange(results);
                }
            }

            return finalDisplayList;
        }

        public bool TryInspect(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null)
            {
                RunInspect(out _);
                return true;
            }
            if (!UpdateInspData(inspObj)) return false;
            _inspectBoard.Inspect(inspObj);
            return DisplayResult(inspObj, inspType);
        }

        //===== [그룹 3] 건반 위치 탐색 및 보정 =====

        // ✅ 잔상 방지: 화면 그리기 제거, 결과 리스트만 반환
        public List<DrawInspectInfo> RunKeyMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            _lastMatchedKeyRects.Clear();
            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty()) return displayList;

            var matchedRects = new List<Rect>();
            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                var matchAlgo = window.AlgorithmList.OfType<MatchAlgorithm>().FirstOrDefault(a => a.IsUse);
                if (matchAlgo == null) continue;

                matchAlgo.DoInspect();
                if (matchAlgo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                    matchedRects.AddRange(results.OrderBy(r => r.rect.X).Select(r => r.rect));
            }

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            var candidateRects = new List<Rect>();

            foreach (var matched in matchedRects)
            {
                Rect keyRect = FindActualKeyRect(colorMat, grayMat, matched);
                candidateRects.Add(keyRect);
            }

            if (candidateRects.Count > 0)
            {
                int maxH = candidateRects.Max(r => r.Height);
                int minValidHeight = (int)(maxH * 0.5f);
                foreach (var keyRect in candidateRects)
                {
                    bool isBoundaryClipped = keyRect.X <= 5
                                          || keyRect.Right >= colorMat.Width - 5
                                          || keyRect.Y <= 5
                                          || keyRect.Bottom >= colorMat.Height - 5;

                    if (!isBoundaryClipped && keyRect.Height < minValidHeight) continue;

                    _lastMatchedKeyRects.Add(keyRect);
                    displayList.Add(new DrawInspectInfo(keyRect, $"Key H={keyRect.Height}", InspectType.InspNone, DecisionType.Good));
                }
            }

            return displayList;
        }

        private Rect FindActualKeyRect(Mat colorMat, Mat grayMat, Rect matched)
        {
            int imgH = colorMat.Height;
            int imgW = colorMat.Width;

            int[] sampleYs = new[] { 0.35f, 0.50f, 0.65f }
                .Select(r => Math.Max(0, Math.Min(matched.Y + (int)(matched.Height * r), imgH - 1)))
                .ToArray();

            int[] scanCols = Enumerable.Range(0, 5)
                .Select(i => Math.Max(0, Math.Min((int)(matched.X + matched.Width * (0.2f + i * 0.15f)), imgW - 1)))
                .ToArray();

            int sumB = 0, sumG = 0, sumR = 0, cnt = 0;
            foreach (int sy in sampleYs)
            {
                foreach (int x in scanCols)
                {
                    Vec3b px = colorMat.At<Vec3b>(sy, x);
                    if ((px.Item0 + px.Item1 + px.Item2) / 3 < 40) continue;
                    sumB += px.Item0; sumG += px.Item1; sumR += px.Item2; cnt++;
                }
            }

            if (cnt == 0) return matched;
            Vec3b refColor = new Vec3b((byte)(sumB / cnt), (byte)(sumG / cnt), (byte)(sumR / cnt));

            bool isYellow = refColor.Item2 > 150 && refColor.Item1 > 150 && refColor.Item0 < 100;
            bool isRed = refColor.Item2 > 150 && refColor.Item1 < 100 && refColor.Item0 < 100;
            bool isBlue = refColor.Item0 > 100 && refColor.Item2 < 100;

            int colorTolerance = isYellow ? 80 : isRed ? 70 : isBlue ? 65 : 60;
            int gapLimit = isYellow ? 40 : isRed ? 35 : 30;

            int centerY = matched.Y + (int)(matched.Height * 0.5f);
            int topY = centerY;
            int bottomY = centerY;
            int gap = 0;

            int topScanLimit = Math.Max(0, matched.Y - (int)(matched.Height * 0.25f));
            for (int y = centerY; y >= topScanLimit; y--)
            {
                bool match = scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) >= 3;
                if (match) { topY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            gap = 0;
            for (int y = centerY; y <= Math.Min(imgH - 1, matched.Bottom); y++)
            {
                bool match = scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) >= 3;
                if (match) { bottomY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            int margin = Math.Max(10, (int)((bottomY - topY) * 0.05f));
            int finalTop = Math.Max(0, topY);
            int finalBottom = Math.Min(imgH - 1, bottomY + margin);

            return new Rect(matched.X, finalTop, matched.Width, finalBottom - finalTop);
        }

        private bool IsColorMatch(Vec3b px, Vec3b refColor, int tolerance) =>
            Math.Abs(px.Item0 - refColor.Item0) <= tolerance &&
            Math.Abs(px.Item1 - refColor.Item1) <= tolerance &&
            Math.Abs(px.Item2 - refColor.Item2) <= tolerance;

        //===== [그룹 4] 세부 부품(볼트/각인) 검사 =====

        // ✅ 잔상 방지: 화면 그리기 제거, 결과 리스트만 반환
        public List<DrawInspectInfo> RunBoltMark()
        {
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            if (_lastMatchedKeyRects.Count == 0) return displayList;

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null || grayMat.Empty()) return displayList;

            _boltNgCount = 0;
            _markNgCount = 0;

            foreach (Rect key in _lastMatchedKeyRects)
            {
                displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));

                if (!CheckBolt(grayMat, key, BoltPosition.Top, displayList)) _boltNgCount++;
                if (!CheckBolt(grayMat, key, BoltPosition.Bottom, displayList)) _boltNgCount++;
                if (!CheckMark(grayMat, key, displayList)) _markNgCount++;
            }

            SendResultToForm(_boltNgCount, _markNgCount);

            return displayList;
        }

        private enum BoltPosition { Top, Bottom }

        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos, List<DrawInspectInfo> displayList)
        {
            float yCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            Rect boltRoi = new Rect(
                key.X + (int)(key.Width * 0.2f),
                key.Y + (int)(key.Height * (yCenter - 0.12f)),
                (int)(key.Width * 0.6f),
                (int)(key.Height * 0.24f));
            boltRoi = boltRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (boltRoi.Width <= 0 || boltRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(grayMat, boltRoi))
            {
                CircleSegment[] circles = Cv2.HoughCircles(
                    roiMat, HoughModes.Gradient, 1.0, roiMat.Width,
                    50, 18, roiMat.Width / 6, roiMat.Width / 2);
                bool boltFound = false;

                if (circles?.Length > 0)
                {
                    var c = circles[0];
                    Rect innerRect = new Rect(
                        (int)c.Center.X - (int)(c.Radius * 0.6f),
                        (int)c.Center.Y - (int)(c.Radius * 0.6f),
                        (int)(c.Radius * 1.2f),
                        (int)(c.Radius * 1.2f));
                    innerRect = innerRect.Intersect(new Rect(0, 0, roiMat.Width, roiMat.Height));

                    if (innerRect.Width > 0 && innerRect.Height > 0)
                    {
                        using (Mat inner = new Mat(roiMat, innerRect))
                        {
                            Cv2.MinMaxLoc(inner, out _, out double innerMax);
                            Cv2.MeanStdDev(inner, out Scalar iMean, out _);
                            boltFound = (innerMax / iMean.Val0) > 1.6
                                     && innerMax > 120.0
                                     && iMean.Val0 > 45.0;
                        }
                    }
                }

                displayList.Add(new DrawInspectInfo(
                    boltRoi,
                    $"{(pos == BoltPosition.Top ? "Top" : "Bot")} Bolt {(boltFound ? "OK" : "NG")}",
                    InspectType.InspNone,
                    boltFound ? DecisionType.Good : DecisionType.Defect));
                return boltFound;
            }
        }

        private bool CheckMark(Mat grayMat, Rect key, List<DrawInspectInfo> displayList)
        {
            Rect markRoi = new Rect(
                key.X + (int)(key.Width * 0.3f),
                key.Y + (int)(key.Height * 0.5f),
                (int)(key.Width * 0.4f),
                (int)(key.Height * 0.25f));
            markRoi = markRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (markRoi.Width <= 0 || markRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(grayMat, markRoi))
            {
                Cv2.MeanStdDev(roiMat, out _, out Scalar stddev);
                using (Mat lap = new Mat())
                {
                    Cv2.Laplacian(roiMat, lap, MatType.CV_64F);
                    Cv2.MeanStdDev(lap, out _, out Scalar lapStd);
                    bool markFound = stddev.Val0 > 3.0 && lapStd.Val0 > 1.5;
                    displayList.Add(new DrawInspectInfo(
                        markRoi,
                        $"Mark {(markFound ? "OK" : "NG")}",
                        InspectType.InspNone,
                        markFound ? DecisionType.Good : DecisionType.Defect));
                    return markFound;
                }
            }
        }

        //===== [그룹 5] 데이터 동기화 및 출력 제어 =====

        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null) return;

            _boltNgCount = 0;
            _markNgCount = 0;

            foreach (Rect key in _lastMatchedKeyRects)
            {
                if (showKeyboard)
                    displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));

                if (showBolt)
                {
                    if (!CheckBolt(grayMat, key, BoltPosition.Top, displayList)) _boltNgCount++;
                    if (!CheckBolt(grayMat, key, BoltPosition.Bottom, displayList)) _boltNgCount++;
                }

                if (showMark)
                {
                    if (!CheckMark(grayMat, key, displayList)) _markNgCount++;
                }
            }

            // ✅ 결과창 중복 누적 방지: 화면 옵션만 변경할 때는 결과를 다시 보내지 않습니다.
            // SendResultToForm(_boltNgCount, _markNgCount); 

            cameraForm?.ResetDisplay();
            if (displayList.Count > 0) cameraForm?.AddRect(displayList);
        }

        private void SendResultToForm(int boltNg, int markNg)
        {
            var resultForm = MainForm.GetDockForm<ResultForm>();
            if (resultForm == null) return;

            Bitmap captured = null;
            try
            {
                Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
                if (colorMat != null && !colorMat.Empty())
                {
                    Mat bgr = new Mat();
                    if (colorMat.Channels() == 1)
                        Cv2.CvtColor(colorMat, bgr, ColorConversionCodes.GRAY2BGR);
                    else
                        bgr = colorMat.Clone();

                    captured = BitmapConverter.ToBitmap(bgr);
                    bgr.Dispose();
                }
            }
            catch { }

            resultForm.UpdateNgSummary(boltNg, markNg, captured);
        }

        public bool UpdateInspData(InspWindow inspWindow)
        {
            if (inspWindow == null) return false;
            inspWindow.PatternLearn();
            foreach (var algo in inspWindow.AlgorithmList)
            {
                algo.TeachRect = algo.InspRect = inspWindow.WindowArea;
                algo.SetInspData(Global.Inst.InspStage.GetMat(0, algo.ImageChannel));
            }
            return true;
        }

        private bool DisplayResult(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) return false;
            List<DrawInspectInfo> totalArea = new List<DrawInspectInfo>();
            foreach (var algorithm in inspObj.AlgorithmList)
            {
                if (inspType != InspectType.InspNone && algorithm.InspectType != inspType) continue;
                if (algorithm.GetResultRect(out List<DrawInspectInfo> resultArea) > 0) totalArea.AddRange(resultArea);
            }
            if (totalArea.Count > 0) MainForm.GetDockForm<CameraForm>()?.AddRect(totalArea);
            return true;
        }
    }
}