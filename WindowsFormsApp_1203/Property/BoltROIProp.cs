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

        // Bolt ROI 버튼 - 볼트 감지 + 누락 탐지
        private void btnBoltROI_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            stage.RunOnlyBoltMatch();
        }

        // ROI Set 버튼 - ✅ Teaching: 현재 이미지의 볼트 위치를 기준으로 저장
        private void button1_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            stage.SaveBoltReferenceFromCurrentImage();
        }

        // Check Mark 버튼 - 각인 대조 검사
        private void btnCheckMark_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            stage.RunCheckMarkContrast();
        }
    }
}