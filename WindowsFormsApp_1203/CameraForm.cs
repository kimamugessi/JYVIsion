using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System.IO;
using JYVision.Core;
using OpenCvSharp;
using JYVision.Algorithm;
using JYVision.Teach;
using JYVision.UIControl;
using JYVision.Util;

namespace JYVision
{
    //public partial class CameraForm: Form
    public partial class CameraForm : DockContent
    {
        eImageChannel _currentImageChannel = eImageChannel.Color;
        public CameraForm()
        {
            InitializeComponent();

            this.FormClosed += CameraForm_FormClosed;

            imageViewer.DiagramEntityEvent += ImageViewer_DiagramEntityEvent;

            mainViewToolbar.ButtonChanged += Toolbar_ButtonChanged;
        }

        private void ImageViewer_DiagramEntityEvent(object sender, DiagramEntityEventArgs e)
        {
            SLogger.Write($"ImageViewer Action {e.ActionType.ToString()}");
            switch (e.ActionType)
            {
                case EntityActionType.Select:
                    Global.Inst.InspStage.SelectInspWindow(e.InspWindow);
                    imageViewer.Focus();
                    break;
                case EntityActionType.Inspect:
                    UpdateDiagramEntity();
                    Global.Inst.InspStage.TryInspection(e.InspWindow);
                    break;
                case EntityActionType.Add:
                    Global.Inst.InspStage.AddInspWindow(e.WindowType, e.Rect);
                    break;
                case EntityActionType.Copy:
                    Global.Inst.InspStage.AddInspWindow(e.InspWindow, e.OffsetMove);
                    break;
                case EntityActionType.Move:
                    Global.Inst.InspStage.MoveInspWindow(e.InspWindow, e.OffsetMove);
                    break;
                case EntityActionType.Resize:
                    Global.Inst.InspStage.ModifyInspWindow(e.InspWindow, e.Rect);
                    break;
                case EntityActionType.Delete:
                    Global.Inst.InspStage.DelInspWindow(e.InspWindow);
                    break;
                case EntityActionType.DeleteList:
                    Global.Inst.InspStage.DelInspWindow(e.InspWindowList);
                    break;
            }
        }

        private void imageViewer_Load(object sender, EventArgs e)
        {

        }

        public void LoadImage(string filePath)  //이미지 경로받아 PictureBox에 이미지를 로드
        {
            if (File.Exists(filePath) == false) return; //파일이 없다면 리턴

            Image bitmap = Image.FromFile(filePath);
            imageViewer.LoadBitmap((Bitmap)bitmap);
        }

        public Mat GetDisplayImage()
        {
            return Global.Inst.InspStage.ImageSpace.GetMat(0,_currentImageChannel);
        }

        private void CameraForm_Resize(object sender, EventArgs e)
        {
            int margin = 0;
            imageViewer.Width = this.Width - mainViewToolbar.Width-margin * 2;
            imageViewer.Height = this.Height - margin * 2;

            imageViewer.Location = new System.Drawing.Point(margin, margin);
        }

        public void UpdateDisplay(Bitmap bitmap = null)
        {
            if (bitmap == null)
            {
                //#6_INSP_STAGE#3 업데이트시 bitmap이 없다면 InspSpace에서 가져온다
                bitmap = Global.Inst.InspStage.GetBitmap(0,_currentImageChannel);
                if (bitmap == null)
                    return;
            }

            if (imageViewer != null)
                imageViewer.LoadBitmap(bitmap);
        }

        public void UpdateImageViewer()
        {
            imageViewer.UpdateInspParam();
            imageViewer.Invalidate();
        }

        public void UpdateDiagramEntity()
        {
            imageViewer.ResetEntity();

            Model model = Global.Inst.InspStage.CurModel;
            List<DiagramEntity> diagramEntityList = new List<DiagramEntity>();

            foreach (InspWindow window in model.InspWindowList)
            {
                if (window == null)
                    continue;

                DiagramEntity entity = new DiagramEntity()
                {
                    LinkedWindow = window,
                    EntityROI = new Rectangle(
                        window.WindowArea.X, window.WindowArea.Y,
                            window.WindowArea.Width, window.WindowArea.Height),
                    EntityColor = imageViewer.GetWindowColor(window.InspWindowType),
                    IsHold = window.IsTeach
                };
                diagramEntityList.Add(entity);
            }

            imageViewer.SetDiagramEntityList(diagramEntityList);
        }

