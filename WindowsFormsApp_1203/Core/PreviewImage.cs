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
        private Mat _originalImage=null;
        private Mat _previewimage=null;

        private InspWindow _inspWindow = null;
        private bool _usePreview = true;

        public void SetImage(Mat image)
        {
            _originalImage = image;
            _previewimage = new Mat();
        }

        public void SetInspWindow(InspWindow inspWindow)    /*검사 윈도우 설정*/
        {
            _inspWindow = inspWindow;
        }

        public void SetBinary(int lowerValue, int upperValue, bool invert, ShowBinaryMode showBinaryMode)
        {
            if(_usePreview==false) return;
            if(_originalImage == null) return;

            var cameraForm=MainForm.GetDockForm<CameraForm>();
            if(cameraForm == null) return;

            Bitmap bmpImage;
            if (showBinaryMode == ShowBinaryMode.ShowBinaryNone)
            {
                bmpImage=BitmapConverter.ToBitmap(_originalImage);
                cameraForm.UpdateDisplay(bmpImage);
                return;
            }
            Rect windowArea = new Rect(0,0,_originalImage.Width,_originalImage.Height);

            if (_inspWindow != null) //검사 윈도우가 설정되어 있다면 해당 영역으로 설정
            {
                windowArea = _inspWindow.WindowArea; //검사 윈도우 영역
            }

            Mat orgRoi = _originalImage[windowArea];
            Mat grayImage = new Mat();
            if (orgRoi.Type() == MatType.CV_8UC3)
                Cv2.CvtColor(orgRoi, grayImage, ColorConversionCodes.BGR2GRAY);
            else
                grayImage = orgRoi;

            Mat binaryMask = new Mat();
            Cv2.InRange(grayImage, lowerValue, upperValue, binaryMask);

            if (invert)
                binaryMask = ~binaryMask;

            Mat fullBinaryMask = Mat.Zeros(_originalImage.Size(), MatType.CV_8UC1);
            binaryMask.CopyTo(new Mat(fullBinaryMask, windowArea));

            if (showBinaryMode == ShowBinaryMode.ShowBinaryOnly)
            {
                if (orgRoi.Type() == MatType.CV_8UC3)
                {
                    Mat colorBinary = new Mat();
                    Cv2.CvtColor(binaryMask, colorBinary, ColorConversionCodes.GRAY2BGR);
                    _previewimage = _originalImage.Clone();
                    colorBinary.CopyTo(new Mat(_previewimage, windowArea));
                }
                else
                {
                    _previewimage = _originalImage.Clone();
                    binaryMask.CopyTo(new Mat(_previewimage, windowArea));
                }

                bmpImage = BitmapConverter.ToBitmap(_previewimage);
                cameraForm.UpdateDisplay(bmpImage);
                return;
            }
            Scalar highlightColor;
            if (showBinaryMode == ShowBinaryMode.ShowBinaryHighlightRed)
                highlightColor = new Scalar(0, 0, 255);
            else if (showBinaryMode == ShowBinaryMode.ShowBinaryHighlightGreen)
                highlightColor = new Scalar(0, 255, 0);
            else
                highlightColor = new Scalar(255, 0, 0);

            Mat overlayImage;
            if (_originalImage.Type() == MatType.CV_8UC1)
            {
                overlayImage = new Mat();
                Cv2.CvtColor(_originalImage, overlayImage, ColorConversionCodes.GRAY2BGR);

                Mat colorOrinal = overlayImage.Clone();

                overlayImage.SetTo(highlightColor, fullBinaryMask);

                Cv2.AddWeighted(colorOrinal, 0.7, overlayImage, 0.3, 0, _previewimage);
            }
            else
            {
                overlayImage = _originalImage.Clone();
                overlayImage.SetTo(highlightColor, fullBinaryMask); 

                Cv2.AddWeighted(_originalImage, 0.7, overlayImage, 0.3, 0, _previewimage);
            }

            bmpImage = BitmapConverter.ToBitmap(_previewimage);
            cameraForm.UpdateDisplay(bmpImage);
        }
    }
}
