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
using System.IO;
using JYVision.Core;

namespace JYVision
{
    //public partial class CameraForm :Form
    public partial class CameraForm : DockContent
    {
        public CameraForm()
        {
            InitializeComponent();
        }

        public void LoadImage(string filename)  //이미지 경로받아 PictureBox에 이미지를 로드
        {    
            if (File.Exists(filename) == false) return; //파일이 없다면 리턴

            Image bitmap = Image.FromFile(filename);
            imageViewer.LoadBitmap((Bitmap)bitmap);
        }

        private void imageViewer_Load(object sender, EventArgs e)
        {

        }

        private void CameraForm_Resize(object sender, EventArgs e)
        {
            int margin = 0;
            imageViewer.Width = this.Width - margin * 2;
            imageViewer.Height = this.Height - margin * 2;

            imageViewer.Location=new System.Drawing.Point(margin, margin);
        }
        public void UpdateDisplay(Bitmap bitmap = null)
        {
            if (bitmap == null)
            {
                //#6_INSP_STAGE#3 업데이트시 bitmap이 없다면 InspSpace에서 가져온다
                bitmap = Global.Inst.InspStage.GetBitmap(0);
                if (bitmap == null)
                    return;
            }

            if (imageViewer != null)
                imageViewer.LoadBitmap(bitmap);
        }
        public Bitmap GetDisplayImage()
        {
            Bitmap curImage = null;

            if (imageViewer != null)
                curImage = imageViewer.GetCurBitmap();

            return curImage;
        }
    }
}
