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
        //===== [그룹 1] 초기화 =====

        public xylophone()
        {
            InitializeComponent();

            // 체크박스 클릭 시 별도의 버튼 클릭 없이 화면이 즉시 갱신되도록 이벤트 연결
            cbKeybord.CheckedChanged += OnRoiFilterChanged;
            cbBolt.CheckedChanged += OnRoiFilterChanged;
            cbMark.CheckedChanged += OnRoiFilterChanged;
        }

        //===== [그룹 2] 이벤트 핸들러 =====

        //------- ROI 생성 및 검출 버튼 클릭 -------
        private void btnBoltROI_Click(object sender, EventArgs e)
        {
            // 1. 초기 상태에서는 비활성화되어 있을 수 있는 옵션들을 모두 활성화
            cbKeybord.Enabled = true;
            cbBolt.Enabled = true;
            cbMark.Enabled = true;

            var stage = Global.Inst.InspStage;

            // 2. 건반 매칭 알고리즘을 먼저 실행하여 검사 대상들의 좌표(_lastMatchedKeyRects)를 확보
            stage.RunKeyMatch();

            // 3. 처음 실행 시에는 모든 ROI가 보이도록 체크박스를 모두 켬
            cbKeybord.Checked = true;
            cbBolt.Checked = true;
            cbMark.Checked = true;

            // 4. 확보된 좌표와 현재 체크박스 상태를 조합하여 화면 갱신
            RefreshDisplay();
        }

        //------- 체크박스 상태 변경 시 핸들러 -------
        private void OnRoiFilterChanged(object sender, EventArgs e)
        {
            // 필터링 옵션(건반/볼트/각인)이 바뀔 때마다 즉시 화면을 다시 그림
            RefreshDisplay();
        }

        //===== [그룹 3] 화면 갱신 유틸리티 =====

        //------- 현재 UI 설정 기준으로 ROI 표시 갱신 -------
        private void RefreshDisplay()
        {
            // InspStage에 현재 체크박스의 참/거짓 값을 전달하여 필터링된 결과만 화면에 표시하도록 요청
            Global.Inst.InspStage.RunDisplayWithOptions(
                showKeyboard: cbKeybord.Checked,
                showBolt: cbBolt.Checked,
                showMark: cbMark.Checked
            );
        }
    }
}