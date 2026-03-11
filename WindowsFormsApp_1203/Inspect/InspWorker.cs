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
using System.Diagnostics; // Stopwatch 사용 

namespace JYVision.Inspect
{
    // ===== 시각 검사 실행 및 결과 처리 워커 클래스 (통계 + TactTime CSV 통합 버전) =====
    public class InspWorker : IDisposable
    {
        // ── 취소 토큰 및 상태 ──
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly InspectBoard _inspectBoard = new InspectBoard();
        private readonly List<Rect> _lastMatchedKeyRects = new List<Rect>();
        public bool IsRunning { get; private set; } = false;

        // ── NG 카운트 (단일 사이클용) ──
        private int _boltNgCount = 0;
        private int _markNgCount = 0;

        // ── 통계 및 데이터 관리 변수 ──
        private int _totalCount = 0;
        private int _okCount = 0;
        private int _ngBoltCountTotal = 0;
        private int _ngMarkCountTotal = 0;
        private DateTime _startTime = DateTime.Now;

        // ── 구간별 Tact Time 측정용 변수 ──
        private long _lastMatchTimeMs = 0;

        // ── 로그 저장 경로 안전 보장 ──
        private string BasePath { get; }
        private string LogDir => Path.Combine(BasePath, "Logs");
        private string LogPath => Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}_InspectLog.csv");

        // ── UI 폼 ──
        private CameraForm _cameraForm;
        private ResultForm _resultForm;
        private CameraForm CameraForm { get { if (_cameraForm == null) _cameraForm = MainForm.GetDockForm<CameraForm>(); return _cameraForm; } }
        private ResultForm ResultForm { get { if (_resultForm == null) _resultForm = MainForm.GetDockForm<ResultForm>(); return _resultForm; } }

        // ── 저장 큐 및 스레드 ──
        private readonly BlockingCollection<SaveTask> _saveQueue = new BlockingCollection<SaveTask>(boundedCapacity: 10);
        private readonly Thread _saveThread;

        private struct SaveTask { public Mat Image; public int BoltNg; public int MarkNg; public string FileName; }

        //------- 생성자: 경로 초기화 및 이미지 저장 전용 백그라운드 스레드 구동 -------
        public InspWorker()
        {
            BasePath = Directory.Exists(@"D:\") ? @"D:\Results" : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results");
            _saveThread = new Thread(SaveWorkerLoop) { IsBackground = true, Name = "ImageSaveThread" };
            _saveThread.Start();
        }

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

