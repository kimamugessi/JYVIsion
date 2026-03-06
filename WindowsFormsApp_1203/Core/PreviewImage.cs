using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JYVision.Property;
using JYVision.Teach;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace JYVision.Core
{
    public class PreviewImage
    {
        private Mat _orinalImage = null;
        private Mat _previewImage = null;

        private InspWindow _inspWindow = null;

        public void SetImage(Mat image)
        {
            _orinalImage = image;
            _previewImage = new Mat();
        }

   
        public void SetInspWindow(InspWindow inspwindow)
        {
            _inspWindow = inspwindow;
        }
    }
}
