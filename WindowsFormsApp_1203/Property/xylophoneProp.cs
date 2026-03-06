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
    public partial class xylophone : UserControl
    {
        public xylophone()
        {
            InitializeComponent();

            // 체크박스 변경 시 화면 즉시 갱신
            cbKeybord.CheckedChanged += OnRoiFilterChanged;
            cbBolt.CheckedChanged += OnRoiFilterChanged;
            cbMark.CheckedChanged += OnRoiFilterChanged;
        }

        // 검사 버튼: 매칭 먼저 실행 후 옵션에 따라 표시
        private void btnBoltROI_Click(object sender, EventArgs e)
        {
            cbKeybord.Enabled = true; cbBolt.Enabled = true; cbMark.Enabled = true;
            var stage = Global.Inst.InspStage;

            // 1) 건반 매칭으로 _lastMatchedKeyRects 확보
            stage.RunKeyMatch();

            cbKeybord.Checked = true; cbBolt.Checked = true; cbMark.Checked = true;
            // 2) 현재 체크 상태로 ROI 표시
            RefreshDisplay();
        }

        // 체크박스 상태가 바뀔 때마다 호출
        private void OnRoiFilterChanged(object sender, EventArgs e)
        {
            RefreshDisplay();
        }

        // 공통: 현재 체크박스 상태 기준으로 화면 갱신
        private void RefreshDisplay()
        {
            Global.Inst.InspStage.RunDisplayWithOptions(
                showKeyboard: cbKeybord.Checked,
                showBolt: cbBolt.Checked,
                showMark: cbMark.Checked
            );
        }
    }
}