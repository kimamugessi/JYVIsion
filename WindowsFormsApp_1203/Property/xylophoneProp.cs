using JYVision.Core;
using JYVision.Util;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JYVision.Property
{
    //===== 실로폰 검사 ROI 필터링 제어 UI 클래스 =====
    public partial class xylophone : UserControl
    {
        //===== [그룹 1] static 상태값 (InspStage에서 직접 읽음) =====
        public static bool ShowKeyboard { get; private set; } = true;
        public static bool ShowBolt { get; private set; } = true;
        public static bool ShowMark { get; private set; } = true;

        //===== [그룹 2] 초기화 =====
        public xylophone()
        {
            InitializeComponent();
            cbKeybord.CheckedChanged += OnRoiFilterChanged;
            cbBolt.CheckedChanged += OnRoiFilterChanged;
            cbMark.CheckedChanged += OnRoiFilterChanged;
        }

        //===== [그룹 3] 이벤트 핸들러 =====

        //------- ROI 생성 및 검출 버튼 클릭 -------
        private void btnBoltROI_Click(object sender, EventArgs e)
        {
            cbKeybord.Enabled = true;
            cbBolt.Enabled = true;
            cbMark.Enabled = true;

            Global.Inst.InspStage.RunKeyMatch();

            cbKeybord.Checked = true;
            cbBolt.Checked = true;
            cbMark.Checked = true;

            RefreshDisplay();
        }

        //------- 체크박스 상태 변경 시 static 동기화 + 즉시 화면 갱신 -------
        private void OnRoiFilterChanged(object sender, EventArgs e)
        {
            ShowKeyboard = cbKeybord.Checked;
            ShowBolt = cbBolt.Checked;
            ShowMark = cbMark.Checked;

            RefreshDisplay();
        }

        //===== [그룹 4] 화면 갱신 유틸리티 =====

        private void RefreshDisplay()
        {
            Global.Inst.InspStage.RunDisplayWithOptions(
                showKeyboard: cbKeybord.Checked,
                showBolt: cbBolt.Checked,
                showMark: cbMark.Checked);
        }
    }
}