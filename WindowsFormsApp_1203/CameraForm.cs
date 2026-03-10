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
    //===== 메인 영상 출력 및 ROI 편집 제어 폼 =====
    public partial class CameraForm : DockContent
    {
        //===== [그룹 1] 필드 및 초기화 =====

        private eImageChannel _currentImageChannel = eImageChannel.Color; // 현재 화면에 표시 중인 채널

        //------- 폼 생성자 및 이벤트 연결 -------
        public CameraForm()
        {
            InitializeComponent();

            this.FormClosed += CameraForm_FormClosed;

            // 커스텀 컨트롤(ImageViewer, Toolbar) 이벤트 핸들러 등록
            imageViewer.DiagramEntityEvent += ImageViewer_DiagramEntityEvent;
            mainViewToolbar.ButtonChanged += Toolbar_ButtonChanged;
        }

        //------- 폼 종료 시 등록된 이벤트 해제 및 리소스 정리 -------
        private void CameraForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            mainViewToolbar.ButtonChanged -= Toolbar_ButtonChanged;
            imageViewer.DiagramEntityEvent -= ImageViewer_DiagramEntityEvent;
            this.FormClosed -= CameraForm_FormClosed;
        }

        //===== [그룹 2] 이미지 뷰어 이벤트 핸들러 =====

        //------- 뷰어 내 ROI 도형 조작(추가/이동/삭제/복사) 이벤트 처리 -------
        private void ImageViewer_DiagramEntityEvent(object sender, DiagramEntityEventArgs e)
        {
            SLogger.Write($"ImageViewer Action {e.ActionType.ToString()}");
            switch (e.ActionType)
            {
                case EntityActionType.Select: // ROI 선택
                    Global.Inst.InspStage.SelectInspWindow(e.InspWindow);
                    imageViewer.Focus();
                    break;
                case EntityActionType.Inspect: // 단일 ROI 즉시 검사
                    UpdateDiagramEntity();
                    Global.Inst.InspStage.TryInspection(e.InspWindow);
                    break;
                case EntityActionType.Add: // 새 ROI 추가
                    Global.Inst.InspStage.AddInspWindow(e.WindowType, e.Rect);
                    break;
                case EntityActionType.Copy: // ROI 복제
                    Global.Inst.InspStage.AddInspWindow(e.InspWindow, e.OffsetMove);
                    break;
                case EntityActionType.Move: // ROI 이동
                    Global.Inst.InspStage.MoveInspWindow(e.InspWindow, e.OffsetMove);
                    break;
                case EntityActionType.Resize: // ROI 크기 변경
                    Global.Inst.InspStage.ModifyInspWindow(e.InspWindow, e.Rect);
                    break;
                case EntityActionType.Delete: // ROI 단일 삭제
                    Global.Inst.InspStage.DelInspWindow(e.InspWindow);
                    break;
                case EntityActionType.DeleteList: // ROI 다중 삭제
                    Global.Inst.InspStage.DelInspWindow(e.InspWindowList);
                    break;
            }
        }

        //===== [그룹 3] 이미지 로드 및 화면 갱신 =====

        //------- 외부 이미지 파일을 불러와 뷰어에 표시 -------
        public void LoadImage(string filePath)
        {
            if (!File.Exists(filePath)) return;

            // 파일 잠김 방지를 위해 스트림으로 읽어서 Bitmap 생성
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                using (var tempBmp = Image.FromStream(stream))
                {
                    imageViewer.LoadBitmap(new Bitmap(tempBmp));
                }
            }
        }

        //------- 현재 선택된 채널의 원본 이미지를 Mat 형식으로 반환 -------
        public Mat GetDisplayImage()
        {
            return Global.Inst.InspStage.ImageSpace.GetMat(0, _currentImageChannel);
        }

        //------- 이미지 버퍼 데이터를 뷰어 비트맵으로 업데이트 -------
        public void UpdateDisplay(Bitmap bitmap = null)
        {
            if (bitmap == null)
            {
                // 인자가 없으면 현재 설정된 채널의 버퍼를 가져옴
                bitmap = Global.Inst.InspStage.GetBitmap(0, _currentImageChannel);
                if (bitmap == null) return;
            }

            if (imageViewer != null)
                imageViewer.LoadBitmap(bitmap);
        }

        //------- 검사 파라미터 변경 사항을 뷰어에 반영 및 강제 다시 그리기 -------
        public void UpdateImageViewer()
        {
            imageViewer.UpdateInspParam();
            imageViewer.Invalidate();
        }

        //------- 폼 크기 변경 시 이미지 뷰어 레이아웃 재조정 -------
        private void CameraForm_Resize(object sender, EventArgs e)
        {
            int margin = 0;
            imageViewer.Width = this.Width - mainViewToolbar.Width - margin * 2;
            imageViewer.Height = this.Height - margin * 2;
            imageViewer.Location = new System.Drawing.Point(margin, margin);
        }

        //===== [그룹 4] ROI 및 다이어그램 데이터 관리 =====

        //------- 현재 모델의 ROI 정보(InspWindowList)를 뷰어 엔티티로 동기화 -------
        public void UpdateDiagramEntity()
        {
            imageViewer.ResetEntity();

            Model model = Global.Inst.InspStage.CurModel;
            List<DiagramEntity> diagramEntityList = new List<DiagramEntity>();

            foreach (InspWindow window in model.InspWindowList)
            {
                if (window == null) continue;

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

        //------- 특정 검사 영역(Window)을 뷰어에서 선택 상태로 강조 -------
        public void SelectDiagramEntity(InspWindow window)
        {
            imageViewer.SelectDiagramEntity(window);
        }

        //------- 뷰어에 표시된 모든 그래픽 엔티티(ROI, 결과 사각형 등) 초기화 -------
        public void ResetDisplay()
        {
            imageViewer.ResetEntity();
        }

        //------- 검사 결과 사각형들을 뷰어 레이어에 추가 -------
        public void AddRect(List<DrawInspectInfo> rectInfos)
        {
            imageViewer.AddRect(rectInfos);
        }

        //------- 특정 타입의 새로운 ROI 생성 모드 진입 -------
        public void AddRoi(InspWindowType inspWindowType)
        {
            imageViewer.NewRoi(inspWindowType);
        }

        //===== [그룹 5] 툴바 및 상태 제어 =====

        //------- 뷰어 상단에 현재 장비의 작업 상태(LIVE, INSPECT 등) 표시 -------
        public void SetWorkingState(WorkingState workingState)
        {
            string state = "";
            switch (workingState)
            {
                case WorkingState.INSPECT: state = "INSPECT"; break;
                case WorkingState.LIVE: state = "LIVE"; break;
                case WorkingState.ALARM: state = "ALARM"; break;
            }

            imageViewer.WorkingState = state;
            imageViewer.Invalidate();
        }

        //------- 뷰어 우측 툴바 버튼 클릭 이벤트 핸들러 -------
        private void Toolbar_ButtonChanged(object sender, ToolbarEventArgs e)
        {
            switch (e.Button)
            {
                case ToolbarButton.ShowROI: // ROI 보이기/숨기기
                    if (e.IsChecked) UpdateDiagramEntity();
                    else imageViewer.ResetEntity();
                    break;
                case ToolbarButton.ChannelColor: // 각 채널별 화면 전환
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

        //------- 외부(InspStage 등)에서 강제로 이미지 채널을 변경할 때 사용 -------
        public void SetImageChannel(eImageChannel channel)
        {
            mainViewToolbar.SetSelectButton(channel);
            _currentImageChannel = channel;
            UpdateDisplay();
        }

        //===== [그룹 6] 영상 처리 유틸리티 =====

        //------- Canny 알고리즘을 활용하여 각인 등 엣지가 강조된 영상 획득 -------
        public Mat GetEdgeEnhancedImage(eImageChannel channel)
        {
            Mat src = Global.Inst.InspStage.ImageSpace.GetMat(0, channel);
            if (src.Empty()) return null;

            Mat processed = new Mat();

            // Grayscale 변환
            if (src.Channels() > 1)
                Cv2.CvtColor(src, processed, ColorConversionCodes.BGR2GRAY);
            else
                src.CopyTo(processed);

            // 노이즈 제거 (가우시안 블러)
            Cv2.GaussianBlur(processed, processed, new OpenCvSharp.Size(5, 5), 0);

            // Canny 엣지 검출 (각인 테두리 추출용 파라미터 30, 90)
            Cv2.Canny(processed, processed, 30, 90);

            return processed;
        }
    }
}