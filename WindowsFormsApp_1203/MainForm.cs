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
//using WeifenLuo.WinFormsUI.ThemeVS2015;

namespace JYVision
{
    public partial class MainForm : Form
    {
        private static DockPanel _dockPanel;

        public MainForm()
        {
            InitializeComponent();

            _dockPanel = new DockPanel()
            {
                Dock = DockStyle.Fill
            };
            //_dockPanel = new DockPanel();
            //_dockPanel.Dock = DockStyle.Fill; 상단 코드와 동일
            Controls.Add(_dockPanel);

            _dockPanel.Theme = new VS2015BlueTheme();

            LoadDockingWindows();
        }

        private void LoadDockingWindows()
        {
            _dockPanel.AllowEndUserDocking = false;
            //아래의 각 폼의 부모를 DockContent로 설정
            var cameraForm = new CameraForm();
            cameraForm.Show(_dockPanel, DockState.Document); //첫번째 Form은  Document로 기본적으로 설정 해야함

            var resultForm = new ResultForm();
            resultForm.Show(cameraForm.Pane, DockAlignment.Bottom, 0.3);  // _____.Show(기준, 위치, 크기); 

            var propForm = new PropertiesForm();
            propForm.Show(_dockPanel, DockState.DockRight); //propForm, stat 위치값 동일 -> 겹쳐진 형태

            var stat = new StatisticForm();
            stat.Show(_dockPanel, DockState.DockRight);

            var logForm = new LogForm();
            logForm.Show(propForm.Pane, DockAlignment.Bottom, 0.5);


        }
        //시범으로 작성
        public static T GetDockForm<T>() where T : DockContent
        {
            var findForm = _dockPanel.Contents.OfType<T>().FirstOrDefault();
            return findForm;
        }

        private void imageOpenToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            CameraForm cameraForm = GetDockForm<CameraForm>();
            if (cameraForm is null) return;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "이미지 파일 선택";
                openFileDialog.Filter = "Image Files|*.bmp;*.jpg;*.jpeg;*.png;*.gif";
                openFileDialog.Multiselect = false;
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    cameraForm.LoadImage(filePath);
                }
            }
        }
    }
}
