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
    public partial class RunForm: DockContent
    {
        public RunForm()
        {
            InitializeComponent();
        }

        private void btnGrab_Click(object sender, EventArgs e)
        {
            Global.Inst.InspStage.CheckImageBuffer();
            Global.Inst.InspStage.Grab(0);
        }
        private void btnStart_Click(object sender, EventArgs e)
        {
            string serialID = $"{DateTime.Now:MM-dd HH:mm:ss}";
            Global.Inst.InspStage.InspectReady("LOT_NUMBER", serialID);

            if (SettingXml.Inst.CamType == Grab.CameraType.None)
            {
                bool cycleMode = SettingXml.Inst.CycleMode;
                Global.Inst.InspStage.CycleInspect(cycleMode);
            }
            else
            {
                Global.Inst.InspStage.StartAutoRun();
            }
        }

        private void btnLive_Click(object sender, EventArgs e)
        {
            Global.Inst.InspStage.LiveMode = !Global.Inst.InspStage.LiveMode;

            if (Global.Inst.InspStage.LiveMode)
            {
                Global.Inst.InspStage.SetWorkingState(WorkingState.LIVE);

                Global.Inst.InspStage.CheckImageBuffer();
                Global.Inst.InspStage.Grab(0);
            }
            else
            {
                Global.Inst.InspStage.SetWorkingState(WorkingState.NONE);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            Global.Inst.InspStage.StopCycle();
        }

        private void edge_Click(object sender, EventArgs e)
        {
            var stage = Global.Inst.InspStage;
            eImageChannel channel = eImageChannel.Color;

            using (Mat src = stage.GetMat(0, channel))
            {
                if (src == null || src.Empty()) return;

                using (Mat enhanced = new Mat())
                {
                    // 1. 대비(Contrast) 및 밝기 조정
                    // 1.2는 대비(1.0보다 크면 강화), -20은 밝기입니다. 
                    // 글자와 배경의 차이를 그레이스케일 상태에서 벌려줍니다.
                    src.ConvertTo(enhanced, -1, 1.2, -20);

                    // 2. 언샤프 마스크(Unsharp Mask)로 경계선 강조
                    // 흐릿한 볼트 테두리를 알고리즘이 잘 잡도록 날카롭게 만듭니다.
                    using (Mat blurred = new Mat())
                    {
                        Cv2.GaussianBlur(enhanced, blurred, new OpenCvSharp.Size(5, 5), 1.5);
                        Cv2.AddWeighted(enhanced, 1.5, blurred, -0.5, 0, enhanced);
                    }

                    // 3. 미세한 노이즈 제거 (매칭 점수를 깎는 자잘한 점들 제거)
                    Cv2.MedianBlur(enhanced, enhanced, 3);

                    // 4. 실제 메모리 버퍼에 복사 (컬러/Stride 유지)
                    IntPtr destPtr = stage.ImageSpace.GetnspectionBufferPtr(0);
                    if (destPtr != IntPtr.Zero)
                    {
                        int width = enhanced.Width;
                        int height = enhanced.Height;
                        int bytesPerPixel = (int)enhanced.ElemSize();
                        int alignedWidth = (width + 3) / 4 * 4;
                        int destStride = alignedWidth * bytesPerPixel;

                        for (int y = 0; y < height; y++)
                        {
                            IntPtr srcRowPtr = enhanced.Ptr(y);
                            IntPtr destRowPtr = IntPtr.Add(destPtr, y * destStride);

                            int rowBytes = width * bytesPerPixel;
                            byte[] rowData = new byte[rowBytes];
                            System.Runtime.InteropServices.Marshal.Copy(srcRowPtr, rowData, 0, rowBytes);
                            System.Runtime.InteropServices.Marshal.Copy(rowData, 0, destRowPtr, rowBytes);
                        }

                        stage.ImageSpace.Split(0);
                    }
                }
            }

            // 화면 갱신
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
