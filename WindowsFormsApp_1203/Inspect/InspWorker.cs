using JYVision.Algorithm;
using JYVision.Core;
using JYVision.Teach;
using JYVision.Util;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JYVision.Inspect
{
    // ===== 시각 검사 실행 및 결과 처리 워커 클래스 (최적화 버전) =====
    public class InspWorker : IDisposable
    {
        // ── 취소 토큰 ──
        private CancellationTokenSource _cts = new CancellationTokenSource();

        // ── 검사 보드 ──
        private readonly InspectBoard _inspectBoard = new InspectBoard();

        // ── 마지막 매칭된 건반 사각형 목록 ──
        private readonly List<Rect> _lastMatchedKeyRects = new List<Rect>();

        // ── 상태 플래그 ──
        public bool IsRunning { get; private set; } = false;

        // ── NG 카운트 ──
        private int _boltNgCount = 0;
        private int _markNgCount = 0;

        // ── UI 폼: 항상 유효한 참조 반환 (null이면 재탐색) ──
        private CameraForm _cameraForm;
        private ResultForm _resultForm;
        private CameraForm CameraForm { get { if (_cameraForm == null) _cameraForm = MainForm.GetDockForm<CameraForm>(); return _cameraForm; } }
        private ResultForm ResultForm { get { if (_resultForm == null) _resultForm = MainForm.GetDockForm<ResultForm>(); return _resultForm; } }

        // ── 저장 큐: 별도 전용 스레드에서 순차 처리 ──
        private readonly BlockingCollection<SaveTask> _saveQueue =
            new BlockingCollection<SaveTask>(boundedCapacity: 10);
        private readonly Thread _saveThread;

        // ── 저장 태스크 ──
        private struct SaveTask
        {
            public Mat Image;       // Clone된 Mat
            public int BoltNg;
            public int MarkNg;
            public string FileName;
        }

        // ────────────────────────────────────────────
        //------- 생성자: 이미지 저장 전용 백그라운드 스레드 구동 -------
        public InspWorker()
        {
            _saveThread = new Thread(SaveWorkerLoop)
            {
                IsBackground = true,
                Name = "ImageSaveThread"
            };
            _saveThread.Start();
        }

        // ────────────────────────────────────────────
        //------- 자원 해제 및 동작 중인 스레드/큐 안전 종료 -------
        public void Dispose()
        {
            _cts.Cancel();
            _saveQueue.CompleteAdding();
            _saveThread.Join(2000);
            _cts.Dispose();
            _saveQueue.Dispose();
        }

        // ===== [그룹 1] 엔진 및 루프 제어 =====

        //------- 연속 검사 사이클 시작 (비동기 루프 실행) -------
        public void StartCycleInspectImage()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose(); // 기존 토큰 리소스 해제 (메모리 누수 방지)
            }
            _cts = new CancellationTokenSource();

            // UI 스레드에서 미리 탐색 (백그라운드 스레드 첫 접근 지연 방지)
            _cameraForm = MainForm.GetDockForm<CameraForm>();
            _resultForm = MainForm.GetDockForm<ResultForm>();

            Task.Run(() => InspectionLoop(_cts.Token), _cts.Token);
        }

        //------- 진행 중인 검사 루프 즉시 중지 -------
        public void Stop() => _cts.Cancel();

        //------- 실제 검사 메인 루프 (중지 신호가 올 때까지 무한 반복) -------
        private void InspectionLoop(CancellationToken token)
        {
            Global.Inst.InspStage.SetWorkingState(WorkingState.INSPECT);
            IsRunning = true;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool hasMore = Global.Inst.InspStage.OneCycle();
                    if (!hasMore) break;
                }
            }
            finally
            {
                IsRunning = false;
                Global.Inst.InspStage.SetWorkingState(WorkingState.NONE);
            }
        }

        // ===== [그룹 2] 검사 실행 메인 흐름 =====

        //------- 등록된 모든 검사 알고리즘을 순차적으로 실행하고 결과 수합 -------
        public List<DrawInspectInfo> RunInspect(out bool isDefect)
        {
            isDefect = false;
            Model curMode = Global.Inst.InspStage.CurModel;
            var finalDisplayList = new List<DrawInspectInfo>();

            foreach (var window in curMode.InspWindowList)
            {
                if (!UpdateInspData(window)) continue;
                foreach (var algo in window.AlgorithmList)
                {
                    algo.DoInspect();
                    if (algo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                        finalDisplayList.AddRange(results);
                }
            }
            return finalDisplayList;
        }

        //------- 특정 영역(Window)과 타입만 지정해서 단일 검사 실행 -------
        public bool TryInspect(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) { RunInspect(out _); return true; }
            if (!UpdateInspData(inspObj)) return false;
            _inspectBoard.Inspect(inspObj);
            return DisplayResult(inspObj, inspType);
        }

        // ===== [그룹 3] 건반 위치 탐색 및 보정 =====

        //------- 전체 이미지에서 건반 영역(ROI)들을 찾아내고 크기 보정 -------
        public List<DrawInspectInfo> RunKeyMatch()
        {
            Model curMode = Global.Inst.InspStage.CurModel;
            var displayList = new List<DrawInspectInfo>();

            _lastMatchedKeyRects.Clear();

            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty()) return displayList;

            var matchedRects = new List<Rect>();
            foreach (var window in curMode.InspWindowList)
            {
                if (!UpdateInspData(window)) continue;
                var matchAlgo = window.AlgorithmList
                    .OfType<MatchAlgorithm>()
                    .FirstOrDefault(a => a.IsUse);
                if (matchAlgo == null) continue;

                matchAlgo.DoInspect();
                if (matchAlgo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                    matchedRects.AddRange(results.OrderBy(r => r.rect.X).Select(r => r.rect));
            }

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);

            // 병렬로 실제 건반 영역 탐색 (키 개수가 많을 때 효과적)
            var candidateRects = new Rect[matchedRects.Count];
            Parallel.For(0, matchedRects.Count, i =>
                candidateRects[i] = FindActualKeyRect(colorMat, grayMat, matchedRects[i]));

            if (candidateRects.Length > 0)
            {
                int maxH = candidateRects.Max(r => r.Height);
                int minValidH = (int)(maxH * 0.5f);
                int imgW = colorMat.Width;
                int imgH = colorMat.Height;

                foreach (var keyRect in candidateRects)
                {
                    bool clipped = keyRect.X <= 5
                                || keyRect.Right >= imgW - 5
                                || keyRect.Y <= 5
                                || keyRect.Bottom >= imgH - 5;
                    if (!clipped && keyRect.Height < minValidH) continue;

                    _lastMatchedKeyRects.Add(keyRect);
                    displayList.Add(new DrawInspectInfo(
                        keyRect, $"Key H={keyRect.Height}", InspectType.InspNone, DecisionType.Good));
                }
            }
            return displayList;
        }

        //------- 매칭된 대략적인 좌표를 바탕으로 픽셀 색상을 분석해 실제 건반 경계 박스 추출 -------
        private Rect FindActualKeyRect(Mat colorMat, Mat grayMat, Rect matched)
        {
            int imgH = colorMat.Height;
            int imgW = colorMat.Width;

            // 샘플 좌표 사전 계산
            int[] sampleYs = new[] { 0.35f, 0.50f, 0.65f }
                .Select(r => Math.Max(0, Math.Min(matched.Y + (int)(matched.Height * r), imgH - 1)))
                .ToArray();
            int[] scanCols = Enumerable.Range(0, 5)
                .Select(i => Math.Max(0, Math.Min(
                    (int)(matched.X + matched.Width * (0.2f + i * 0.15f)), imgW - 1)))
                .ToArray();

            // 기준 색상 샘플링 (어두운 픽셀 제외)
            int sumB = 0, sumG = 0, sumR = 0, cnt = 0;
            foreach (int sy in sampleYs)
                foreach (int x in scanCols)
                {
                    var px = colorMat.At<Vec3b>(sy, x);
                    if ((px.Item0 + px.Item1 + px.Item2) / 3 < 40) continue;
                    sumB += px.Item0; sumG += px.Item1; sumR += px.Item2; cnt++;
                }

            if (cnt == 0) return matched;

            var refColor = new Vec3b(
                (byte)(sumB / cnt),
                (byte)(sumG / cnt),
                (byte)(sumR / cnt));

            // 색상 판별 및 허용 오차 결정
            bool isYellow = refColor.Item2 > 150 && refColor.Item1 > 150 && refColor.Item0 < 100;
            bool isRed = refColor.Item2 > 150 && refColor.Item1 < 100 && refColor.Item0 < 100;
            bool isBlue = refColor.Item0 > 100 && refColor.Item2 < 100;

            int colorTol = isYellow ? 80 : isRed ? 70 : isBlue ? 65 : 60;
            int gapLimit = isYellow ? 40 : isRed ? 35 : 30;

            int centerY = matched.Y + (int)(matched.Height * 0.5f);
            int topY = centerY, bottomY = centerY, gap = 0;

            // 위쪽 탐색
            int topLimit = Math.Max(0, matched.Y - (int)(matched.Height * 0.25f));
            for (int y = centerY; y >= topLimit; y--)
            {
                bool match = scanCols.Count(
                    x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTol)) >= 3;
                if (match) { topY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            // 아래쪽 탐색
            gap = 0;
            for (int y = centerY; y <= Math.Min(imgH - 1, matched.Bottom); y++)
            {
                bool match = scanCols.Count(
                    x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTol)) >= 3;
                if (match) { bottomY = y; gap = 0; }
                else if (++gap > gapLimit) break;
            }

            int margin = Math.Max(10, (int)((bottomY - topY) * 0.05f));
            int finalTop = Math.Max(0, topY);
            int finalBottom = Math.Min(imgH - 1, bottomY + margin);
            return new Rect(matched.X, finalTop, matched.Width, finalBottom - finalTop);
        }

        //------- 타겟 픽셀이 기준 색상 오차 범위 안에 있는지 확인 (성능 위해 인라인화) -------
        private static bool IsColorMatch(Vec3b px, Vec3b r, int tol) =>
            Math.Abs(px.Item0 - r.Item0) <= tol &&
            Math.Abs(px.Item1 - r.Item1) <= tol &&
            Math.Abs(px.Item2 - r.Item2) <= tol;

        // ===== [그룹 4] 세부 부품(볼트/각인) 검사 =====

        //------- 찾은 건반 영역들을 돌면서 내부의 볼트/마킹 정상 여부 일괄 검사 -------
        public List<DrawInspectInfo> RunBoltMark()
        {
            var displayList = new List<DrawInspectInfo>();
            if (_lastMatchedKeyRects.Count == 0) return displayList;

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null || grayMat.Empty()) return displayList;

            _boltNgCount = 0;
            _markNgCount = 0;

            foreach (Rect key in _lastMatchedKeyRects)
            {
                if (!CheckBolt(grayMat, key, BoltPosition.Top, displayList)) _boltNgCount++;
                if (!CheckBolt(grayMat, key, BoltPosition.Bottom, displayList)) _boltNgCount++;
                if (!CheckMark(grayMat, key, displayList)) _markNgCount++;
            }

            SendResultToForm(_boltNgCount, _markNgCount);

            return displayList;
        }

        private enum BoltPosition { Top, Bottom }

        //------- 해당 건반 위치(상/하)의 볼트 유무 및 퀄리티(명암비) 검사 -------
        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos, List<DrawInspectInfo> displayList)
        {
            float yCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            var boltRoi = new Rect(
                key.X + (int)(key.Width * 0.2f),
                key.Y + (int)(key.Height * (yCenter - 0.12f)),
                (int)(key.Width * 0.6f),
                (int)(key.Height * 0.24f));
            boltRoi = boltRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (boltRoi.Width <= 0 || boltRoi.Height <= 0) return false;

            bool boltFound = false;
            using (var roiMat = new Mat(grayMat, boltRoi))
            {
                var circles = Cv2.HoughCircles(
                    roiMat, HoughModes.Gradient, 1.0, roiMat.Width,
                    50, 18, roiMat.Width / 6, roiMat.Width / 2);

                if (circles?.Length > 0)
                {
                    var c = circles[0];
                    var inner = new Rect(
                        (int)(c.Center.X - c.Radius * 0.6f),
                        (int)(c.Center.Y - c.Radius * 0.6f),
                        (int)(c.Radius * 1.2f),
                        (int)(c.Radius * 1.2f));
                    inner = inner.Intersect(new Rect(0, 0, roiMat.Width, roiMat.Height));

                    if (inner.Width > 0 && inner.Height > 0)
                    {
                        using (var innerMat = new Mat(roiMat, inner))
                        {
                            Cv2.MinMaxLoc(innerMat, out _, out double iMax);
                            Cv2.MeanStdDev(innerMat, out Scalar iMean, out _);
                            boltFound = (iMax / iMean.Val0) > 1.6
                                     && iMax > 120.0
                                     && iMean.Val0 > 45.0;
                        }
                    }
                }
            }
            if (displayList != null)
            {
                string label = $"{(pos == BoltPosition.Top ? "Top" : "Bot")} Bolt {(boltFound ? "OK" : "NG")}";
                displayList.Add(new DrawInspectInfo(
                    boltRoi, label, InspectType.InspNone,
                    boltFound ? DecisionType.Good : DecisionType.Defect));
            }
            return boltFound;
        }

        //------- 건반 중앙의 마킹(각인) 선명도 및 존재 여부 검사 -------
        //------- 건반 중앙의 마킹(각인) 선명도 및 존재 여부 검사 (최종 튜닝 버전) -------
        private bool CheckMark(Mat grayMat, Rect key, List<DrawInspectInfo> displayList)
        {
            // 1. 각인이 위치한 중앙 ROI 설정
            var markRoi = new Rect(
                key.X + (int)(key.Width * 0.3f),
                key.Y + (int)(key.Height * 0.5f),
                (int)(key.Width * 0.4f),
                (int)(key.Height * 0.25f));

            // 이미지 경계 이탈 방지
            markRoi = markRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (markRoi.Width <= 0 || markRoi.Height <= 0) return false;

            bool markFound = false;

            using (var roiMat = new Mat(grayMat, markRoi))
            using (var enhanced = new Mat())
            using (var blurred = new Mat())
            {
                // 2. [CLAHE] 로컬 대비 향상 (어두운 건반의 각인을 강제로 살려냄)
                // clipLimit이 높을수록 대비가 강해지지만 노이즈도 커집니다. (1.5 ~ 2.0 추천)
                using (var clahe = Cv2.CreateCLAHE(clipLimit: 1.8, tileGridSize: new OpenCvSharp.Size(8, 8)))
                {
                    clahe.Apply(roiMat, enhanced);
                }

                // 3. [GaussianBlur] 미세 노이즈 제거 (가짜 엣지 방지)
                Cv2.GaussianBlur(enhanced, blurred, new OpenCvSharp.Size(3, 3), 0);

                // 4. [Laplacian] 선명도(엣지 강도) 추출
                using (var lap = new Mat())
                {
                    Cv2.Laplacian(blurred, lap, MatType.CV_64F);

                    // 5. 통계치 계산 (Contrast와 Edge 선명도)
                    Cv2.MeanStdDev(blurred, out _, out Scalar stddev); // 전체적인 질감
                    Cv2.MeanStdDev(lap, out _, out Scalar lapStd);    // 각인 엣지의 선명도
                    markFound = stddev.Val0 > 9.0 && lapStd.Val0 > 2.8;
                }
            }

            // 결과 UI 출력
            if (displayList != null)
            {
                displayList.Add(new DrawInspectInfo(
                    markRoi,
                    $"Mark {(markFound ? "OK" : "NG")}",
                    InspectType.InspNone,
                    markFound ? DecisionType.Good : DecisionType.Defect));
            }

            return markFound;
        }

        // ===== [그룹 5] 데이터 동기화 및 출력 제어 =====

        //------- UI 체크 옵션에 따라 건반/볼트/마킹 검사 결과를 화면에 그리기 -------
        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
        {
            if (_lastMatchedKeyRects.Count == 0) return;

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null) return;

            var displayList = new List<DrawInspectInfo>();
            int boltNg = 0, markNg = 0;

            foreach (Rect key in _lastMatchedKeyRects)
            {
                if (showKeyboard)
                    displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));
                if (showBolt)
                {
                    if (!CheckBolt(grayMat, key, BoltPosition.Top, displayList)) boltNg++;
                    if (!CheckBolt(grayMat, key, BoltPosition.Bottom, displayList)) boltNg++;
                }
                if (showMark)
                    if (!CheckMark(grayMat, key, displayList)) markNg++;
            }

            CameraForm?.ResetDisplay();
            if (displayList.Count > 0) CameraForm?.AddRect(displayList);
        }

        // ===== [그룹 6] 결과 전송 및 이미지 저장 =====

        //------- 메인 UI에 NG 결과를 업데이트하고, 비동기 큐에 저장할 이미지 데이터 밀어넣기 -------
        private void SendResultToForm(int boltNg, int markNg)
        {
            if (ResultForm == null) return;

            // UI 스레드용 썸네일 생성
            Bitmap captured = null;
            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);

            if (colorMat != null && !colorMat.Empty())
            {
                try
                {
                    Mat bgr = colorMat.Channels() == 1
                        ? colorMat.CvtColor(ColorConversionCodes.GRAY2BGR)
                        : colorMat.Clone();
                    using (bgr)
                        captured = BitmapConverter.ToBitmap(bgr);
                }
                catch { /* 변환 실패 시 null 유지 */ }

                // 저장 큐에 적재 (이미지는 Clone해서 소유권 이전)
                try
                {
                    string lastPath = Global.Inst.InspStage.LastInspectedImagePath;
                    string fileName = string.IsNullOrEmpty(lastPath)
                        ? $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png"
                        : Path.GetFileNameWithoutExtension(lastPath) + ".png";

                    if (!_saveQueue.IsAddingCompleted)
                    {
                        Mat cloneMat = colorMat.Clone();
                        bool isAdded = _saveQueue.TryAdd(new SaveTask
                        {
                            Image = cloneMat,
                            BoltNg = boltNg,
                            MarkNg = markNg,
                            FileName = fileName
                        }, 0);

                        // 큐가 꽉 차서 저장이 스킵된 경우 복사된 이미지 리소스 뱉어내기
                        if (!isAdded)
                        {
                            cloneMat?.Dispose();
                        }
                    }
                }
                catch { }
            }

            // UI 업데이트
            ResultForm.UpdateNgSummary(boltNg, markNg, captured);
        }

        //------- 큐에 쌓인 검사 결과물들을 하나씩 꺼내서 디스크(하드)에 저장하는 백그라운드 태스크 -------
        private void SaveWorkerLoop()
        {
            foreach (var task in _saveQueue.GetConsumingEnumerable())
            {
                try
                {
                    using (task.Image)
                    {
                        string subFolder;
                        if (task.BoltNg > 0 && task.MarkNg > 0) subFolder = "NG-BOLT_MARK";
                        else if (task.BoltNg > 0) subFolder = "NG-BOLT";
                        else if (task.MarkNg > 0) subFolder = "NG-MARK";
                        else subFolder = "OK";

                        string saveDir = Path.Combine(@"D:\Results", subFolder);
                        Directory.CreateDirectory(saveDir);

                        string savePath = Path.Combine(saveDir, task.FileName);
                        Cv2.ImWrite(savePath, task.Image);
                        SLogger.Write($"결과 저장: {savePath}");
                    }
                }
                catch (Exception ex)
                {
                    SLogger.Write($"결과 이미지 저장 실패: {ex.Message}", SLogger.LogType.Error);
                }
            }
        }

        // ===== [그룹 7] 공통 헬퍼 =====
        
        //------- 검사 시작 전, 버퍼 이미지를 알고리즘에 밀어넣고 필요한 패턴 학습 처리 -------
        public bool UpdateInspData(InspWindow inspWindow)
        {
            if (inspWindow == null) return false;

            // 패턴 학습 (IsLearned 미지원 시 매 사이클 호출)
            inspWindow.PatternLearn();

            foreach (var algo in inspWindow.AlgorithmList)
            {
                algo.TeachRect = algo.InspRect = inspWindow.WindowArea;
                algo.SetInspData(Global.Inst.InspStage.GetMat(0, algo.ImageChannel));
            }
            return true;
        }

        //------- 검사가 완료된 객체의 결과를 UI(CameraForm) 위에 박스 형태로 출력 -------
        private bool DisplayResult(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) return false;
            var total = new List<DrawInspectInfo>();
            foreach (var algo in inspObj.AlgorithmList)
            {
                if (inspType != InspectType.InspNone && algo.InspectType != inspType) continue;
                if (algo.GetResultRect(out List<DrawInspectInfo> area) > 0)
                    total.AddRange(area);
            }
            if (total.Count > 0) CameraForm?.AddRect(total);
            return true;
        }
    }
}