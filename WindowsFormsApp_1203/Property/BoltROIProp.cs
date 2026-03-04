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
    public partial class BoltROIProp : UserControl
    {
        public BoltROIProp()
        {
            InitializeComponent();
        }

        // 건반 매칭 버튼 - 건반 위치 감지 (세로 길이 자동 보정)
        private void btnKeyMatch_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            stage.RunKeyMatch();
        }

        // Bolt ROI 버튼 - 건반 기준 볼트 확인 + 각인 확인
        private void btnBoltROI_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            stage.RunOnlyBoltMatch();
        }

        // Check Mark 버튼 - 각인 대조 검사
        private void btnCheckMark_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            stage.RunCheckMarkContrast();
        }
    }
}