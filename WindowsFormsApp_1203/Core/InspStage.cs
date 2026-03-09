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

        public SaigeAI AIModule
        {
            get { if (_saigeAI == null) _saigeAI = new SaigeAI(); return _saigeAI; }
        }

        public InspStage() { }

        //===== [그룹 2] 외부 호출 진입점 =====

        public List<DrawInspectInfo> RunKeyMatch()
            => _inspWorker.RunKeyMatch();

        public List<DrawInspectInfo> RunBoltMark()
            => _inspWorker.RunBoltMark();

        public void RunDisplayWithOptions(bool showKeyboard, bool showBolt, bool showMark)
            => _inspWorker.RunDisplayWithOptions(showKeyboard, showBolt, showMark);

        //------- Clear 버튼 → 이미지 카운터 초기화 -------
        public void ResetImageLoader()
        {
            _imageLoader?.Reset();
            SLogger.Write("이미지 카운터 초기화 (ResultForm Clear)");
        }

        //===== [그룹 3] 초기화 및 설정 =====

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

        private void LoadSetting()
        {
            _camType = SettingXml.Inst.CamType;
        }

        public void InitModelGrab(int bufferCount)
        {
            if (_grabManager == null) return;

            int pixelBpp = 8;
            _grabManager.GetPixelBpp(out pixelBpp);

            int w, h, stride;
            _grabManager.GetResolution(out w, out h, out stride);

            _imageSpace?.SetImageInfo(pixelBpp, w, h, stride);
            SetBuffer(bufferCount);

            eImageChannel ch = (pixelBpp == 24) ? eImageChannel.Color : eImageChannel.Gray;
            SetImageChannel(ch);
        }

        //===== [그룹 4] 이미지 버퍼 및 메모리 관리 =====

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
                DisplayGrabImage(0);
            }
            catch (Exception ex)
            {
                SLogger.Write($"SetImageBuffer 오류: {ex.Message}", SLogger.LogType.Error);
            }
        }

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

        //===== [그룹 5] 티칭 및 ROI 윈도우 관리 =====

        private void UpdateProperty(InspWindow w)
        {
            if (w == null) return;
            MainForm.GetDockForm<PropertiesForm>()?.UpdateProperty(w);
        }

        public void UpdateTeachingImage(int index)
        {
            if (_selectedInspWindow != null) SetTeachingImage(_selectedInspWindow, index);
        }

        public void DelTeachingImage(int index)
        {
            if (_selectedInspWindow == null) return;
            _selectedInspWindow.DelWindowImage(index);
            if (_selectedInspWindow.FindInspAlgorithm(InspectType.InspMatch) is MatchAlgorithm)
                UpdateProperty(_selectedInspWindow);
        }

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

        public void TryInspection(InspWindow w) { UpdateDiagramEntity(); InspWorker.TryInspect(w, InspectType.InspNone); }

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

        public void AddInspWindow(InspWindowType windowType, Rect rect)
        {
            InspWindow w = _model.AddInspWindow(windowType);
            if (w == null) return;
            w.WindowArea = rect; w.IsTeach = false;
            SetTeachingImage(w); UpdateProperty(w); UpdateDiagramEntity();
            CameraForm cf = MainForm.GetDockForm<CameraForm>();
            if (cf != null) { cf.SelectDiagramEntity(w); SelectInspWindow(w); }
        }

        public bool AddInspWindow(InspWindow src, OpenCvSharp.Point offset)
        {
            InspWindow clone = src.Clone(offset);
            if (clone == null || !_model.AddInspWindow(clone)) return false;
            UpdateProperty(clone); UpdateDiagramEntity();
            CameraForm cf = MainForm.GetDockForm<CameraForm>();
            if (cf != null) { cf.SelectDiagramEntity(clone); SelectInspWindow(clone); }
            return true;
        }

        public void MoveInspWindow(InspWindow w, OpenCvSharp.Point offset)
        { if (w != null) { w.OffsetMove(offset); UpdateProperty(w); } }

        public void ModifyInspWindow(InspWindow w, Rect rect)
        { if (w != null) { w.WindowArea = rect; w.IsTeach = false; UpdateProperty(w); } }

        public void DelInspWindow(InspWindow w) { _model.DelInspWindow(w); UpdateDiagramEntity(); }
        public void DelInspWindow(List<InspWindow> list) { _model.DelInspWindowList(list); UpdateDiagramEntity(); }

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
                Mat img = GetMat(0, eImageChannel.Color);
                if (img != null)
                    img.SaveImage(Path.Combine(_capturePath, $"{++SaveImageIndex:D4}.png"));
            }

            DisplayGrabImage(bufferIndex);

            if (LiveMode)
            {
                SLogger.Write("Grab");
                await Task.Delay(100);
                _grabManager.Grab(bufferIndex, true);
            }

            if (_isInspectMode) RunInspect();
        }

        //===== [그룹 7] 디스플레이 및 화면 갱신 =====

        private void DisplayGrabImage(int bufferIndex)
            => MainForm.GetDockForm<CameraForm>()?.UpdateDisplay();

        public void UpdateDisplay(Bitmap bitmap)
            => MainForm.GetDockForm<CameraForm>()?.UpdateDisplay(bitmap);

        public void SetPreviewImage(eImageChannel channel)
        {
            if (_previewImage == null) return;
            _previewImage.SetImage(BitmapConverter.ToMat(ImageSpace.GetBitmap(0, channel)));
            SetImageChannel(channel);
        }

        public void SetImageChannel(eImageChannel channel)
            => MainForm.GetDockForm<CameraForm>()?.SetImageChannel(channel);

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

        public void RedrawMainView() => MainForm.GetDockForm<CameraForm>()?.UpdateImageViewer();
        public void ResetDisplay() => MainForm.GetDockForm<CameraForm>()?.ResetDisplay();

        //===== [그룹 8] 모델 데이터 관리 =====

        public bool LoadModel(string filePath)
        {
            SLogger.Write($"모델 로딩:{filePath}");
            _model = _model.Load(filePath);
            if (_model == null) { SLogger.Write($"모델 로딩 실패:{filePath}"); return false; }

            if (File.Exists(_model.InspectImagePath))
                SetImageBuffer(_model.InspectImagePath);

            UpdateDiagramEntity();
            _regKey.SetValue("LastestModelPath", filePath);
            return true;
        }

        public void SaveModel(string filePath)
        {
            SLogger.Write($"모델 저장:{filePath}");
            if (string.IsNullOrEmpty(filePath)) CurModel.Save();
            else CurModel.SaveAs(filePath);
        }

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

        public void CycleInspect(bool isCycle)
        {
            if (InspWorker.IsRunning) return;

            if (!UseCamera)
            {
                string inspImagePath = CurModel.InspectImagePath;
                if (inspImagePath == "") return;

                string inspImageDir = Path.GetDirectoryName(inspImagePath);
                if (!Directory.Exists(inspImageDir)) return;

                // 이미지 목록이 없으면 로드
                if (!_imageLoader.IsLoadedImages())
                    _imageLoader.LoadImages(inspImageDir);

                // ✅ 이미지 전부 소진된 경우에만 리셋 → 다시 처음부터
                // 중간에 버튼 누르면 이어서 진행 (소진 판단은 RemainingCount로)
                if (_imageLoader.RemainingCount == 0)
                    _imageLoader.Reset();
            }

            if (isCycle) _inspWorker.StartCycleInspectImage();
            else OneCycle();
        }

        //------- 검사 한 주기 -------
        // 이미지 소진 시 false 반환 → 사이클 루프 종료
        public bool OneCycle()
        {
            ResetDisplay();

            bool grabSuccess = UseCamera ? Grab(0) : VirtualGrab();
            if (!grabSuccess)
            {
                SLogger.Write("모든 이미지 검사 완료 - 사이클 종료");
                StopCycle();
                return false;
            }

            RunInspect();
            Thread.Sleep(300);
            return true;
        }

        //------- 실제 검사 프로세스 -------
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
        }

        public void StopCycle()
        {
            _inspWorker?.Stop();
            VisionSequence.Inst.StopAutoRun();
            _isInspectMode = false;
            SetWorkingState(WorkingState.NONE);
        }

        public bool VirtualGrab()
        {
            if (_imageLoader == null) return false;
            string path = _imageLoader.GetNextImagePath();
            if (path == "") return false;
            SetImageBuffer(path);
            _imageSpace.Split(0);
            return true;
        }

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

        public void SetWorkingState(WorkingState ws) => MainForm.GetDockForm<CameraForm>()?.SetWorkingState(ws);
        public void SetExposure(long exposureTime) => _grabManager?.SetExposureTime(exposureTime);

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