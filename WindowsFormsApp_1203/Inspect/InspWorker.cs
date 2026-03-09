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
    //===== 시각 검사 실행 및 결과 처리 워커 클래스 =====
    public class InspWorker
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly InspectBoard _inspectBoard = new InspectBoard();
        private readonly List<Rect> _lastMatchedKeyRects = new List<Rect>();

        public bool IsRunning { get; set; } = false;

        public InspWorker() { }

        //===== [그룹 1] 엔진 및 루프 제어 =====

        //------- 검사 루프 시작 -------
        public void StartCycleInspectImage()
        {
            _cts?.Cancel(); // 기존 작업이 돌고 있으면 일단 캔슬
            _cts = new CancellationTokenSource();
            Task.Run(() => InspectionLoop(this, _cts.Token)); // 새 토큰으로 검사 스레드 가동
        }

        //------- 루프 중지 -------
        public void Stop() => _cts.Cancel();

        //------- 내부 검사 스레드 루프 -------
        private void InspectionLoop(InspWorker inspWorker, CancellationToken token)
        {
            Global.Inst.InspStage.SetWorkingState(WorkingState.INSPECT);
            IsRunning = true;

            while (!token.IsCancellationRequested)
            {
                Global.Inst.InspStage.OneCycle(); // 정지 요청 전까지 무한 반복
            }

            IsRunning = false;
        }

        //===== [그룹 2] 검사 실행 메인 흐름 =====

        //------- 전체 양산 검사 실행 -------
        public bool RunInspect(out bool isDefect)
        {
            isDefect = false;
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> finalDisplayList = new List<DrawInspectInfo>();

            // 현재 모델에 설정된 모든 윈도우와 알고리즘 순회 실행
            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window); // 최신 이미지/위치 데이터 동기화
                foreach (var algo in window.AlgorithmList)
                {
                    algo.DoInspect();
                    if (algo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                    {
                        finalDisplayList.AddRange(results);
                    }
                }
            }

            // UI 화면 리셋 후 결과 박스 다시 그리기
            if (cameraForm != null)
            {
                cameraForm.ResetDisplay();
                if (finalDisplayList.Count > 0) cameraForm.AddRect(finalDisplayList);
            }
            return true;
        }

        //------- 개별 윈도우/알고리즘 검사 시도 -------
        public bool TryInspect(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) return RunInspect(out _);
            if (!UpdateInspData(inspObj)) return false;

            _inspectBoard.Inspect(inspObj); // 전용 보드 클래스에서 검사 수행
            return DisplayResult(inspObj, inspType);
        }

        //===== [그룹 3] 건반 위치 탐색 및 보정 =====

        //------- 건반 템플릿 매칭 실행 -------
        public void RunKeyMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();

            _lastMatchedKeyRects.Clear();
            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty()) return;

            // 1. 설정된 윈도우들에서 매칭 알고리즘만 골라 실행
            var matchedRects = new List<Rect>();
            foreach (var window in curMode.InspWindowList)
            {
                UpdateInspData(window);
                var matchAlgo = window.AlgorithmList.OfType<MatchAlgorithm>().FirstOrDefault(a => a.IsUse);
                if (matchAlgo == null) continue;

                matchAlgo.DoInspect();
                if (matchAlgo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                {
                    // 왼쪽 건반부터 순서대로 정렬해서 리스트에 담음
                    matchedRects.AddRange(results.OrderBy(r => r.rect.X).Select(r => r.rect));
                }
            }

            // 2. 찾아낸 대략적인 위치를 바탕으로 실제 색상 영역 정밀 보정
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            var candidateRects = new List<Rect>();

            foreach (var matched in matchedRects)
            {
                Rect keyRect = FindActualKeyRect(colorMat, grayMat, matched);
                candidateRects.Add(keyRect);
            }

            // ✅ 높이 필터링: 가장 큰 ROI 높이의 50% 미만은 제외 (부분 걸침 오인식 방지)
            if (candidateRects.Count > 0)
            {
                int maxH = candidateRects.Max(r => r.Height);
                int minValidHeight = (int)(maxH * 0.5f);

                foreach (var keyRect in candidateRects)
                {
                    if (keyRect.Height < minValidHeight) continue; // 너무 작으면 스킵

                    _lastMatchedKeyRects.Add(keyRect);
                    displayList.Add(new DrawInspectInfo(keyRect, $"Key H={keyRect.Height}", InspectType.InspNone, DecisionType.Good));
                }
            }

            cameraForm?.ResetDisplay();
            if (displayList.Count > 0) cameraForm?.AddRect(displayList);
        }

        //------- 색상 기반 건반 영역 정밀 추출 -------
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

            // ✅ centerY를 따로 저장 (위/아래 스캔 모두 여기서 출발)
            int centerY = matched.Y + (int)(matched.Height * 0.5f);
            int topY = centerY;
            int bottomY = centerY;
            int gap = 0;

            // ✅ 위쪽 스캔: matched.Y를 절대 넘지 않음
            int topScanLimit = Math.Max(0, matched.Y - (int)(matched.Height * 0.25f));
            for (int y = centerY; y >= topScanLimit; y--)
            {
                bool match = scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) >= 3;
                if (match) { topY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }
            // ✅ 아래쪽 스캔: centerY에서 출발 (topY 아님), matched.Bottom을 넘지 않음
            gap = 0;
            for (int y = centerY; y <= Math.Min(imgH - 1, matched.Bottom); y++)
            {
                bool match = scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTolerance)) >= 3;
                if (match) { bottomY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            int margin = Math.Max(10, (int)((bottomY - topY) * 0.05f));
            int finalTop = Math.Max(0, topY);                        // 마진 제거
            int finalBottom = Math.Min(imgH - 1, bottomY + margin);

            return new Rect(matched.X, finalTop, matched.Width, finalBottom - finalTop);
        }

        //------- 픽셀 색상 일치 여부 확인 -------
        private bool IsColorMatch(Vec3b px, Vec3b refColor, int tolerance) =>
            Math.Abs(px.Item0 - refColor.Item0) <= tolerance &&
            Math.Abs(px.Item1 - refColor.Item1) <= tolerance &&
            Math.Abs(px.Item2 - refColor.Item2) <= tolerance;

        //===== [그룹 4] 세부 부품(볼트/각인) 검사 =====

        //------- 볼트 및 각인 전체 검사 수행 -------
        public void RunBoltMark()
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null || grayMat.Empty()) return;

            foreach (Rect key in _lastMatchedKeyRects)
            {
                displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));
                // 보정된 건반 영역 안에서 볼트와 각인 알고리즘을 태움
                CheckBolt(grayMat, key, BoltPosition.Top, displayList);
                CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                CheckMark(grayMat, key, displayList);
            }

            cameraForm?.ResetDisplay();
            cameraForm?.AddRect(displayList);
        }

        private enum BoltPosition { Top, Bottom }

        //------- 단일 볼트 존재 검사 -------
        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos, List<DrawInspectInfo> displayList)
        {
            // 상/하 위치에 따른 세부 ROI 영역 계산
            float yCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            Rect boltRoi = new Rect(key.X + (int)(key.Width * 0.2f), key.Y + (int)(key.Height * (yCenter - 0.12f)), (int)(key.Width * 0.6f), (int)(key.Height * 0.24f));

            boltRoi = boltRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (boltRoi.Width <= 0 || boltRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(grayMat, boltRoi))
            {
                // 원형 피처 검출
                CircleSegment[] circles = Cv2.HoughCircles(roiMat, HoughModes.Gradient, 1.0, roiMat.Width, 50, 18, roiMat.Width / 6, roiMat.Width / 2);
                bool boltFound = false;

                if (circles?.Length > 0)
                {
                    var c = circles[0];
                    Rect innerRect = new Rect((int)c.Center.X - (int)(c.Radius * 0.6f), (int)c.Center.Y - (int)(c.Radius * 0.6f), (int)(c.Radius * 1.2f), (int)(c.Radius * 1.2f));
                    innerRect = innerRect.Intersect(new Rect(0, 0, roiMat.Width, roiMat.Height));

                    if (innerRect.Width > 0 && innerRect.Height > 0)
                    {
                        using (Mat inner = new Mat(roiMat, innerRect))
                        {
                            Cv2.MinMaxLoc(inner, out _, out double innerMax);
                            Cv2.MeanStdDev(inner, out Scalar iMean, out _);
                            // 금속 반사광(Max)이 주변 평균(Mean)보다 압도적으로 높은지 분석
                            boltFound = (innerMax / iMean.Val0) > 1.6 && innerMax > 80.0;
                        }
                    }
                }

                displayList.Add(new DrawInspectInfo(boltRoi, $"{(pos == BoltPosition.Top ? "Top" : "Bot")} Bolt {(boltFound ? "OK" : "NG")}", InspectType.InspNone, boltFound ? DecisionType.Good : DecisionType.Defect));
                return boltFound;
            }
        }

        //------- 중앙 각인 유무 검사 -------
        private bool CheckMark(Mat grayMat, Rect key, List<DrawInspectInfo> displayList)
        {
            // 건반의 정중앙 각인 예상 부위 ROI 설정
            Rect markRoi = new Rect(key.X + (int)(key.Width * 0.3f), key.Y + (int)(key.Height * 0.5f), (int)(key.Width * 0.4f), (int)(key.Height * 0.25f));
            markRoi = markRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (markRoi.Width <= 0 || markRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(grayMat, markRoi))
            {
                // 표준편차로 표면 거칠기 확인 및 라플라시안으로 에지 강도 추출
                Cv2.MeanStdDev(roiMat, out _, out Scalar stddev);
                using (Mat lap = new Mat())
                {
                    Cv2.Laplacian(roiMat, lap, MatType.CV_64F);
                    Cv2.MeanStdDev(lap, out _, out Scalar lapStd);

                    // 질감과 에지가 살아있으면 각인이 있는 것으로 판정
                    bool markFound = stddev.Val0 > 3.0 && lapStd.Val0 > 1.5;
                    displayList.Add(new DrawInspectInfo(markRoi, $"Mark {(markFound ? "OK" : "NG")}", InspectType.InspNone, markFound ? DecisionType.Good : DecisionType.Defect));
                    return markFound;
                }
            }
        }

        //===== [그룹 5] 데이터 동기화 및 출력 제어 =====

        //------- 옵션에 따른 결과 화면 표시 -------
        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            var cameraForm = MainForm.GetDockForm<CameraForm>();
            List<DrawInspectInfo> displayList = new List<DrawInspectInfo>();
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null) return;

            // 체크박스 설정 값에 따라 필요한 사각형들만 선별해서 리스트 구성
            foreach (Rect key in _lastMatchedKeyRects)
            {
                if (showKeyboard) displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));
                if (showBolt)
                {
                    CheckBolt(grayMat, key, BoltPosition.Top, displayList);
                    CheckBolt(grayMat, key, BoltPosition.Bottom, displayList);
                }
                if (showMark) CheckMark(grayMat, key, displayList);
            }

            cameraForm?.ResetDisplay();
            if (displayList.Count > 0) cameraForm?.AddRect(displayList);
        }

        //------- 알고리즘별 입력 데이터(이미지/ROI) 갱신 -------
        public bool UpdateInspData(InspWindow inspWindow)
        {
            if (inspWindow == null) return false;
            inspWindow.PatternLearn(); // 기본 패턴 학습 데이터 갱신
            foreach (var algo in inspWindow.AlgorithmList)
            {
                // 현재 윈도우 좌표를 알고리즘 검사 영역으로 복사
                algo.TeachRect = algo.InspRect = inspWindow.WindowArea;
                // 알고리즘 채널에 맞는 이미지(Color/Gray) 할당
                algo.SetInspData(Global.Inst.InspStage.GetMat(0, algo.ImageChannel));
            }
            return true;
        }

        //------- 검사 결과 사각형 화면 출력 -------
        private bool DisplayResult(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) return false;
            List<DrawInspectInfo> totalArea = new List<DrawInspectInfo>();
            foreach (var algorithm in inspObj.AlgorithmList)
            {
                // 지정된 검사 타입만 필터링해서 표시
                if (inspType != InspectType.InspNone && algorithm.InspectType != inspType) continue;
                if (algorithm.GetResultRect(out List<DrawInspectInfo> resultArea) > 0) totalArea.AddRange(resultArea);
            }
            if (totalArea.Count > 0) MainForm.GetDockForm<CameraForm>()?.AddRect(totalArea);
            return true;
        }
    }
}