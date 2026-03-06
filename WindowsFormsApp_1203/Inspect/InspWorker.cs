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

namespace JYVision.Inspect
{
    /// <summary>
    /// 시각 검사 실행 및 결과 처리를 담당하는 워커 클래스
    /// </summary>
    public class InspWorker
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private InspectBoard _inspectBoard = new InspectBoard();
        private List<Rect> _lastMatchedKeyRects = new List<Rect>(); // 마지막 매칭된 건반 영역 저장

        public bool IsRunning { get; set; } = false;

        public InspWorker() { }

        // --- 루프 제어 ---
        public void StartCycleInspectImage()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => InspectionLoop(this, _cts.Token));
        }

        public void Stop() => _cts.Cancel();

        private void InspectionLoop(InspWorker inspWorker, CancellationToken token)
        {
            Global.Inst.InspStage.SetWorkingState(WorkingState.INSPECT);
            IsRunning = true;
            while (!token.IsCancellationRequested)
                Global.Inst.InspStage.OneCycle();
            IsRunning = false;
        }

        // --- 양산 검사 (Main) ---
        public bool RunInspect(out bool isDefect)
        {
            isDefect = false;
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> finalDisplayList = new List<DrawInspectInfo>();

            // 모든 검사 윈도우 및 알고리즘 실행
            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                foreach (var algo in window.AlgorithmList)
                {
                    algo.DoInspect();
                    algo.GetResultRect(out List<DrawInspectInfo> results);
                    finalDisplayList.AddRange(results);
                }
            }

            // 결과 화면 갱신
            if (cameraForm != null)
            {
                cameraForm.ResetDisplay();
                if (finalDisplayList.Count > 0) cameraForm.AddRect(finalDisplayList);
            }
            return true;
        }

        // --- 건반 매칭 및 영역 최적화 ---
        public void RunKeyMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            _lastMatchedKeyRects.Clear();
            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty()) return;

            // 1. 템플릿 매칭으로 기초 위치 탐색
            var matchedRects = new List<Rect>();
            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                var matchAlgo = window.AlgorithmList.OfType<MatchAlgorithm>().FirstOrDefault(a => a.IsUse);
                if (matchAlgo == null) continue;

                matchAlgo.DoInspect();
                matchAlgo.GetResultRect(out List<DrawInspectInfo> results);
                if (results != null) matchedRects.AddRange(results.OrderBy(r => r.rect.X).Select(r => r.rect));
            }

            // 2. 색상 기반으로 실제 건반 영역(Height) 정밀 추출
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            foreach (var matched in matchedRects)
            {
                Rect keyRect = FindActualKeyRect(colorMat, grayMat, matched);
                _lastMatchedKeyRects.Add(keyRect);
                displayList.Add(new DrawInspectInfo(keyRect, $"Key H={keyRect.Height}", InspectType.InspNone, DecisionType.Good));
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        // --- 건반 높이 정밀 탐색 로직 ---
        private Rect FindActualKeyRect(Mat colorMat, Mat grayMat, Rect matched)
        {
            int imgH = colorMat.Height;
            int imgW = colorMat.Width;
            int sampleY = Math.Max(0, Math.Min(matched.Y + (int)(matched.Height * 0.5f), imgH - 1));

            // 가로 5점 샘플링으로 기준 색상 추출
            int[] scanCols = Enumerable.Range(0, 5).Select(i => (int)(matched.X + matched.Width * (0.2f + i * 0.15f))).ToArray();
            int sumB = 0, sumG = 0, sumR = 0, cnt = 0;

            foreach (int x in scanCols)
            {
                Vec3b px = colorMat.At<Vec3b>(sampleY, x);
                if ((px.Item0 + px.Item1 + px.Item2) / 3 < 40) continue;
                sumB += px.Item0; sumG += px.Item1; sumR += px.Item2; cnt++;
            }

            if (cnt == 0) return matched;
            Vec3b refColor = new Vec3b((byte)(sumB / cnt), (byte)(sumG / cnt), (byte)(sumR / cnt));

            // 노란색 여부에 따른 허용치 설정
            bool isYellow = refColor.Item2 > 150 && refColor.Item1 > 150 && refColor.Item0 < 100;
            int colorTolerance = isYellow ? 80 : 60;
            int gapLimit = isYellow ? 40 : 25;

            // 상하 스캔을 통한 경계 결정
            int maxScan = (int)(matched.Height * 1.5f);
            int topY = sampleY, bottomY = sampleY, gap = 0;

            // 위쪽 스캔
            for (int y = sampleY; y >= Math.Max(0, matched.Y - maxScan); y--)
            {
                if (scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) >= 3) { topY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }
            // 아래쪽 스캔
            gap = 0;
            for (int y = sampleY; y <= Math.Min(imgH - 1, matched.Bottom + maxScan); y++)
            {
                if (scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) >= 3) { bottomY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            return new Rect(matched.X, Math.Max(0, topY - 15), matched.Width, Math.Min(imgH - 1, bottomY + 15) - Math.Max(0, topY - 15));
        }

        private bool IsColorMatch(Vec3b px, Vec3b refColor, int tolerance) =>
            Math.Abs(px.Item0 - refColor.Item0) <= tolerance &&
            Math.Abs(px.Item1 - refColor.Item1) <= tolerance &&
            Math.Abs(px.Item2 - refColor.Item2) <= tolerance;

        // --- 볼트 및 각인 유무 검사 ---
        public void RunOnlyBoltMatch()
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            foreach (Rect key in _lastMatchedKeyRects)
            {
                displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));

                // 각 건반별 상/하 볼트 및 각인 상태 검사
                bool topOk = CheckBolt(grayMat, key, BoltPosition.Top, displayList);
                bool botOk = CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                bool markOk = CheckMark(grayMat, key, displayList);
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        private enum BoltPosition { Top, Bottom }

        // 볼트 검사: 원형 검출 후 내부 명암비(Ratio) 및 밝기(Max) 분석
        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos, List<DrawInspectInfo> displayList)
        {
            float yCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            Rect boltRoi = new Rect(key.X + (int)(key.Width * 0.2f), key.Y + (int)(key.Height * (yCenter - 0.12f)), (int)(key.Width * 0.6f), (int)(key.Height * 0.24f));

            // 이미지 경계 예외 처리
            boltRoi = boltRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (boltRoi.Width <= 0 || boltRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(grayMat, boltRoi))
            {
                CircleSegment[] circles = Cv2.HoughCircles(roiMat, HoughModes.Gradient, 1.0, roiMat.Width, 50, 18, roiMat.Width / 6, roiMat.Width / 2);
                bool boltFound = false;

                if (circles?.Length > 0)
                {
                    var c = circles[0];
                    Rect innerRect = new Rect((int)c.Center.X - (int)(c.Radius * 0.6f), (int)c.Center.Y - (int)(c.Radius * 0.6f), (int)(c.Radius * 1.2f), (int)(c.Radius * 1.2f));
                    innerRect = innerRect.Intersect(new Rect(0, 0, roiMat.Width, roiMat.Height));

                    using (Mat inner = new Mat(roiMat, innerRect))
                    {
                        Cv2.MinMaxLoc(inner, out _, out double innerMax);
                        Cv2.MeanStdDev(inner, out Scalar iMean, out _);
                        // 볼트 특성: 반사광으로 인해 평균 대비 최대 밝기 비율이 높음
                        boltFound = (innerMax / iMean.Val0) > 1.6 && innerMax > 80.0;
                    }
                }

                displayList.Add(new DrawInspectInfo(boltRoi, $"{(pos == BoltPosition.Top ? "Top" : "Bot")} Bolt {(boltFound ? "OK" : "NG")}", InspectType.InspNone, boltFound ? DecisionType.Good : DecisionType.Defect));
                return boltFound;
            }
        }

        public void RunOnlyCheckMark()
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            foreach (Rect key in _lastMatchedKeyRects)
            {
                displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));
                bool markOk = CheckMark(grayMat, key, displayList);
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        // 각인 검사: 표준편차(표면 거칠기) 및 라플라시안(에지 강도) 분석
        private bool CheckMark(Mat grayMat, Rect key, List<DrawInspectInfo> displayList)
        {
            Rect markRoi = new Rect(key.X + (int)(key.Width * 0.3f), key.Y + (int)(key.Height * 0.5f), (int)(key.Width * 0.4f), (int)(key.Height * 0.25f));
            markRoi = markRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (markRoi.Width <= 0 || markRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(grayMat, markRoi))
            {
                Cv2.MeanStdDev(roiMat, out _, out Scalar stddev);
                Mat lap = new Mat();
                Cv2.Laplacian(roiMat, lap, MatType.CV_64F);
                Cv2.MeanStdDev(lap, out _, out Scalar lapStd);
                lap.Dispose();

                bool markFound = stddev.Val0 > 3.0 && lapStd.Val0 > 1.5;
                displayList.Add(new DrawInspectInfo(markRoi, $"Mark {(markFound ? "OK" : "NG")}", InspectType.InspNone, markFound ? DecisionType.Good : DecisionType.Defect));
                return markFound;
            }
        }

        // 체크박스 옵션에 따라 선택적으로 ROI 표시
        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            foreach (Rect key in _lastMatchedKeyRects)
            {
                // ✅ Keyboard 체크박스: 건반 외곽 ROI 표시
                if (showKeyboard)
                    displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));

                // ✅ Bolt 체크박스: 상/하 볼트 ROI 표시
                if (showBolt)
                {
                    CheckBolt(grayMat, key, BoltPosition.Top, displayList);
                    CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                }

                // ✅ Mark 체크박스: 각인 ROI 표시
                if (showMark)
                    CheckMark(grayMat, key, displayList);
            }

            cameraForm?.ResetDisplay();
            if (displayList.Count > 0)
                cameraForm?.AddRect(displayList);
        }

        public bool TryInspect(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) return RunInspect(out _);
            if (!UpdateInspData(inspObj)) return false;
            _inspectBoard.Inspect(inspObj);
            return DisplayResult(inspObj, inspType);
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