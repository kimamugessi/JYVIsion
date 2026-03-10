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

        //------- 컨트롤 초기화 및 체크박스 이벤트 연결 -------
        public xylophone()
        {
            InitializeComponent();
            cbKeybord.CheckedChanged += OnRoiFilterChanged;
            cbBolt.CheckedChanged += OnRoiFilterChanged;
            cbMark.CheckedChanged += OnRoiFilterChanged;
        }

        //===== [그룹 3] 이벤트 핸들러 =====

        //------- 볼트 ROI 자동 생성 및 검출 실행 -------
        private void btnBoltROI_Click(object sender, EventArgs e)
        {
            // 필터링 제어 활성화
            cbKeybord.Enabled = true;
            cbBolt.Enabled = true;
            cbMark.Enabled = true;

            // 실제 건반 매칭 알고리즘 실행
            Global.Inst.InspStage.RunKeyMatch();

            // 초기 검출 시 모든 레이어 표시
            cbKeybord.Checked = true;
            cbBolt.Checked = true;
            cbMark.Checked = true;

            RefreshDisplay();
        }

        //------- 체크박스 상태 변경 시 static 플래그 동기화 및 화면 갱신 -------
        private void OnRoiFilterChanged(object sender, EventArgs e)
        {
            ShowKeyboard = cbKeybord.Checked;
            ShowBolt = cbBolt.Checked;
            ShowMark = cbMark.Checked;

            RefreshDisplay();
        }

        //===== [그룹 4] 화면 갱신 유틸리티 =====

        //------- 현재 필터링 설정에 맞춰 메인 화면 다시 그리기 -------
        private void RefreshDisplay()
        {
            // InspStage를 통해 화면에 그릴 항목들 필터링 처리
            Global.Inst.InspStage.RunDisplayWithOptions(
                showKeyboard: cbKeybord.Checked,
                showBolt: cbBolt.Checked,
                showMark: cbMark.Checked);
        }
    }
}