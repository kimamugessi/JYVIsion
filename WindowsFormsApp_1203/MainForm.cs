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
    public partial class MainForm : MaterialForm
    {
        private static DockPanel _dockPanel;

        public MainForm()
        {
            InitializeComponent();

            // 1. MaterialSkin 테마 및 컬러 설정 (UI 뼈대)
            SetupMaterialTheme();

            // 2. DockPanel 초기화 (Fill 설정)
            _dockPanel = new DockPanel
            {
                Dock = DockStyle.Fill,
                Theme = new VS2015BlueTheme() // Material Light 테마와 잘 어울리는 블루 테마
            };
            Controls.Add(_dockPanel);

            // 3. 프리징 방지: 폼이 화면에 나타난 후 무거운 작업을 시작하도록 이벤트 연결
            this.Shown += MainForm_Shown;
        }

        private void SetupMaterialTheme()
        {
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;

            // 이미지 가이드 기반 컬러 스킴 설정
            materialSkinManager.ColorScheme = new ColorScheme(
                Color.FromArgb(241, 90, 40),    // Primary: Royal Blue
                Color.FromArgb(0, 20, 40),    // Dark Primary: 짙은 남색
                Color.FromArgb(130, 145, 162), // Light Primary: Blue Grey
                Color.FromArgb(241, 90, 40),   // Accent: Sunrise Orange (포인트 컬러)
                TextShade.WHITE               // 타이틀바 글자색 (흰색)
            );

            // 커서 및 레이아웃 안정화
            this.Padding = new Padding(3, 64, 3, 3);
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            // 이 시점부터는 화면이 그려진 상태이므로 모래시계가 뜨더라도 윈도우가 굳지 않습니다.

            // 1. 도킹 윈도우 배치 (UI 구성)
            LoadDockingWindows();

            // 2. 무거운 백그라운드 초기화 (비동기 처리로 프리징 해결)
            await Task.Run(() =>
            {
                Global.Inst.Initialize();
            });

            // 3. 설정 로드 및 폰트 적용
            LoadSetting();

            // 4. 로딩 완료 후 커서 복구
            this.Cursor = Cursors.Default;
        }

        private void LoadDockingWindows()
        {
            _dockPanel.AllowEndUserDocking = false;

            var cameraForm = new CameraForm();
            cameraForm.Show(_dockPanel, DockState.Document);

            // 하단 영역 높이를 30%
            var resultForm = new ResultForm();
            resultForm.Show(cameraForm.Pane, DockAlignment.Bottom, 0.3);

            var propForm = new PropertiesForm();
            propForm.Show(_dockPanel, DockState.DockRight);

            // RunForm이 포함된 우측 하단 영역 너비를 40%
            var modelTreeWindow = new ModelTreeForm();
            modelTreeWindow.Show(resultForm.Pane, DockAlignment.Right, 0.3);

            var runWindow = new RunForm();
            runWindow.Show(modelTreeWindow.Pane, null);

            var logForm = new LogForm();
            logForm.Show(propForm.Pane, DockAlignment.Bottom, 0.3);
        }

        private void LoadSetting()
        {
            if (SettingXml.Inst != null)
                cycleModeMenuItem.Checked = SettingXml.Inst.CycleMode;
        }

        public static T GetDockForm<T>() where T : DockContent
        {
            return _dockPanel.Contents.OfType<T>().FirstOrDefault();
        }

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
                    Global.Inst.InspStage.SetImageBuffer(filePath);
                    Global.Inst.InspStage.CurModel.InspectImagePath = filePath;
                }
            }
        }

        private void SetupMenuItem_Click(object sender, EventArgs e)
        {
            SLogger.Write($"환경설정창 열기");
            SetupForm setupForm = new SetupForm();
            setupForm.ShowDialog();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Global.Inst.Dispose();
        }

        private string GetMdoelTitle(Model curModel)
        {
            if (curModel is null) return "";
            return $"{Define.PROGRAM_NAME} - MODEL : {curModel.ModelName}";
        }

        private void modelNewMenuItem_Click(object sender, EventArgs e)
        {
            NewModel newModel = new NewModel();
            if (newModel.ShowDialog() == DialogResult.OK)
            {
                Model curModel = Global.Inst.InspStage.CurModel;
                if (curModel != null) this.Text = GetMdoelTitle(curModel);
            }
        }

        private void modelOpenMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = SettingXml.Inst.ModelDir;
                openFileDialog.Filter = "Model Files|*.xml;";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (Global.Inst.InspStage.LoadModel(openFileDialog.FileName))
                    {
                        Model curModel = Global.Inst.InspStage.CurModel;
                        if (curModel != null) this.Text = GetMdoelTitle(curModel);
                    }
                }
            }
        }

        private void modelSaveMenuItem_Click(object sender, EventArgs e)
        {
            Global.Inst.InspStage.SaveModel("");
        }

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

        private void cycleModeMenuItem_Click(object sender, EventArgs e)
        {
            SettingXml.Inst.CycleMode = cycleModeMenuItem.Checked;
        }
    }
}