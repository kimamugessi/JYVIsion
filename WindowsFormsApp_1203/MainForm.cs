using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JYVision.Core;
using JYVision.Setting;
using JYVision.Teach;
using JYVision.Util;
using JYVision4.Setting;
using WeifenLuo.WinFormsUI.Docking;
using MaterialSkin;
using MaterialSkin.Controls;

namespace JYVision
{
    //===== 프로그램 전체의 레이아웃과 생명주기를 관리하는 메인 폼 =====
    public partial class MainForm : MaterialForm
    {
        //===== [그룹 1] 필드 및 생성자 =====

        // 모든 서브 윈도우(도킹 창)를 담을 사령부 역할을 하는 패널
        private static DockPanel _dockPanel;

        public MainForm()
        {
            InitializeComponent();

            // 1. 시각적인 뼈대(테마, 색상)를 먼저 설정함
            SetupMaterialTheme();

            // 2. 도킹 패널을 폼 전체에 꽉 채우고 VS2015 테마를 입힘
            _dockPanel = new DockPanel
            {
                Dock = DockStyle.Fill,
                Theme = new VS2015BlueTheme() // 눈이 편안한 파란색 계열 테마 사용
            };
            Controls.Add(_dockPanel);

            // 3. 폼이 실제로 화면에 다 그려진 뒤에 '진짜 무거운 작업'을 시작하도록 예약
            this.Shown += MainForm_Shown;
        }

        //===== [그룹 2] UI 테마 및 레이아웃 설정 =====

        //------- MaterialSkin 테마 설정 -------
        private void SetupMaterialTheme()
        {
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT; // 기본 밝은 테마

            // 기업 이미지에 맞춘 컬러 스킴 (오렌지 포인트 + 짙은 남색 베이스)
            materialSkinManager.ColorScheme = new ColorScheme(
                Color.FromArgb(241, 90, 40),    // Primary: 로고 색상인 오렌지
                Color.FromArgb(0, 20, 40),      // Dark Primary: 상단 바 등 베이스 남색
                Color.FromArgb(130, 145, 162),  // Light Primary: 보조적인 그레이 블루
                Color.FromArgb(241, 90, 40),    // Accent: 강조용 오렌지
                TextShade.WHITE                 // 제목 글자색 (흰색)
            );

            // MaterialForm 특유의 헤더 높이를 고려한 패딩값 보정
            this.Padding = new Padding(3, 64, 3, 3);
        }

        //------- 도킹 윈도우 배치 (서브 창들 로드) -------
        private void LoadDockingWindows()
        {
            // 사용자가 마음대로 창을 떼어내지 못하도록 도킹 고정
            _dockPanel.AllowEndUserDocking = false;

            // 1. 메인 카메라 화면 (가장 넓은 영역)
            var cameraForm = new CameraForm();
            cameraForm.Show(_dockPanel, DockState.Document);

            // 2. 하단 결과창 (카메라 화면 아래 30% 비중)
            var resultForm = new ResultForm();
            resultForm.Show(cameraForm.Pane, DockAlignment.Bottom, 0.3);

            // 3. 우측 속성창 (검사 파라미터 제어)
            var propForm = new PropertiesForm();
            propForm.Show(_dockPanel, DockState.DockRight);

            // 4. 모델 트리 구조창 (우측 하단 30% 비중)
            var modelTreeWindow = new ModelTreeForm();
            modelTreeWindow.Show(resultForm.Pane, DockAlignment.Right, 0.27);

            // 5. 운전 제어창 (모델 트리와 같은 영역에 탭으로 묶음)
            var runWindow = new RunForm();
            runWindow.Show(modelTreeWindow.Pane, null);

            // 6. 로그 기록창 (속성창 아래 30% 비중)
            var logForm = new LogForm();
            logForm.Show(propForm.Pane, DockAlignment.Bottom, 0.3);

            var trendForm = new JYVision.Inspect.TrendChartForm();
            trendForm.Show(_dockPanel, DockState.Float);
            trendForm.FloatPane.FloatWindow.Size = new Size(800, 500);
        }

        //===== [그룹 3] 시스템 라이프사이클 관리 =====

