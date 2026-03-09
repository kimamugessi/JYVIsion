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
        //===== [그룹 1] 필드 및 속성 설정 =====
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

        public SaigeAI AIModule
        {
            get
            {
                if (_saigeAI == null) _saigeAI = new SaigeAI();
                return _saigeAI;
            }
        }

        public InspStage() { }

        //===== [그룹 2] 외부 호출 진입점 =====

        //------- 건반 위치 감지 (세로 길이 자동 보정) -------
        public List<DrawInspectInfo> RunKeyMatch()
        {
            return _inspWorker.RunKeyMatch();
        }

        //------- 건반 기준 볼트, 각인 검사 실행 -------
        public List<DrawInspectInfo> RunBoltMark()
        {
            return _inspWorker.RunBoltMark();
        }

        //------- 옵션 기반 디스플레이 제어 -------
        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
        {
            _inspWorker.RunDisplayWithOptions(showKeyboard, showBolt, showMark);
        }

        //===== [그룹 3] 초기화 및 설정 =====

        //------- 스테이지 초기화 -------
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

            if (_grabManager != null && _grabManager.InitGrab() == true)
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

        //------- 설정 파일 로드 -------
        private void LoadSetting()
        {
            _camType = SettingXml.Inst.CamType;
        }

        //------- 카메라 해상도 및 버퍼 초기화 -------
        public void InitModelGrab(int bufferCount)
        {
            if (_grabManager == null) return;

            int pixelBpp = 8;
            _grabManager.GetPixelBpp(out pixelBpp);

            int inspectionWidth, inspectionHeight, inspectionStride;
            _grabManager.GetResolution(out inspectionWidth, out inspectionHeight, out inspectionStride);

            if (_imageSpace != null)
                _imageSpace.SetImageInfo(pixelBpp, inspectionWidth, inspectionHeight, inspectionStride);

            SetBuffer(bufferCount);

            eImageChannel imageChannel = (pixelBpp == 24) ? eImageChannel.Color : eImageChannel.Gray;
            SetImageChannel(imageChannel);
        }

        //===== [그룹 4] 이미지 버퍼 및 메모리 관리 =====

        //------- 파일로부터 이미지 버퍼 설정 -------
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

                    using (Mat alignedMat = new Mat(matImage.Height, alignedWidth, matImage.Type(), Scalar.Black))
                    {
                        matImage.CopyTo(alignedMat[new Rect(0, 0, matImage.Width, matImage.Height)]);

                        long bufSize = alignedMat.Total() * alignedMat.ElemSize();
                        IntPtr destPtr = ImageSpace.GetnspectionBufferPtr(0);

                        if (destPtr != IntPtr.Zero)
                        {
                            byte[] managedBuf = new byte[bufSize];
                            Marshal.Copy(alignedMat.Data, managedBuf, 0, (int)bufSize);
                            Marshal.Copy(managedBuf, 0, destPtr, (int)bufSize);
                        }
                    }
                }
                _imageSpace.Split(0);
                DisplayGrabImage(0);
            }
            catch (Exception ex)
            {
                SLogger.Write($"SetImageBuffer 오류: {ex.Message}", SLogger.LogType.Error);
            }
        }

        //------- 이미지 버퍼 상태 체크 및 갱신 -------
        public void CheckImageBuffer()
        {
            if (_grabManager == null || SettingXml.Inst.CamType == CameraType.None) return;

            int imageWidth, imageHeight, imageStride;
            _grabManager.GetResolution(out imageWidth, out imageHeight, out imageStride);

            if (_imageSpace.ImageSize.Width != imageWidth ||
                _imageSpace.ImageSize.Height != imageHeight)
            {
                int pixelBpp = 8;
                _grabManager.GetPixelBpp(out pixelBpp);
                _imageSpace.SetImageInfo(pixelBpp, imageWidth, imageHeight, imageStride);
                SetBuffer(_imageSpace.BufferCount);
            }
        }

        //------- 메모리 버퍼 초기화 및 하드웨어 연동 -------
        public void SetBuffer(int bufferCount)
        {
            _imageSpace.InitImageSpace(bufferCount);

            if (_grabManager != null)
            {
                _grabManager.InitBuffer(bufferCount);
                for (int i = 0; i < bufferCount; i++)
                {
                    _grabManager.SetBuffer(
                        _imageSpace.GetInspectionBuffer(i),
                        _imageSpace.GetnspectionBufferPtr(i),
                        _imageSpace.GetInspectionBufferHandle(i),
                        i);
                }
            }
            SLogger.Write("버퍼 초기화 성공");
        }

        //===== [그룹 5] 티칭 및 ROI 윈도우 관리 =====

        private void UpdateProperty(InspWindow inspWindow)
        {
            if (inspWindow == null) return;
            PropertiesForm propertiesForm = MainForm.GetDockForm<PropertiesForm>();
            if (propertiesForm == null) return;
            propertiesForm.UpdateProperty(inspWindow);
        }

        public void UpdateTeachingImage(int index)
        {
            if (_selectedInspWindow == null) return;
            SetTeachingImage(_selectedInspWindow, index);
        }

        public void DelTeachingImage(int index)
        {
            if (_selectedInspWindow == null) return;
            _selectedInspWindow.DelWindowImage(index);
            MatchAlgorithm matchAlgo = (MatchAlgorithm)_selectedInspWindow.FindInspAlgorithm(InspectType.InspMatch);
            if (matchAlgo != null)
                UpdateProperty(_selectedInspWindow);
        }

        public void SetTeachingImage(InspWindow inspWindow, int index = -1)
        {
            if (inspWindow == null) return;

            CameraForm cameraForm = MainForm.GetDockForm<CameraForm>();
            if (cameraForm == null) return;

            Mat curImage = cameraForm.GetDisplayImage();
            if (curImage == null) return;

            if (inspWindow.WindowArea.Right >= curImage.Width ||
                inspWindow.WindowArea.Bottom >= curImage.Height)
            {
                SLogger.Write("ROI 영역이 잘못되었습니다.");
                return;
            }

            Mat windowImage = curImage[inspWindow.WindowArea];

            if (index < 0) inspWindow.AddWindowImage(windowImage);
            else inspWindow.SetWindowImage(windowImage, index);

            inspWindow.IsPatternLearn = false;

            MatchAlgorithm matchAlgo = (MatchAlgorithm)inspWindow.FindInspAlgorithm(InspectType.InspMatch);
            if (matchAlgo != null)
            {
                matchAlgo.ImageChannel = SelImageChannel;
                if (matchAlgo.ImageChannel == eImageChannel.Color)
                    matchAlgo.ImageChannel = eImageChannel.Gray;
                UpdateProperty(inspWindow);
            }
        }

        public void TryInspection(InspWindow inspWindow)
        {
            UpdateDiagramEntity();
            InspWorker.TryInspect(inspWindow, InspectType.InspNone);
        }

        public void SelectInspWindow(InspWindow inspWindow)
        {
            _selectedInspWindow = inspWindow;

            var propForm = MainForm.GetDockForm<PropertiesForm>();
            if (propForm != null)
            {
                if (inspWindow is null) { propForm.ResetProperty(); return; }
                propForm.ShowProperty(inspWindow);
            }

            UpdateProperty(inspWindow);
            Global.Inst.InspStage.PreView.SetInspWindow(inspWindow);
        }

        public void AddInspWindow(InspWindowType windowType, Rect rect)
        {
            InspWindow inspWindow = _model.AddInspWindow(windowType);
            if (inspWindow is null) return;

            inspWindow.WindowArea = rect;
            inspWindow.IsTeach = false;
            SetTeachingImage(inspWindow);
            UpdateProperty(inspWindow);
            UpdateDiagramEntity();

            CameraForm cameraForm = MainForm.GetDockForm<CameraForm>();
            if (cameraForm != null)
            {
                cameraForm.SelectDiagramEntity(inspWindow);
                SelectInspWindow(inspWindow);
            }
        }

        public bool AddInspWindow(InspWindow sourceWindow, OpenCvSharp.Point offset)
        {
            InspWindow cloneWindow = sourceWindow.Clone(offset);
            if (cloneWindow is null || !_model.AddInspWindow(cloneWindow)) return false;

            UpdateProperty(cloneWindow);
            UpdateDiagramEntity();

            CameraForm cameraForm = MainForm.GetDockForm<CameraForm>();
            if (cameraForm != null)
            {
                cameraForm.SelectDiagramEntity(cloneWindow);
                SelectInspWindow(cloneWindow);
            }
            return true;
        }

        public void MoveInspWindow(InspWindow inspWindow, OpenCvSharp.Point offset)
        {
            if (inspWindow == null) return;
            inspWindow.OffsetMove(offset);
            UpdateProperty(inspWindow);
        }

        public void ModifyInspWindow(InspWindow inspWindow, Rect rect)
        {
            if (inspWindow == null) return;
            inspWindow.WindowArea = rect;
            inspWindow.IsTeach = false;
            UpdateProperty(inspWindow);
        }

        public void DelInspWindow(InspWindow inspWindow)
        {
            _model.DelInspWindow(inspWindow);
            UpdateDiagramEntity();
        }

        public void DelInspWindow(List<InspWindow> inspWindowList)
        {
            _model.DelInspWindowList(inspWindowList);
            UpdateDiagramEntity();
        }

        //===== [그룹 6] 카메라 제어 및 그랩 =====

        public bool Grab(int bufferIndex)
        {
            if (_grabManager == null) return false;
            return _grabManager.Grab(bufferIndex, true);
        }

        private async void _multiGrab_TransferCompleted(object sender, object e)
        {
            int bufferIndex = (int)e;
            SLogger.Write($"TransferCompleted {bufferIndex}");

            _imageSpace.Split(bufferIndex);

            if (SaveCamImage && Directory.Exists(_capturePath))
            {
                Mat curImage = GetMat(0, eImageChannel.Color);
                if (curImage != null)
                {
                    string savePath = Path.Combine(_capturePath, $"{++SaveImageIndex:D4}.png");
                    curImage.SaveImage(savePath);
                }
            }

            DisplayGrabImage(bufferIndex);

            if (LiveMode)
            {
                SLogger.Write("Grab");
                await Task.Delay(100);
                _grabManager.Grab(bufferIndex, true);
            }

            if (_isInspectMode)
                RunInspect();
        }

        //===== [그룹 7] 디스플레이 및 화면 갱신 =====

        private void DisplayGrabImage(int bufferIndex)
        {
            MainForm.GetDockForm<CameraForm>()?.UpdateDisplay();
        }

        public void UpdateDisplay(Bitmap bitmap)
        {
            MainForm.GetDockForm<CameraForm>()?.UpdateDisplay(bitmap);
        }

        public void SetPreviewImage(eImageChannel channel)
        {
            if (_previewImage == null) return;
            Bitmap bitmap = ImageSpace.GetBitmap(0, channel);
            _previewImage.SetImage(BitmapConverter.ToMat(bitmap));
            SetImageChannel(channel);
        }

        public void SetImageChannel(eImageChannel channel)
        {
            MainForm.GetDockForm<CameraForm>()?.SetImageChannel(channel);
        }

        public Bitmap GetBitmap(int bufferIndex = -1, eImageChannel imageChannel = eImageChannel.None)
        {
            if (bufferIndex >= 0) SelBufferIndex = bufferIndex;
            if (imageChannel != eImageChannel.None) SelImageChannel = imageChannel;
            if (ImageSpace == null) return null;
            return ImageSpace.GetBitmap(SelBufferIndex, SelImageChannel);
        }

        public Mat GetMat(int bufferIndex = -1, eImageChannel imageChannel = eImageChannel.None)
        {
            if (bufferIndex >= 0) SelBufferIndex = bufferIndex;
            return ImageSpace.GetMat(SelBufferIndex, imageChannel);
        }

        public void UpdateDiagramEntity()
        {
            MainForm.GetDockForm<CameraForm>()?.UpdateDiagramEntity();
            MainForm.GetDockForm<ModelTreeForm>()?.UpdateDiagramEntity();
        }

        public void RedrawMainView()
        {
            MainForm.GetDockForm<CameraForm>()?.UpdateImageViewer();
        }

        public void ResetDisplay()
        {
            MainForm.GetDockForm<CameraForm>()?.ResetDisplay();
        }

        //===== [그룹 8] 모델 데이터 관리 =====

        public bool LoadModel(string filePath)
        {
            SLogger.Write($"모델 로딩:{filePath}");
            _model = _model.Load(filePath);

            if (_model == null)
            {
                SLogger.Write($"모델 로딩 실패:{filePath}");
                return false;
            }

            string inspImagePath = _model.InspectImagePath;
            if (File.Exists(inspImagePath))
                SetImageBuffer(inspImagePath);

            UpdateDiagramEntity();
            _regKey.SetValue("LastestModelPath", filePath);
            return true;
        }

        public void SaveModel(string filePath)
        {
            SLogger.Write($"모델 저장:{filePath}");
            if (string.IsNullOrEmpty(filePath))
                CurModel.Save();
            else
                CurModel.SaveAs(filePath);
        }

        private bool LastestModelOpen()
        {
            if (_lastestModelOpen) return true;
            _lastestModelOpen = true;

            string lastestModel = (string)_regKey.GetValue("LastestModelPath");
            if (!File.Exists(lastestModel)) return true;

            DialogResult result = MessageBox.Show(
                $"최근 모델을 불러오시겠습니까?\r\n[{lastestModel}] ",
                "최근 모델 불러오기", MessageBoxButtons.YesNo);

            return result == DialogResult.No ? true : LoadModel(lastestModel);
        }

        //===== [그룹 9] 검사 사이클 및 시퀀스 처리 =====

        public void CycleInspect(bool isCycle)
        {
            if (InspWorker.IsRunning) return;

            if (!UseCamera)
            {
                string inspImagePath = CurModel.InspectImagePath;
                if (inspImagePath == "") return;

                string inspImageDir = Path.GetDirectoryName(inspImagePath);
                if (Directory.Exists(inspImageDir))
                {
                    if (!_imageLoader.IsLoadedImages())
                        _imageLoader.LoadImages(inspImageDir);
                }
            }

            if (isCycle) _inspWorker.StartCycleInspectImage();
            else OneCycle();
        }

        //------- 검사 한 주기 실행 -------
        public void OneCycle()
        {
            ResetDisplay();

            bool grabSuccess = UseCamera ? Grab(0) : VirtualGrab();
            if (!grabSuccess) return;

            RunInspect();

            Thread.Sleep(300);
        }

        //------- 실제 검사 프로세스 실행 (체크박스 필터 적용) -------
        private void RunInspect()
        {
            bool isDefect = false;

            // 1. 윈도우 검사 실행
            _inspWorker.RunInspect(out isDefect);

            // 2. 건반 위치 탐색 (_lastMatchedKeyRects 갱신, 항상 실행)
            RunKeyMatch();

            // 3. 볼트/각인 검사 실행 (ResultForm 결과 전송, 항상 실행)
            RunBoltMark();

            // 4. ✅ static 프로퍼티로 체크박스 상태 읽어서 필터링된 결과만 화면 표시
            _inspWorker.RunDisplayWithOptions(
                showKeyboard: JYVision.Property.xylophone.ShowKeyboard,
                showBolt: JYVision.Property.xylophone.ShowBolt,
                showMark: JYVision.Property.xylophone.ShowMark);
        }

        //------- 사이클 중지 -------
        public void StopCycle()
        {
            if (_inspWorker != null) _inspWorker.Stop();
            VisionSequence.Inst.StopAutoRun();
            _isInspectMode = false;
            SetWorkingState(WorkingState.NONE);
        }

        //------- 가상(이미지 파일) 그랩 -------
        public bool VirtualGrab()
        {
            if (_imageLoader is null) return false;

            string imagePath = _imageLoader.GetNextImagePath();
            if (imagePath == "") return false;

            SetImageBuffer(imagePath);
            _imageSpace.Split(0);
            return true;
        }

        //------- 시퀀스 명령 핸들러 -------
        private void SeqCommand(object sender, SeqCmd seqCmd, object Param)
        {
            switch (seqCmd)
            {
                case SeqCmd.InspStart:
                    SLogger.Write("MMI : InspStart", SLogger.LogType.Info);
                    if (UseCamera)
                    { if (!Grab(0)) SLogger.Write("Failed to grab", SLogger.LogType.Error); }
                    else
                    { if (!VirtualGrab()) SLogger.Write("Failed to virtual grab", SLogger.LogType.Error); }
                    break;

                case SeqCmd.InspEnd:
                    SLogger.Write("MMI : InspEnd", SLogger.LogType.Info);
                    SLogger.Write("검사 종료");
                    VisionSequence.Inst.VisionCommand(Vision2Mmi.InspEnd, "");
                    break;
            }
        }

        //------- 검사 준비 -------
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

        //------- 자동 운전 가동 -------
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
                    foreach (string file in Directory.GetFiles(_capturePath))
                        try { File.Delete(file); }
                        catch (Exception ex)
                        { SLogger.Write($"파일 삭제 실패: {file} / {ex.Message}", SLogger.LogType.Error); }
            }

            string modelPath = CurModel.ModelPath;
            if (modelPath == "")
            {
                SLogger.Write("모델이 없습니다.", SLogger.LogType.Error);
                MessageBox.Show("모델이 없습니다.");
                return false;
            }

            LiveMode = false;
            UseCamera = SettingXml.Inst.CamType != CameraType.None;
            SetWorkingState(WorkingState.INSPECT);

            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            VisionSequence.Inst.StartAutoRun(modelName);
            _isInspectMode = true;
            return true;
        }

        //===== [그룹 10] 기타 제어 및 유틸리티 =====

        public void SetWorkingState(WorkingState workingState)
        {
            MainForm.GetDockForm<CameraForm>()?.SetWorkingState(workingState);
        }

        public void SetExposure(long exposureTime)
        {
            _grabManager?.SetExposureTime(exposureTime);
        }

        //===== [그룹 11] 리소스 해제 =====

        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    VisionSequence.Inst.SeqCommand -= SeqCommand;
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