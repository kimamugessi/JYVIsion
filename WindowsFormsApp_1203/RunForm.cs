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

        //------- 이미지 선명화 및 에지 강조 (알고리즘 보조용) -------
        private void edge_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            eImageChannel channel = eImageChannel.Color;

            // 현재 버퍼에 있는 이미지를 가져와 처리 시작
            using (Mat src = stage.GetMat(0, channel))
            {
                if (src == null || src.Empty()) return;

                using (Mat enhanced = new Mat())
                {
                    // 1. 대비(Contrast) 및 밝기 조정
                    // 알파(1.2): 대비 강화로 글자와 배경 차이 확대 / 베타(-20): 노이즈 억제를 위한 밝기 감소
                    src.ConvertTo(enhanced, -1, 1.2, -20);

                    // 2. 언샤프 마스크(Unsharp Mask)로 경계선 강조
                    // 가우시안 블러를 적용한 이미지와 원본의 가중치 합을 통해 테두리를 날카롭게 만듦
                    using (Mat blurred = new Mat())
                    {
                        Cv2.GaussianBlur(enhanced, blurred, new OpenCvSharp.Size(5, 5), 1.5);
                        Cv2.AddWeighted(enhanced, 1.5, blurred, -0.5, 0, enhanced);
                    }

                    // 3. 미세한 점 노이즈 제거
                    // 중앙값 블러를 적용해 매칭률을 떨어뜨리는 지저분한 픽셀들을 정리
                    Cv2.MedianBlur(enhanced, enhanced, 3);

                    // 4. 처리된 결과를 시스템 메모리 버퍼(IntPtr)에 직접 주입
                    // 비전 시스템의 무결성을 위해 OpenCV Mat 데이터를 Stride 정렬된 버퍼로 복사
                    IntPtr destPtr = stage.ImageSpace.GetnspectionBufferPtr(0);
                    if (destPtr != IntPtr.Zero)
                    {
                        int width = enhanced.Width;
                        int height = enhanced.Height;
                        int bytesPerPixel = (int)enhanced.ElemSize();
                        int alignedWidth = (width + 3) / 4 * 4; // 4바이트 정렬 계산
                        int destStride = alignedWidth * bytesPerPixel;

                        // 행(Row) 단위로 메모리 포인터를 이동하며 고속 복사 수행
                        for (int y = 0; y < height; y++)
                        {
                            IntPtr srcRowPtr = enhanced.Ptr(y);
                            IntPtr destRowPtr = IntPtr.Add(destPtr, y * destStride);

                            int rowBytes = width * bytesPerPixel;
                            byte[] rowData = new byte[rowBytes];
                            System.Runtime.InteropServices.Marshal.Copy(srcRowPtr, rowData, 0, rowBytes);
                            System.Runtime.InteropServices.Marshal.Copy(rowData, 0, destRowPtr, rowBytes);
                        }

                        // 복사 완료 후 채널 분리(R, G, B, Gray) 동기화
                        stage.ImageSpace.Split(0);
                    }
                }
            }

            // 5. 변경된 이미지를 화면(CameraForm)에 즉시 갱신
            foreach (Form openForm in Application.OpenForms)
            {
                if (openForm is CameraForm camForm)
                {
                    camForm.UpdateDisplay();
                    break;
                }
            }
        }
    }
}