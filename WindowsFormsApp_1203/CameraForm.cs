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

namespace WindowsFormsApp_1203
{
    //public partial class CameraForm :Form
    public partial class CameraForm : DockContent
    {
        public CameraForm()
        {
            InitializeComponent();
        }

        public void LoadImage(string filename) {
            if (File.Exists(filename) == false) return;

            Image bitmap = Image.FromFile(filename);
            imageViewer.LoadBitmap((Bitmap)bitmap);
        }

        private void imageViewer_Load(object sender, EventArgs e)
        {

        }
    }
}