        public void StartCycleInspectImage()
        {
            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); }
            _cts = new CancellationTokenSource();
            _cameraForm = MainForm.GetDockForm<CameraForm>();
            _resultForm = MainForm.GetDockForm<ResultForm>();
            Task.Run(() => InspectionLoop(_cts.Token), _cts.Token);
        }

        public void Stop() => _cts.Cancel();

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

        // ===== [그룹 2 & 3] 검사 실행 및 건반 탐색 =====

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
                    if (algo.GetResultRect(out List<DrawInspectInfo> results) > 0) finalDisplayList.AddRange(results);
                }
            }
            return finalDisplayList;
        }

        public bool TryInspect(InspWindow inspObj, InspectType inspType)
        {
            if (inspObj == null) { RunInspect(out _); return true; }
            if (!UpdateInspData(inspObj)) return false;
            _inspectBoard.Inspect(inspObj);
            return DisplayResult(inspObj, inspType);
        }

        //------- 건반 영역(ROI) 탐색 + 소요 시간 측정 -------
        public List<DrawInspectInfo> RunKeyMatch()
        {
            Stopwatch sw = Stopwatch.StartNew(); // 탐색 시작

            Model curMode = Global.Inst.InspStage.CurModel;
            var displayList = new List<DrawInspectInfo>();
            _lastMatchedKeyRects.Clear();

            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);
            if (colorMat == null || colorMat.Empty())
            {
                sw.Stop();
                _lastMatchTimeMs = sw.ElapsedMilliseconds;
                return displayList;
            }

            var matchedRects = new List<Rect>();
            foreach (var window in curMode.InspWindowList)
            {
                if (!UpdateInspData(window)) continue;
                var matchAlgo = window.AlgorithmList.OfType<MatchAlgorithm>().FirstOrDefault(a => a.IsUse);
                if (matchAlgo == null) continue;

                matchAlgo.DoInspect();
                if (matchAlgo.GetResultRect(out List<DrawInspectInfo> results) > 0)
                    matchedRects.AddRange(results.OrderBy(r => r.rect.X).Select(r => r.rect));
            }

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            var candidateRects = new Rect[matchedRects.Count];
            Parallel.For(0, matchedRects.Count, i => candidateRects[i] = FindActualKeyRect(colorMat, grayMat, matchedRects[i]));

            if (candidateRects.Length > 0)
            {
                int maxH = candidateRects.Max(r => r.Height);
                int minValidH = (int)(maxH * 0.5f);
                int imgW = colorMat.Width;
                int imgH = colorMat.Height;

                foreach (var keyRect in candidateRects)
                {
                    bool clipped = keyRect.X <= 5 || keyRect.Right >= imgW - 5 || keyRect.Y <= 5 || keyRect.Bottom >= imgH - 5;
                    if (!clipped && keyRect.Height < minValidH) continue;
                    _lastMatchedKeyRects.Add(keyRect);
                    displayList.Add(new DrawInspectInfo(keyRect, $"Key H={keyRect.Height}", InspectType.InspNone, DecisionType.Good));
                }
            }

            sw.Stop(); // 탐색 완료
            _lastMatchTimeMs = sw.ElapsedMilliseconds;

            return displayList;
        }

        private Rect FindActualKeyRect(Mat colorMat, Mat grayMat, Rect matched)
        {
            int imgH = colorMat.Height, imgW = colorMat.Width;
            int[] sampleYs = new[] { 0.35f, 0.50f, 0.65f }.Select(r => Math.Max(0, Math.Min(matched.Y + (int)(matched.Height * r), imgH - 1))).ToArray();
            int[] scanCols = Enumerable.Range(0, 5).Select(i => Math.Max(0, Math.Min((int)(matched.X + matched.Width * (0.2f + i * 0.15f)), imgW - 1))).ToArray();

            int sumB = 0, sumG = 0, sumR = 0, cnt = 0;
            foreach (int sy in sampleYs) foreach (int x in scanCols)
                {
                    var px = colorMat.At<Vec3b>(sy, x);
                    if ((px.Item0 + px.Item1 + px.Item2) / 3 < 40) continue;
                    sumB += px.Item0; sumG += px.Item1; sumR += px.Item2; cnt++;
                }
            if (cnt == 0) return matched;

            var refColor = new Vec3b((byte)(sumB / cnt), (byte)(sumG / cnt), (byte)(sumR / cnt));
            bool isYellow = refColor.Item2 > 150 && refColor.Item1 > 150 && refColor.Item0 < 100;
            bool isRed = refColor.Item2 > 150 && refColor.Item1 < 100 && refColor.Item0 < 100;
            bool isBlue = refColor.Item0 > 100 && refColor.Item2 < 100;
            int colorTol = isYellow ? 80 : isRed ? 70 : isBlue ? 65 : 60;
            int gapLimit = isYellow ? 40 : isRed ? 35 : 30;

            int centerY = matched.Y + (int)(matched.Height * 0.5f);
            int topY = centerY, bottomY = centerY, gap = 0;

            int topLimit = Math.Max(0, matched.Y - (int)(matched.Height * 0.25f));
            for (int y = centerY; y >= topLimit; y--)
            {
                bool match = scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTol)) >= 3;
                if (match) { topY = y; gap = 0; } else if (++gap > gapLimit) break;
            }

            gap = 0;
            for (int y = centerY; y <= Math.Min(imgH - 1, matched.Bottom); y++)
            {
                bool match = scanCols.Count(x => IsColorMatch(colorMat.At<Vec3b>(y, x), refColor, colorTol)) >= 3;
                if (match) { bottomY = y; gap = 0; } else if (++gap > gapLimit) break;
            }

            int margin = Math.Max(10, (int)((bottomY - topY) * 0.05f));
            int finalTop = Math.Max(0, topY);
            int finalBottom = Math.Min(imgH - 1, bottomY + margin);
            return new Rect(matched.X, finalTop, matched.Width, finalBottom - finalTop);
        }

        private static bool IsColorMatch(Vec3b px, Vec3b r, int tol) => Math.Abs(px.Item0 - r.Item0) <= tol && Math.Abs(px.Item1 - r.Item1) <= tol && Math.Abs(px.Item2 - r.Item2) <= tol;

        // ===== [그룹 4] 세부 부품 검사 (볼트/각인) 및 Tact Time 취합 =====

        //------- 볼트/마킹 구간 검사 시간 분리 측정 ------
        public List<DrawInspectInfo> RunBoltMark()
        {
            var displayList = new List<DrawInspectInfo>();
            if (_lastMatchedKeyRects.Count == 0) return displayList;

            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null || grayMat.Empty()) return displayList;

            _boltNgCount = 0; _markNgCount = 0;

            long boltTimeMs = 0;
            long markTimeMs = 0;
            Stopwatch swTotal = Stopwatch.StartNew(); // 이 함수 전체 수행시간 측정용
            Stopwatch swStep = new Stopwatch();       // 세부 항목 측정용

            foreach (Rect key in _lastMatchedKeyRects)
            {
                // 1. 볼트 검사 소요 시간 누적
                swStep.Restart();
                if (!CheckBolt(grayMat, key, BoltPosition.Top, displayList)) _boltNgCount++;
                if (!CheckBolt(grayMat, key, BoltPosition.Bottom, displayList)) _boltNgCount++;
                swStep.Stop();
                boltTimeMs += swStep.ElapsedMilliseconds;

                // 2. 각인(마크) 검사 소요 시간 누적
                swStep.Restart();
                if (!CheckMark(grayMat, key, displayList)) _markNgCount++;
                swStep.Stop();
                markTimeMs += swStep.ElapsedMilliseconds;
            }

            // 3. UI 업데이트 및 이미지 저장 큐 적재 소요 시간 측정
            swStep.Restart();
            SendResultToForm(_boltNgCount, _markNgCount);
            swStep.Stop();
            long uiTimeMs = swStep.ElapsedMilliseconds;

            swTotal.Stop();
            long totalCycleTime = _lastMatchTimeMs + swTotal.ElapsedMilliseconds;

            // 4. 구간별 Tact Time 종합 로그 출력
            SLogger.Write($"[TACT TIME] 탐색:{_lastMatchTimeMs}ms | 볼트:{boltTimeMs}ms | 각인:{markTimeMs}ms | 후처리:{uiTimeMs}ms ➔ 1Cycle 총합: {totalCycleTime}ms");

            // 5. [신규] 통계 업데이트 및 CSV 저장 (Tact Time 데이터 포함)
            UpdateStatistics(_boltNgCount, _markNgCount, _lastMatchTimeMs, boltTimeMs, markTimeMs, uiTimeMs, totalCycleTime);

            return displayList;
        }

        private enum BoltPosition { Top, Bottom }

        private bool CheckBolt(Mat grayMat, Rect key, BoltPosition pos, List<DrawInspectInfo> displayList)
        {
            float yCenter = (pos == BoltPosition.Top) ? 0.20f : 0.80f;
            var boltRoi = new Rect(key.X + (int)(key.Width * 0.2f), key.Y + (int)(key.Height * (yCenter - 0.12f)), (int)(key.Width * 0.6f), (int)(key.Height * 0.24f));
            boltRoi = boltRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (boltRoi.Width <= 0 || boltRoi.Height <= 0) return false;

            bool boltFound = false;
            using (var roiMat = new Mat(grayMat, boltRoi))
            {
                var circles = Cv2.HoughCircles(roiMat, HoughModes.Gradient, 1.0, roiMat.Width, 50, 18, roiMat.Width / 6, roiMat.Width / 2);
                if (circles?.Length > 0)
                {
                    var c = circles[0];
                    var inner = new Rect((int)(c.Center.X - c.Radius * 0.6f), (int)(c.Center.Y - c.Radius * 0.6f), (int)(c.Radius * 1.2f), (int)(c.Radius * 1.2f));
                    inner = inner.Intersect(new Rect(0, 0, roiMat.Width, roiMat.Height));
                    if (inner.Width > 0 && inner.Height > 0)
                    {
                        using (var innerMat = new Mat(roiMat, inner))
                        {
                            Cv2.MinMaxLoc(innerMat, out _, out double iMax);
                            Cv2.MeanStdDev(innerMat, out Scalar iMean, out _);
                            boltFound = (iMax / iMean.Val0) > 1.6 && iMax > 120.0 && iMean.Val0 > 45.0;
                        }
                    }
                }
            }
            if (displayList != null) displayList.Add(new DrawInspectInfo(boltRoi, $"{(pos == BoltPosition.Top ? "Top" : "Bot")} Bolt {(boltFound ? "OK" : "NG")}", InspectType.InspNone, boltFound ? DecisionType.Good : DecisionType.Defect));
            return boltFound;
        }

        private bool CheckMark(Mat grayMat, Rect key, List<DrawInspectInfo> displayList)
        {
            var markRoi = new Rect(key.X + (int)(key.Width * 0.3f), key.Y + (int)(key.Height * 0.5f), (int)(key.Width * 0.4f), (int)(key.Height * 0.25f));
            markRoi = markRoi.Intersect(new Rect(0, 0, grayMat.Width, grayMat.Height));
            if (markRoi.Width <= 0 || markRoi.Height <= 0) return false;

            bool markFound = false;
            using (var roiMat = new Mat(grayMat, markRoi))
            using (var enhanced = new Mat())
            using (var blurred = new Mat())
            {
                using (var clahe = Cv2.CreateCLAHE(clipLimit: 1.8, tileGridSize: new OpenCvSharp.Size(8, 8))) { clahe.Apply(roiMat, enhanced); }
                Cv2.GaussianBlur(enhanced, blurred, new OpenCvSharp.Size(3, 3), 0);
                using (var lap = new Mat())
                {
                    Cv2.Laplacian(blurred, lap, MatType.CV_64F);
                    Cv2.MeanStdDev(blurred, out _, out Scalar stddev);
                    Cv2.MeanStdDev(lap, out _, out Scalar lapStd);
                    markFound = stddev.Val0 > 9.0 && lapStd.Val0 > 2.8;
                }
            }
            if (displayList != null) displayList.Add(new DrawInspectInfo(markRoi, $"Mark {(markFound ? "OK" : "NG")}", InspectType.InspNone, markFound ? DecisionType.Good : DecisionType.Defect));
            return markFound;
        }

        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
        {
            if (_lastMatchedKeyRects.Count == 0) return;
            Mat grayMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Gray);
            if (grayMat == null) return;

            var displayList = new List<DrawInspectInfo>();
            foreach (Rect key in _lastMatchedKeyRects)
            {
                if (showKeyboard) displayList.Add(new DrawInspectInfo(key, "Key ROI", InspectType.InspNone, DecisionType.Good));
                if (showBolt) { CheckBolt(grayMat, key, BoltPosition.Top, displayList); CheckBolt(grayMat, key, BoltPosition.Bottom, displayList); }
                if (showMark) CheckMark(grayMat, key, displayList);
            }
            CameraForm?.ResetDisplay();
            if (displayList.Count > 0) CameraForm?.AddRect(displayList);
        }

        // ===== [그룹 5] 통계 업데이트 및 데이터 관리 (CSV에 시간 기록 추가) =====

        //------- 매 검사 결과마다 통계(파레토, UPH)를 갱신하고 CSV로 내보내기 (시간 데이터 추가) -------
        private void UpdateStatistics(int boltNg, int markNg, long matchMs, long boltMs, long markMs, long uiMs, long totalMs)
        {
            _totalCount++;
            bool isTotalOk = (boltNg == 0 && markNg == 0);

            if (isTotalOk) _okCount++;
            _ngBoltCountTotal += boltNg;
            _ngMarkCountTotal += markNg;

            // UPH (가동률) 계산
            double elapsedHours = (DateTime.Now - _startTime).TotalHours;
            double uph = elapsedHours > 0.0001 ? _totalCount / elapsedHours : _totalCount;

            // CSV 데이터 라인 생성 (끝에 시간 측정값 추가)
            string finalResult = isTotalOk ? "OK" : "NG";
            string logLine = $"{DateTime.Now:HH:mm:ss},{_totalCount},{boltNg},{markNg},{finalResult},{uph:F1},{matchMs},{boltMs},{markMs},{uiMs},{totalMs}";

            // 비동기 CSV 저장
            Task.Run(() => {
                try
                {
                    if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);

                    if (!File.Exists(LogPath))
                    {
                        // 엑셀 헤더에 항목 추가: Match(ms), Bolt(ms), Mark(ms), UI(ms), Total(ms)
                        string header = "Time,TotalCount,BoltNG,MarkNG,Result,CurrentUPH,Match(ms),Bolt(ms),Mark(ms),UI(ms),Total(ms)\n";
                        File.WriteAllText(LogPath, header, System.Text.Encoding.UTF8);
                    }

                    File.AppendAllText(LogPath, logLine + "\n", System.Text.Encoding.UTF8);
                }
                catch { /* 로그 저장 실패 시 무시 */ }
            });

            // 관리자 확인용 하단 시스템 로그 출력
            SLogger.Write($"[STATS] 누적검사:{_totalCount} | OK:{_okCount} | 볼트NG:{_ngBoltCountTotal} | 마크NG:{_ngMarkCountTotal} | 가동률(UPH):{uph:F1}");
        }

        // ===== [그룹 6] 결과 전송 및 이미지 저장 =====

        private void SendResultToForm(int boltNg, int markNg)
        {
            if (ResultForm == null) return;
            Bitmap captured = null;
            Mat colorMat = Global.Inst.InspStage.GetMat(0, eImageChannel.Color);

            if (colorMat != null && !colorMat.Empty())
            {
                try
                {
                    Mat bgr = colorMat.Channels() == 1 ? colorMat.CvtColor(ColorConversionCodes.GRAY2BGR) : colorMat.Clone();
                    using (bgr) captured = BitmapConverter.ToBitmap(bgr);
                }
                catch { }

                try
                {
                    string lastPath = Global.Inst.InspStage.LastInspectedImagePath;
                    string fileName = string.IsNullOrEmpty(lastPath) ? $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png" : Path.GetFileNameWithoutExtension(lastPath) + ".png";

                    if (!_saveQueue.IsAddingCompleted)
                    {
                        Mat cloneMat = colorMat.Clone();
                        bool isAdded = _saveQueue.TryAdd(new SaveTask { Image = cloneMat, BoltNg = boltNg, MarkNg = markNg, FileName = fileName }, 0);
                        if (!isAdded) cloneMat?.Dispose();
                    }
                }
                catch { }
            }

            // (기존 UpdateStatistics 위치를 옮겨서, 이제 RunBoltMark 끝에서 호출하도록 변경했습니다)

            ResultForm.UpdateNgSummary(boltNg, markNg, captured);
        }

        private void SaveWorkerLoop()
        {
            foreach (var task in _saveQueue.GetConsumingEnumerable())
            {
                try
                {
                    using (task.Image)
                    {
                        string subFolder = (task.BoltNg > 0 && task.MarkNg > 0) ? "NG-BOLT_MARK" : (task.BoltNg > 0 ? "NG-BOLT" : (task.MarkNg > 0 ? "NG-MARK" : "OK"));
                        string saveDir = Path.Combine(BasePath, subFolder);
                        Directory.CreateDirectory(saveDir);
                        string savePath = Path.Combine(saveDir, task.FileName);
                        Cv2.ImWrite(savePath, task.Image);
                    }
                }
                catch (Exception ex) { SLogger.Write($"결과 이미지 저장 실패: {ex.Message}", SLogger.LogType.Error); }
            }
        }

        // ===== [그룹 7] 공통 헬퍼 =====

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
            var total = new List<DrawInspectInfo>();
            foreach (var algo in inspObj.AlgorithmList)
            {
                if (inspType != InspectType.InspNone && algo.InspectType != inspType) continue;
                if (algo.GetResultRect(out List<DrawInspectInfo> area) > 0) total.AddRange(area);
            }
            if (total.Count > 0) CameraForm?.AddRect(total);
            return true;
        }
    }
}