        //------- 비동기 초기화 및 화면 표시 완료 시점 -------
        private async void MainForm_Shown(object sender, EventArgs e)
        {
            // 폼이 뜬 직후에 도킹 창들을 먼저 배치함
            LoadDockingWindows();

            // 백그라운드 스레드에서 무거운 초기화 작업 수행 (UI 프리징 방지)
            // 카메라 연결, SDK 로드 등 시간이 걸리는 작업은 여기서 처리함
            await Task.Run(() =>
            {
                Global.Inst.Initialize();
            });

            // 설정값 복구 및 마무리
            LoadSetting();

            // 모든 로딩이 끝났으므로 커서를 일반 모드로 복구
            this.Cursor = Cursors.Default;
        }

        //------- 프로그램 종료 시 자원 해제 -------
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 카메라 연결 해제 및 메모리 정리 (Dispose)
            Global.Inst.Dispose();
        }

        //===== [그룹 4] 모델 및 파일 관리 기능 =====

        //------- 새 모델 생성 -------
        private void modelNewMenuItem_Click(object sender, EventArgs e)
        {
            NewModel newModel = new NewModel();
            if (newModel.ShowDialog() == DialogResult.OK)
            {
                Model curModel = Global.Inst.InspStage.CurModel;
                if (curModel != null) this.Text = GetMdoelTitle(curModel);
            }
        }

        //------- 기존 모델 불러오기 -------
        private void modelOpenMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = SettingXml.Inst.ModelDir;
                openFileDialog.Filter = "Model Files|*.xml;";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // 모델 데이터를 불러온 후 성공하면 타이틀 바 제목 갱신
                    if (Global.Inst.InspStage.LoadModel(openFileDialog.FileName))
                    {
                        Model curModel = Global.Inst.InspStage.CurModel;
                        if (curModel != null) this.Text = GetMdoelTitle(curModel);
                    }
                }
            }
        }

        //------- 모델 저장 (현재 경로에 덮어쓰기) -------
        private void modelSaveMenuItem_Click(object sender, EventArgs e)
        {
            Global.Inst.InspStage.SaveModel("");
        }

        //------- 모델 다른 이름으로 저장 -------
        private void modelSaveAsMenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.InitialDirectory = SettingXml.Inst.ModelDir;
                saveFileDialog.Filter = "Model Files|*.xml;";
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    Global.Inst.InspStage.SaveModel(saveFileDialog.FileName);
                }
            }
        }

        //------- 이미지 파일 열기 (티칭/시뮬레이션용) -------
        private void imageOpenToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            CameraForm cameraForm = GetDockForm<CameraForm>();
            if (cameraForm == null) return;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "이미지 파일 선택";
                openFileDialog.Filter = "Image Files|*.bmp;*.jpg;*.jpeg;*.png;*.gif";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    // 선택한 이미지를 버퍼에 올리고 모델 정보에 경로 기록
                    Global.Inst.InspStage.SetImageBuffer(filePath);
                    Global.Inst.InspStage.CurModel.InspectImagePath = filePath;
                }
            }
        }

        //===== [그룹 5] 유틸리티 및 설정 메뉴 =====

        //------- 도킹된 폼 인스턴스 찾기 (Generic) -------
        public static T GetDockForm<T>() where T : DockContent
        {
            // 현재 패널에 도킹되어 있는 모든 폼 중 타입(T)이 일치하는 첫 번째 창을 반환
            return _dockPanel.Contents.OfType<T>().FirstOrDefault();
        }

        //------- 타이틀 바 텍스트 생성 -------
        private string GetMdoelTitle(Model curModel)
        {
            if (curModel is null) return "";
            return $"{Define.PROGRAM_NAME} - MODEL : {curModel.ModelName}";
        }

        //------- 전체 환경 설정창 열기 -------
        private void SetupMenuItem_Click(object sender, EventArgs e)
        {
            SLogger.Write($"환경설정창 열기");
            SetupForm setupForm = new SetupForm();
            setupForm.ShowDialog();
        }

        //------- 사이클 모드 설정 변경 -------
        private void cycleModeMenuItem_Click(object sender, EventArgs e)
        {
            // 메뉴의 체크 상태를 XML 설정값에 동기화
            SettingXml.Inst.CycleMode = cycleModeMenuItem.Checked;
        }

        //------- 초기 설정값 로드 -------
        private void LoadSetting()
        {
            if (SettingXml.Inst != null)
                cycleModeMenuItem.Checked = SettingXml.Inst.CycleMode;
        }
    }
}