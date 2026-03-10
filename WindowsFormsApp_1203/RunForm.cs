using JYVision.Core;
using JYVision.Setting;
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
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision
{
    //===== 장비 운전 제어 및 실시간 영상 관리 폼 =====
    public partial class RunForm : DockContent
    {
        //===== [그룹 1] 초기화 =====

        //------- 폼 생성자 및 초기화 -------
        public RunForm()
        {
            InitializeComponent();
        }

        //===== [그룹 2] 카메라 및 그랩 제어 =====

        //------- 단일 프레임 그랩 실행 (One-Shot) -------
        private void btnGrab_Click(object sender, EventArgs e)
        {
            // 현재 해상도 체크 후 0번 버퍼에 1회 촬영
            Global.Inst.InspStage.CheckImageBuffer();
            Global.Inst.InspStage.Grab(0);
        }

        //------- 실시간 라이브 모드 토글 (On/Off) -------
        private void btnLive_Click(object sender, EventArgs e)
        {
            // 라이브 모드 상태 반전
            Global.Inst.InspStage.LiveMode = !Global.Inst.InspStage.LiveMode;

            if (Global.Inst.InspStage.LiveMode)
            {
                // 라이브 시작: 상태바 변경 및 촬영 루프 진입
                Global.Inst.InspStage.SetWorkingState(WorkingState.LIVE);
                Global.Inst.InspStage.CheckImageBuffer();
                Global.Inst.InspStage.Grab(0);
            }
            else
            {
                // 라이브 종료: 상태 초기화
                Global.Inst.InspStage.SetWorkingState(WorkingState.NONE);
            }
        }

        //===== [그룹 3] 검사 실행 및 중지 =====

        //------- 자동 운전 및 검사 시작 트리거 -------
        private void btnStart_Click(object sender, EventArgs e)
        {
            // 결과 저장용 고유 시리얼 번호 생성
            string serialID = $"{DateTime.Now:MM-dd HH:mm:ss}";
            Global.Inst.InspStage.InspectReady("LOT_NUMBER", serialID);

            // 카메라 설정 여부에 따라 가상 검사 또는 실제 카메라 시퀀스 구동
            if (SettingXml.Inst.CamType == Grab.CameraType.None)
            {
                // 가상 모드: 폴더 내 이미지 파일을 순차적으로 로드
                bool cycleMode = SettingXml.Inst.CycleMode;
                Global.Inst.InspStage.CycleInspect(cycleMode);
            }
            else
            {
                // 카메라 모드: PLC 연동 및 자동화 시퀀스 시작
                Global.Inst.InspStage.StartAutoRun();
            }
        }

        //------- 검사 시퀀스 및 자동 운전 즉시 중지 -------
        private void btnStop_Click(object sender, EventArgs e)
        {
            // 모든 검사 루프와 시퀀스 엔진 중단
            Global.Inst.InspStage.StopCycle();
        }

        //===== [그룹 4] 외부 연동용 유틸리티 메서드 =====

        //------- 외부 호출용 단일 이미지 캡처 -------
        public void CaptureImage()
        {
            Global.Inst.InspStage.CheckImageBuffer();
            Global.Inst.InspStage.Grab(0);
        }

        //------- 외부 호출용 라이브 모드 강제 시작 -------
        public void StartLive()
        {
            Global.Inst.InspStage.LiveMode = true;
            Global.Inst.InspStage.SetWorkingState(WorkingState.LIVE);
            Global.Inst.InspStage.CheckImageBuffer();
            Global.Inst.InspStage.Grab(0);
        }

        //------- 외부 호출용 라이브 모드 강제 종료 -------
        public void StopLive()
        {
            Global.Inst.InspStage.LiveMode = false;
            Global.Inst.InspStage.SetWorkingState(WorkingState.NONE);
        }
    }
}