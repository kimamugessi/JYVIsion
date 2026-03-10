using JYVision.Algorithm;
using JYVision.Core;
using JYVision.Grab;
using JYVision.Inspect;
using JYVision.Property;
using JYVision.SaigeSDK;
using JYVision.Sequence;
using JYVision.Setting;
using JYVision.Teach;
using JYVision.Util;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JYVision.Core
{
    //===== 시각 검사 스테이지 통합 관리 클래스 =====
    public class InspStage : IDisposable
    {
        //===== [그룹 1] 필드 및 속성 =====
        public static readonly int MAX_GRAB_BUF = 1;

        private ImageSpace _imageSpace = null;
        private GrabModel _grabManager = null;
        private CameraType _camType = CameraType.None;
        private SaigeAI _saigeAI;
        private PreviewImage _previewImage = null;
        private Model _model = null;
        private InspWindow _selectedInspWindow = null;
        private InspWorker _inspWorker = null;
        private ImageLoader _imageLoader = null;
        private RegistryKey _regKey = null;
        private bool _lastestModelOpen = false;
        private bool _isInspectMode = false;
        private string _capturePath = "";
        private string _lotNumber;
        private string _serialID;

        public bool UseCamera { get; set; } = false;
        public bool SaveCamImage { get; set; } = false;
        public int SaveImageIndex { get; set; } = 0;
        public bool LiveMode { get; set; } = false;
        public int SelBufferIndex { get; set; } = 0;
        public eImageChannel SelImageChannel { get; set; } = eImageChannel.Gray;

        public ImageSpace ImageSpace { get => _imageSpace; }
        public PreviewImage PreView { get => _previewImage; }
        public InspWorker InspWorker { get => _inspWorker; }
        public Model CurModel { get => _model; }

        // InspWorker에서 결과 저장 시 원본 파일명 참조용
        public string LastInspectedImagePath => _imageLoader?.LastImagePath ?? "";

        public SaigeAI AIModule
        {
            get { if (_saigeAI == null) _saigeAI = new SaigeAI(); return _saigeAI; }
        }

        //------- 기본 생성자 -------
        public InspStage() { }

        //===== [그룹 2] 외부 호출 진입점 =====

        //------- 건반 매칭 검사 래퍼(Wrapper) -------
        public List<DrawInspectInfo> RunKeyMatch()
            => _inspWorker.RunKeyMatch();

        //------- 볼트 및 마크 검사 래퍼(Wrapper) -------
        public List<DrawInspectInfo> RunBoltMark()
            => _inspWorker.RunBoltMark();

        //------- UI 체크 옵션에 맞춘 디스플레이 래퍼(Wrapper) -------
        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
            => _inspWorker.RunDisplayWithOptions(showKeyboard, showBolt, showMark);

        //------- ResultForm Clear 버튼 클릭 시 이미지 카운터 초기화 -------
        public void ResetImageLoader()
        {
            _imageLoader?.Reset();
            SLogger.Write("이미지 카운터 초기화 (ResultForm Clear)");
        }

        //===== [그룹 3] 초기화 및 설정 =====

        //------- 스테이지 내 모든 관리 객체(카메라, 워커, 버퍼 등) 초기화 -------
        public bool Initialize()
        {
            LoadSetting();
            SLogger.Write("InspStage 초기화!");
            _imageSpace = new ImageSpace();
            _previewImage = new PreviewImage();
            _inspWorker = new InspWorker();
            _imageLoader = new ImageLoader();
            _regKey = Registry.CurrentUser.CreateSubKey("Software\\JYVision");
            _model = new Model();
            LoadSetting();

            switch (_camType)
            {
                case CameraType.WebCam: { _grabManager = new WebCam(); break; }
                case CameraType.HikRobotCam: { _grabManager = new HikRobotCam(); break; }
            }

            if (_grabManager != null && _grabManager.InitGrab())
            {
                _grabManager.TransferCompleted += _multiGrab_TransferCompleted;
                InitModelGrab(MAX_GRAB_BUF);
            }

            VisionSequence.Inst.InitSequence();
            VisionSequence.Inst.SeqCommand += SeqCommand;

            if (!LastestModelOpen())
                MessageBox.Show("최근 모델을 불러오지 못했습니다.");

            return true;
        }

        //------- XML 설정 파일에서 기본 세팅(카메라 타입 등) 로드 -------
        private void LoadSetting() { _camType = SettingXml.Inst.CamType; }

        //------- 카메라 해상도 및 비트 심도(BPP)를 읽어와 검사용 메모리 버퍼 할당 -------
        public void InitModelGrab(int bufferCount)
        {
            if (_grabManager == null) return;
            int bpp = 8;
            _grabManager.GetPixelBpp(out bpp);
            int w, h, stride;
            _grabManager.GetResolution(out w, out h, out stride);
            _imageSpace?.SetImageInfo(bpp, w, h, stride);
            SetBuffer(bufferCount);
            SetImageChannel(bpp == 24 ? eImageChannel.Color : eImageChannel.Gray);
        }

        //===== [그룹 4] 이미지 버퍼 및 메모리 관리 =====

        //------- 로컬 경로의 이미지를 읽어와서 메모리 버퍼(ImageSpace)에 적재 -------
        public void SetImageBuffer(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                using (Mat matImage = Cv2.ImRead(filePath, ImreadModes.Unchanged))
                {
                    if (matImage.Empty()) return;
                    int alignedWidth = (matImage.Width + 3) / 4 * 4;
                    int bytesPerPixel = (int)matImage.ElemSize();
                    int imageStride = alignedWidth * bytesPerPixel;

                    if (_imageSpace.ImageSize.Width != alignedWidth ||
                        _imageSpace.ImageSize.Height != matImage.Height)
                    {
                        _imageSpace.SetImageInfo(bytesPerPixel * 8, alignedWidth, matImage.Height, imageStride);
                        SetBuffer(_imageSpace.BufferCount);
                    }

                    using (Mat aligned = new Mat(matImage.Height, alignedWidth, matImage.Type(), Scalar.Black))
                    {
                        matImage.CopyTo(aligned[new Rect(0, 0, matImage.Width, matImage.Height)]);
                        long bufSize = aligned.Total() * aligned.ElemSize();
                        IntPtr destPtr = ImageSpace.GetnspectionBufferPtr(0);
                        if (destPtr != IntPtr.Zero)
                        {
                            byte[] buf = new byte[bufSize];
                            Marshal.Copy(aligned.Data, buf, 0, (int)bufSize);
                            Marshal.Copy(buf, 0, destPtr, (int)bufSize);
                        }
                    }
                }
                _imageSpace.Split(0);
                if (!_inspWorker.IsRunning)
                {
                    DisplayGrabImage(0);
                }
            }
            catch (Exception ex)
            {
                SLogger.Write($"SetImageBuffer 오류: {ex.Message}", SLogger.LogType.Error);
            }
        }

        //------- 카메라 해상도 변경을 감지하고 버퍼 크기를 재조정 -------
        public void CheckImageBuffer()
        {
            if (_grabManager == null || SettingXml.Inst.CamType == CameraType.None) return;
            int w, h, stride;
            _grabManager.GetResolution(out w, out h, out stride);
            if (_imageSpace.ImageSize.Width != w || _imageSpace.ImageSize.Height != h)
            {
                int bpp = 8;
                _grabManager.GetPixelBpp(out bpp);
                _imageSpace.SetImageInfo(bpp, w, h, stride);
                SetBuffer(_imageSpace.BufferCount);
            }
        }

        //------- ImageSpace와 카메라 GrabManager의 버퍼를 초기화하고 매핑 -------
        public void SetBuffer(int bufferCount)
        {
            _imageSpace.InitImageSpace(bufferCount);
            if (_grabManager != null)
            {
                _grabManager.InitBuffer(bufferCount);
                for (int i = 0; i < bufferCount; i++)
                    _grabManager.SetBuffer(
                        _imageSpace.GetInspectionBuffer(i),
                        _imageSpace.GetnspectionBufferPtr(i),
                        _imageSpace.GetInspectionBufferHandle(i), i);
            }
            SLogger.Write("버퍼 초기화 성공");
        }

        //===== [그룹 5] 티칭 및 ROI 윈도 참조 관리 =====

        //------- 선택된 검사 영역(Window)의 설정값을 속성창(UI)에 반영 -------
        private void UpdateProperty(InspWindow w)
        {
            if (w == null) return;
            MainForm.GetDockForm<PropertiesForm>()?.UpdateProperty(w);
        }

        //------- 현재 선택된 검사 영역의 마스터(티칭) 이미지를 교체하거나 새로 추가 -------
        public void UpdateTeachingImage(int index)
        { if (_selectedInspWindow != null) SetTeachingImage(_selectedInspWindow, index); }

        //------- 지정된 인덱스의 마스터(티칭) 이미지를 삭제 -------
        public void DelTeachingImage(int index)
        {
            if (_selectedInspWindow == null) return;
            _selectedInspWindow.DelWindowImage(index);
            if (_selectedInspWindow.FindInspAlgorithm(InspectType.InspMatch) is MatchAlgorithm)
                UpdateProperty(_selectedInspWindow);
        }

        //------- 실제 화면(뷰어)에서 해당 ROI 좌표만큼 크롭하여 티칭 이미지로 등록 -------
        public void SetTeachingImage(InspWindow inspWindow, int index = -1)
        {
            if (inspWindow == null) return;
            CameraForm cf = MainForm.GetDockForm<CameraForm>();
            if (cf == null) return;
            Mat curImage = cf.GetDisplayImage();
            if (curImage == null) return;
            if (inspWindow.WindowArea.Right >= curImage.Width ||
                inspWindow.WindowArea.Bottom >= curImage.Height)
            { SLogger.Write("ROI 영역이 잘못되었습니다."); return; }

            Mat windowImage = curImage[inspWindow.WindowArea];
            if (index < 0) inspWindow.AddWindowImage(windowImage);
            else inspWindow.SetWindowImage(windowImage, index);
            inspWindow.IsPatternLearn = false;

            if (inspWindow.FindInspAlgorithm(InspectType.InspMatch) is MatchAlgorithm matchAlgo)
            {
                matchAlgo.ImageChannel = SelImageChannel;
                if (matchAlgo.ImageChannel == eImageChannel.Color)
                    matchAlgo.ImageChannel = eImageChannel.Gray;
                UpdateProperty(inspWindow);
            }
        }

        //------- 현재 선택된 단일 ROI에 대해서만 테스트 검사 수행 -------
        public void TryInspection(InspWindow w) { UpdateDiagramEntity(); InspWorker.TryInspect(w, InspectType.InspNone); }

        //------- 뷰어나 트리에서 검사 영역(ROI)을 클릭/선택 시 상태 동기화 -------
        public void SelectInspWindow(InspWindow inspWindow)
        {
            _selectedInspWindow = inspWindow;
            var propForm = MainForm.GetDockForm<PropertiesForm>();
            if (propForm != null)
            {
                if (inspWindow == null) { propForm.ResetProperty(); return; }
                propForm.ShowProperty(inspWindow);
            }
            UpdateProperty(inspWindow);
            Global.Inst.InspStage.PreView.SetInspWindow(inspWindow);
        }

        //------- 화면에 새로운 검사 영역(ROI Window) 생성 및 기본 티칭 -------
        public void AddInspWindow(InspWindowType windowType, Rect rect)
        {
            InspWindow w = _model.AddInspWindow(windowType);
            if (w == null) return;
            w.WindowArea = rect; w.IsTeach = false;
            SetTeachingImage(w); UpdateProperty(w); UpdateDiagramEntity();
            CameraForm cf = MainForm.GetDockForm<CameraForm>();
            if (cf != null) { cf.SelectDiagramEntity(w); SelectInspWindow(w); }
        }

        //------- 기존 검사 영역(ROI)을 오프셋만큼 이동하여 복제 -------
        public bool AddInspWindow(InspWindow src, OpenCvSharp.Point offset)
        {
            InspWindow clone = src.Clone(offset);
            if (clone == null || !_model.AddInspWindow(clone)) return false;
            UpdateProperty(clone); UpdateDiagramEntity();
            CameraForm cf = MainForm.GetDockForm<CameraForm>();
            if (cf != null) { cf.SelectDiagramEntity(clone); SelectInspWindow(clone); }
            return true;
        }

        //------- 검사 영역(ROI) 좌표 이동 -------
        public void MoveInspWindow(InspWindow w, OpenCvSharp.Point offset)
        { if (w != null) { w.OffsetMove(offset); UpdateProperty(w); } }

        //------- 검사 영역(ROI) 크기 및 위치 재조정 -------
        public void ModifyInspWindow(InspWindow w, Rect rect)
        { if (w != null) { w.WindowArea = rect; w.IsTeach = false; UpdateProperty(w); } }

        //------- 검사 영역(ROI) 단일 삭제 -------
        public void DelInspWindow(InspWindow w) { _model.DelInspWindow(w); UpdateDiagramEntity(); }
        //------- 검사 영역(ROI) 다중 삭제 -------
        public void DelInspWindow(List<InspWindow> list) { _model.DelInspWindowList(list); UpdateDiagramEntity(); }

        //===== [그룹 6] 카메라 제어 및 그랩 =====

        //------- 카메라에 1프레임 캡처 명령 하달 -------
        public bool Grab(int bufferIndex)
        {
            if (_grabManager == null) return false;
            return _grabManager.Grab(bufferIndex, true);
        }

        //------- 카메라 캡처(Grab) 완료 시 호출되는 비동기 콜백 -------
        private async void _multiGrab_TransferCompleted(object sender, object e)
        {
            int bufferIndex = (int)e;
            SLogger.Write($"TransferCompleted {bufferIndex}");
            _imageSpace.Split(bufferIndex);

            if (SaveCamImage && Directory.Exists(_capturePath))
            {
                Mat img = GetMat(0, eImageChannel.Color);
                if (img != null)
                    img.SaveImage(Path.Combine(_capturePath, $"{++SaveImageIndex:D4}.png"));
            }

            if (!_isInspectMode)
            {
                DisplayGrabImage(bufferIndex);
            }

            if (LiveMode)
            {
                SLogger.Write("Grab");
                await Task.Delay(100);
                _grabManager.Grab(bufferIndex, true);
            }

            if (_isInspectMode) RunInspect();
        }

        //===== [그룹 7] 디스플레이 및 화면 갱신 =====

        //------- 메인 뷰어(CameraForm)에 캡처된 이미지 출력 -------
        private void DisplayGrabImage(int bufferIndex)
            => MainForm.GetDockForm<CameraForm>()?.UpdateDisplay();

        //------- 가공된 Bitmap을 메인 뷰어에 강제 출력 -------
        public void UpdateDisplay(Bitmap bitmap)
            => MainForm.GetDockForm<CameraForm>()?.UpdateDisplay(bitmap);

        //------- 프리뷰(미리보기) 창 이미지 갱신 -------
        public void SetPreviewImage(eImageChannel channel)
        {
            if (_previewImage == null) return;
            _previewImage.SetImage(BitmapConverter.ToMat(ImageSpace.GetBitmap(0, channel)));
            SetImageChannel(channel);
        }

        //------- 화면에 보여줄 컬러/흑백 채널 선택 -------
        public void SetImageChannel(eImageChannel channel)
            => MainForm.GetDockForm<CameraForm>()?.SetImageChannel(channel);

        //------- 지정된 버퍼/채널의 이미지를 Bitmap(WinForms용) 포맷으로 반환 -------
        public Bitmap GetBitmap(int bufferIndex = -1, eImageChannel imageChannel = eImageChannel.None)
        {
            if (bufferIndex >= 0) SelBufferIndex = bufferIndex;
            if (imageChannel != eImageChannel.None) SelImageChannel = imageChannel;
            if (ImageSpace == null) return null;
            return ImageSpace.GetBitmap(SelBufferIndex, SelImageChannel);
        }

        //------- 지정된 버퍼/채널의 이미지를 Mat(OpenCV용) 포맷으로 반환 -------
        public Mat GetMat(int bufferIndex = -1, eImageChannel imageChannel = eImageChannel.None)
        {
            if (bufferIndex >= 0) SelBufferIndex = bufferIndex;
            return ImageSpace.GetMat(SelBufferIndex, imageChannel);
        }

        //------- 화면 위에 그려진 도형(ROI 박스 등) UI 갱신 -------
        public void UpdateDiagramEntity()
        {
            MainForm.GetDockForm<CameraForm>()?.UpdateDiagramEntity();
            MainForm.GetDockForm<ModelTreeForm>()?.UpdateDiagramEntity();
        }

        //------- 메인 이미지 뷰어 강제 다시 그리기 -------
        public void RedrawMainView() => MainForm.GetDockForm<CameraForm>()?.UpdateImageViewer();
        //------- 화면에 표시된 오버레이(도형/결과) 초기화 -------
        public void ResetDisplay() => MainForm.GetDockForm<CameraForm>()?.ResetDisplay();

        //===== [그룹 8] 모델 데이터 관리 =====

        //------- 디스크에서 검사 모델(레시피) 정보 불러오기 -------
        public bool LoadModel(string filePath)
        {
            SLogger.Write($"모델 로딩:{filePath}");
            _model = _model.Load(filePath);
            if (_model == null) { SLogger.Write($"모델 로딩 실패:{filePath}"); return false; }
            if (File.Exists(_model.InspectImagePath)) SetImageBuffer(_model.InspectImagePath);
            UpdateDiagramEntity();
            _regKey.SetValue("LastestModelPath", filePath);
            return true;
        }

        //------- 현재 설정된 검사 모델을 파일로 저장 -------
        public void SaveModel(string filePath)
        {
            SLogger.Write($"모델 저장:{filePath}");
            if (string.IsNullOrEmpty(filePath)) CurModel.Save();
            else CurModel.SaveAs(filePath);
        }

        //------- 프로그램 구동 시 마지막에 사용했던 모델 자동 열기 팝업 -------
        private bool LastestModelOpen()
        {
            if (_lastestModelOpen) return true;
            _lastestModelOpen = true;
            string path = (string)_regKey.GetValue("LastestModelPath");
            if (!File.Exists(path)) return true;
            DialogResult r = MessageBox.Show(
                $"최근 모델을 불러오시겠습니까?\r\n[{path}] ",
                "최근 모델 불러오기", MessageBoxButtons.YesNo);
            return r == DialogResult.No ? true : LoadModel(path);
        }

        //===== [그룹 9] 검사 사이클 및 시퀀스 처리 =====

        //------- 루프 사이클 또는 단일 컷 검사 트리거 -------
        public void CycleInspect(bool isCycle)
        {
            if (InspWorker.IsRunning) return;

            if (!UseCamera)
            {
                string inspImagePath = CurModel.InspectImagePath;
                if (inspImagePath == "") return;

                string inspImageDir = Path.GetDirectoryName(inspImagePath);
                if (!Directory.Exists(inspImageDir)) return;

                if (!_imageLoader.IsLoadedImages())
                    _imageLoader.LoadImages(inspImageDir);
            }

            if (isCycle)
            {
                // 사이클: 소진됐으면 자동 리셋 후 시작
                if (!UseCamera && _imageLoader.RemainingCount == 0)
                    _imageLoader.Reset();
                _inspWorker.StartCycleInspectImage();
            }
            else
            {
                // 단일: 소진 상태면 팝업
                if (!UseCamera && _imageLoader.RemainingCount == 0)
                {
                    DialogResult answer = MessageBox.Show(
                        "마지막 이미지입니다.\n다시 검사하시겠습니까?",
                        "검사 완료",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (answer == DialogResult.Yes)
                        _imageLoader.Reset();   // 처음부터
                    else
                        return;                 // 마지막 화면 유지
                }
                OneCycle();
            }
        }

        //------- 단일 검사 루틴 1회 실행 (더 이상 이미지가 없으면 false 반환) -------
        public bool OneCycle()
        {
            bool grabSuccess = UseCamera ? Grab(0) : VirtualGrab();

            if (!grabSuccess)
            {
                SLogger.Write("모든 이미지 검사 완료 - 사이클 종료");
                StopCycle();
                return false;
            }
            RunInspect();
            Thread.Sleep(100);
            return true;
        }

        //------- 현재 버퍼 이미지에 대해 매칭/볼트/마킹 통합 검사 실행 및 UI 반영 -------
        private void RunInspect()
        {
            bool isDefect = false;
            _inspWorker.RunInspect(out isDefect);
            RunKeyMatch();
            RunBoltMark();

            _inspWorker.RunDisplayWithOptions(
                showKeyboard: xylophone.ShowKeyboard,
                showBolt: xylophone.ShowBolt,
                showMark: xylophone.ShowMark);

            DisplayGrabImage(0);
        }

        //------- 진행 중인 전체 검사 루프와 시퀀스 강제 종료 -------
        public void StopCycle()
        {
            _inspWorker?.Stop();
            VisionSequence.Inst.StopAutoRun();
            _isInspectMode = false;
            SetWorkingState(WorkingState.NONE);
        }

        //------- 실물 카메라 대신 폴더의 이미지를 차례대로 불러와 버퍼에 적재 -------
        public bool VirtualGrab()
        {
            if (_imageLoader == null) return false;
            string path = _imageLoader.GetNextImagePath();
            if (path == "") return false;
            SetImageBuffer(path);
            _imageSpace.Split(0);
            return true;
        }

        //------- 외부(PLC 등) 시퀀스 제어 명령(Start/End 등) 수신 이벤트 핸들러 -------
        private void SeqCommand(object sender, SeqCmd seqCmd, object Param)
        {
            switch (seqCmd)
            {
                case SeqCmd.InspStart:
                    SLogger.Write("MMI : InspStart", SLogger.LogType.Info);
                    if (UseCamera) { if (!Grab(0)) SLogger.Write("Failed to grab", SLogger.LogType.Error); }
                    else { if (!VirtualGrab()) SLogger.Write("Failed to virtual grab", SLogger.LogType.Error); }
                    break;
                case SeqCmd.InspEnd:
                    SLogger.Write("MMI : InspEnd", SLogger.LogType.Info);
                    VisionSequence.Inst.VisionCommand(Vision2Mmi.InspEnd, "");
                    break;
            }
        }

        //------- 검사 시작 전 로트/바코드 정보를 세팅하고 UI/버퍼 상태를 준비 -------
        public bool InspectReady(string lotNumber, string serialID)
        {
            _lotNumber = lotNumber;
            _serialID = serialID;
            LiveMode = false;
            UseCamera = SettingXml.Inst.CamType != CameraType.None;
            CheckImageBuffer();
            ResetDisplay();
            return true;
        }

        //------- 양산 모드(AutoRun) 시퀀스 구동 및 캡처 폴더 비우기 -------
        public bool StartAutoRun()
        {
            SLogger.Write("Action : StartAutoRun");

            if (SaveCamImage && _model != null)
            {
                SaveImageIndex = 0;
                _capturePath = Path.Combine(Path.GetDirectoryName(_model.ModelPath), "Capture");
                if (!Directory.Exists(_capturePath))
                    Directory.CreateDirectory(_capturePath);
                else
                    foreach (string f in Directory.GetFiles(_capturePath))
                        try { File.Delete(f); }
                        catch (Exception ex) { SLogger.Write($"파일 삭제 실패: {f} / {ex.Message}", SLogger.LogType.Error); }
            }

            string modelPath = CurModel.ModelPath;
            if (modelPath == "")
            { SLogger.Write("모델이 없습니다.", SLogger.LogType.Error); MessageBox.Show("모델이 없습니다."); return false; }

            LiveMode = false;
            UseCamera = SettingXml.Inst.CamType != CameraType.None;
            SetWorkingState(WorkingState.INSPECT);
            VisionSequence.Inst.StartAutoRun(Path.GetFileNameWithoutExtension(modelPath));
            _isInspectMode = true;
            return true;
        }

        //===== [그룹 10] 기타 =====

        //------- 상태 표시줄(진행중, 대기 등) UI 텍스트 변경 -------
        public void SetWorkingState(WorkingState ws) => MainForm.GetDockForm<CameraForm>()?.SetWorkingState(ws);

        //------- 카메라 노출(Exposure) 값 설정 -------
        public void SetExposure(long exposureTime) => _grabManager?.SetExposureTime(exposureTime);

        //===== [그룹 11] 리소스 해제 =====

        private bool disposed = false;

        //------- 클래스 소멸 시 연결된 모든 리소스(카메라, 스레드, AI 메모리 등) 완전 해제 -------
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    VisionSequence.Inst.SeqCommand -= SeqCommand;
                    _inspWorker?.Dispose();
                    if (_saigeAI != null) { _saigeAI.Dispose(); _saigeAI = null; }
                    if (_grabManager != null) { _grabManager.Dispose(); _grabManager = null; }
                    _regKey?.Close();
                }
                disposed = true;
            }
        }

        public void Dispose() { Dispose(true); }
    }
}