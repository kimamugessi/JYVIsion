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

namespace WindowsFormsApp_1203
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

            _dockPanel.Theme=new VS2015BlueTheme();

            LoadDockingWindows();
        }

        private void LoadDockingWindows()
        {
            //아래의 각 폼의 부모를 DockContent로 설정
            var cameraForm = new CameraForm();
            cameraForm.Show(_dockPanel,DockState.Document); //첫번째 Form은  Document로 기본적으로 설정 해야함

            var resultForm = new ResultForm();
            resultForm.Show(cameraForm.Pane,DockAlignment.Bottom,0.3);  // _____.Show(기준, 위치, 크기); 

            var propForm = new PropertiesForm();
            propForm.Show(_dockPanel, DockState.DockRight); //propForm, stat 위치값 동일 -> 겹쳐진 형태

            var stat=new StatisticForm();
            stat.Show(_dockPanel, DockState.DockRight);

            var logForm=new LogForm();
            logForm.Show(propForm.Pane, DockAlignment.Bottom, 0.5);
        }
    }
}