        public void SelectDiagramEntity(InspWindow window)
        {
            imageViewer.SelectDiagramEntity(window);
        }

        //#8_INSPECT_BINARY#18 imageViewer에 검사 결과 정보를 연결해주기 위한 함수
        public void ResetDisplay()
        {
            imageViewer.ResetEntity();
        }

        //FIXME 검사 결과를 그래픽으로 출력하기 위한 정보를 받는 함수
        public void AddRect(List<DrawInspectInfo> rectInfos)
        {
            imageViewer.AddRect(rectInfos);
        }

        //#10_INSPWINDOW#24 새로운 ROI를 추가하는 함수
        public void AddRoi(InspWindowType inspWindowType)
        {
            imageViewer.NewRoi(inspWindowType);
        }

        //#13_INSP_RESULT#6 검사 양불판정 갯수 설정 함수
        public void SetInspResultCount(int totalArea, int okCnt, int ngCnt)
        {
            imageViewer.SetInspResultCount(new InspectResultCount(totalArea, okCnt, ngCnt));
        }

        public void SetWorkingState(WorkingState workingState)
        {
            string state = "";
            switch (workingState)
            {
                case WorkingState.INSPECT:
                    state = "INSPECT";
                    break;

                case WorkingState.LIVE:
                    state = "LIVE";
                    break;

                case WorkingState.ALARM:
                    state = "ALARM";
                    break;
            }

            imageViewer.WorkingState = state;
            imageViewer.Invalidate();
        }

        private void Toolbar_ButtonChanged(object sender, ToolbarEventArgs e) {
            switch (e.Button)
            {
                case ToolbarButton.ShowROI:
                    if(e.IsChecked)
                        UpdateDiagramEntity();
                    else 
                        imageViewer.ResetEntity();
                    break;
                case ToolbarButton.ChannelColor:
                    _currentImageChannel = eImageChannel.Color;
                    UpdateDisplay();
                    break;
                case ToolbarButton.ChannelGray:
                    _currentImageChannel = eImageChannel.Gray;
                    UpdateDisplay();
                    break;
                case ToolbarButton.ChannelRed:
                    _currentImageChannel = eImageChannel.Red;
                    UpdateDisplay();
                    break;
                case ToolbarButton.ChannelGreen:
                    _currentImageChannel = eImageChannel.Green;
                    UpdateDisplay();
                    break;
                case ToolbarButton.ChannelBlue:
                    _currentImageChannel = eImageChannel.Blue;
                    UpdateDisplay();
                    break;
            }
        }

        public void SetImageChannel(eImageChannel channel)
        {
            mainViewToolbar.SetSelectButton(channel);
            UpdateDisplay();
        }

        private void CameraForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            mainViewToolbar.ButtonChanged -= Toolbar_ButtonChanged;

            imageViewer.DiagramEntityEvent -= ImageViewer_DiagramEntityEvent;

            this.FormClosed -= CameraForm_FormClosed;
        }
        // 기존 GetDisplayImage()를 활용하거나 새로운 처리 함수를 만듭니다.
        public Mat GetEdgeEnhancedImage(eImageChannel channel)
        {
            // 1. 원본 이미지 가져오기 (OpenCvSharp의 Mat 형식)
            Mat src = Global.Inst.InspStage.ImageSpace.GetMat(0, channel);
            if (src.Empty()) return null;

            Mat processed = new Mat();

            // 2. Grayscale 변환 (이미 Gray면 스킵)
            if (src.Channels() > 1)
                Cv2.CvtColor(src, processed, ColorConversionCodes.BGR2GRAY);
            else
                src.CopyTo(processed);

            // 3. 가우시안 블러 (노이즈 제거 - 각인이 흐릿할수록 이 단계가 중요!)
            // 각인이 아주 작다면 Size(3,3), 보통은 (5,5)를 씁니다.
            Cv2.GaussianBlur(processed, processed, new OpenCvSharp.Size(5, 5), 0);

            // 4. Canny 엣지 검출 (각인의 테두리만 추출)
            // Threshold1, 2 값을 조절하며 각인이 가장 잘 보이는 수치를 찾으세요.
            Cv2.Canny(processed, processed, 30, 90);

            return processed;
        }
    }
}
