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

        public RunForm()
        {
            InitializeComponent();
        }

        //===== [그룹 2] 카메라 및 그랩 제어 =====

        //------- 단일 프레임 그랩 실행 -------
        private void btnGrab_Click(object sender, EventArgs e)
        {
            // 현재 설정된 해상도와 버퍼 크기가 일치하는지 확인 후 1회 촬영
            Global.Inst.InspStage.CheckImageBuffer();
            Global.Inst.InspStage.Grab(0);
        }

        //------- 실시간 라이브 모드 토글 -------
        private void btnLive_Click(object sender, EventArgs e)
        {
            // 라이브 모드 상태를 반전 (On <-> Off)
            Global.Inst.InspStage.LiveMode = !Global.Inst.InspStage.LiveMode;

            if (Global.Inst.InspStage.LiveMode)
            {
                // 라이브 시작 시 상태바를 LIVE로 변경하고 연속 촬영 시작
                Global.Inst.InspStage.SetWorkingState(WorkingState.LIVE);
                Global.Inst.InspStage.CheckImageBuffer();
                Global.Inst.InspStage.Grab(0);
            }
            else
            {
                // 라이브 종료 시 상태 초기화
                Global.Inst.InspStage.SetWorkingState(WorkingState.NONE);
            }
        }

        //===== [그룹 3] 검사 실행 및 중지 =====

        //------- 자동 운전 및 검사 시작 -------
        private void btnStart_Click(object sender, EventArgs e)
        {
            // 검사 결과 파일명이나 DB 저장용 고유 시리얼 생성 (날짜 기반)
            string serialID = $"{DateTime.Now:MM-dd HH:mm:ss}";
            Global.Inst.InspStage.InspectReady("LOT_NUMBER", serialID);

            // 카메라 연결 상태에 따라 가상 검사(파일) 또는 실제 카메라 운전 시작
            if (SettingXml.Inst.CamType == Grab.CameraType.None)
            {
                // 가상 모드: 이미지 파일을 순차적으로 불러오며 검사
                bool cycleMode = SettingXml.Inst.CycleMode;
                Global.Inst.InspStage.CycleInspect(cycleMode);
            }
            else
            {
                // 카메라 모드: 시퀀스(PLC 연동 등)에 따른 자동 운전 시작
                Global.Inst.InspStage.StartAutoRun();
            }
        }

        //------- 검사 및 운전 중지 -------
        private void btnStop_Click(object sender, EventArgs e)
        {
            // 모든 검사 루프 및 자동 운전 시퀀스를 즉시 중단
            Global.Inst.InspStage.StopCycle();
        }

        //===== [그룹 4] 이미지 전처리 및 특수 기능 =====
        public void CaptureImage() { Global.Inst.InspStage.CheckImageBuffer(); Global.Inst.InspStage.Grab(0); }
        public void StartLive() { Global.Inst.InspStage.LiveMode = true; Global.Inst.InspStage.SetWorkingState(WorkingState.LIVE); Global.Inst.InspStage.CheckImageBuffer(); Global.Inst.InspStage.Grab(0); }
        public void StopLive() { Global.Inst.InspStage.LiveMode = false; Global.Inst.InspStage.SetWorkingState(WorkingState.NONE); }
    }
}