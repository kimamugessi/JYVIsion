using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JYVision.Core;
using OpenCvSharp;

namespace JYVision.Algorithm
{
    public struct BinaryThreshold
    {
        public int lower { get; set; }
        public int upper { get; set; }
        public bool invert { get; set; }

        public BinaryThreshold(int _lower, int _upper,bool _invert)
        {
            lower = _lower; upper = _upper; invert = _invert;
        }
    }

    public enum BinaryMethod : int
    {
        [Description("필터")]
        Feature, 
        [Description("필셀갯수")]
        PixelCount
    }
    public class BlobFilter
    {
        public string name { get; set; }
        public bool isUse { get; set; }
        public int min { get; set; }
        public int max { get; set; }

        public BlobFilter() { }
    }

    public class BlobAlgorithm :InspAlgorithm
    {
        public BinaryThreshold BinThreshold {  get; set; }= new BinaryThreshold();

        public readonly int FILTER_AREA = 0;
        public readonly int FILTER_WIDTH = 1;
        public readonly int FILTER_HEIGHT = 2;
        public readonly int FILTER_COUNT = 3;

        private List<DrawInspectInfo> _findArea;
        public BinaryMethod BinMethod { get; set; }=BinaryMethod.Feature;

        public bool UseRotatedRect { get; set; } = false;

        List<BlobFilter> _filterBlobs = new List<BlobFilter>();
        
        public List<BlobFilter> BlobFilters
        {
            get { return _filterBlobs; }
            set { _filterBlobs = value; }
        }

        public int OutBlobCount { get; set; } = 0;
        public BlobAlgorithm()
        {
            InspectType = InspectType.InspBinary;
            BinThreshold = new BinaryThreshold(100, 200, false);
        }

        public override InspAlgorithm Clone()
        {
            var cloneAlgo = new BlobAlgorithm();

            // 공통 필드 복사
            this.CopyBaseTo(cloneAlgo);

            cloneAlgo.CopyFrom(this);

            return cloneAlgo;
        }

        public override bool CopyFrom(InspAlgorithm sourceAlgo)
        {
            BlobAlgorithm blobAlgo = (BlobAlgorithm)sourceAlgo;

            this.BinThreshold = blobAlgo.BinThreshold;
            this.BinMethod = blobAlgo.BinMethod;
            this.UseRotatedRect = blobAlgo.UseRotatedRect;

            this.BlobFilters = blobAlgo.BlobFilters
                               .Select(b => new BlobFilter
                               {
                                   name = b.name,
                                   isUse = b.isUse,
                                   min = b.min,
                                   max = b.max
                               })
                               .ToList();

            return true;
        }

        public void SetDefault()
        {
            //픽셀 영역으로 이진화 필터
            BlobFilter areaFilter = new BlobFilter()
            { name = "Area", isUse = false, min = 200, max = 500 };
            _filterBlobs.Add(areaFilter);

            BlobFilter widthFilter = new BlobFilter()
            { name = "width", isUse = false, min = 0, max = 0 };
            _filterBlobs.Add(widthFilter);

            BlobFilter heightFilter = new BlobFilter()
            { name = "Height", isUse = false, min = 0, max = 0 };
            _filterBlobs.Add(heightFilter);

            BlobFilter countFilter = new BlobFilter()
            { name = "Count", isUse = false, min = 0, max = 0 };
            _filterBlobs.Add(countFilter);
        }

        public override bool DoInspect()
        {
            ResetResult();
            OutBlobCount = 0;

            if(_srcImage==null) return false;

            if(InspRect.Right>_srcImage.Width||InspRect.Bottom>_srcImage.Height) return false;

            Mat targetImage = _srcImage[InspRect];

            Mat grayImage=new Mat();
            if (targetImage.Type() == MatType.CV_8UC3)
                Cv2.CvtColor(targetImage, grayImage, ColorConversionCodes.BGR2GRAY);
            else
                grayImage = targetImage;

            Mat binaryImage = new Mat();
            Cv2.InRange(grayImage, BinThreshold.lower, BinThreshold.upper, binaryImage);

            if(BinThreshold.invert)
                binaryImage=~binaryImage;

            if (BinaryMethod.PixelCount == BinMethod)
            {
                if (!InspPixelCount(binaryImage))
                    return false;
            }
            else if (BinaryMethod.Feature == BinMethod)
            {
                if (!InspBlobFilter(binaryImage))
                    return false;
            }

            IsInspected = true;

            return true;
        }

        public override void ResetResult()
        {
            base.ResetResult();
            if(_findArea!=null) _findArea.Clear();
        }

        private bool InspPixelCount(Mat binImage) {
            if(binImage.Empty()) return false;

            int pixelCount=Cv2.CountNonZero(binImage);

            _findArea.Clear();

            IsDefect = false;
            string result = "OK";

            string featureInfo = $"A:{pixelCount}";

            BlobFilter areaFilter = BlobFilters[FILTER_AREA];
            if (areaFilter.isUse)
            {
                if ((areaFilter.min > 0 && pixelCount < areaFilter.min) || (areaFilter.max > 0 && pixelCount < areaFilter.max))
                {
                    IsDefect = true;
                    result = "NG";
                }
            }

            Rect blobRect = new Rect(InspRect.Left,InspRect.Top, binImage.Width, binImage.Height);

            string blobInfo;
            blobInfo = $"Blob X:{blobRect.X}, Y:{blobRect.Y}, Size({blobRect.Width},{blobRect.Height})";
            ResultString.Add(blobInfo);

            DrawInspectInfo rectInfo = new DrawInspectInfo(blobRect, featureInfo, InspectType.InspBinary, DecisionType.Info);
            _findArea.Add(rectInfo);

            OutBlobCount = 1;

            if (IsDefect)
            {
                string resultInfo = "";
                resultInfo = $"[{result}] Blob count [in : {areaFilter.min},{areaFilter.max},out : {pixelCount}]";
                ResultString.Add(resultInfo);
            }

            return true;
        }
        private bool InspBlobFilter(Mat binImage)
        {
            // 컨투어 찾기
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(binImage, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            // 필터링된 객체를 담을 리스트
            Mat filteredImage = Mat.Zeros(binImage.Size(), MatType.CV_8UC1);

            if (_findArea is null)
                _findArea = new List<DrawInspectInfo>();

            _findArea.Clear();

            int findBlobCount = 0;

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area <= 0)
                    continue;

                int showArea = 0;
                int showWidth = 0;
                int showHeight = 0;

                BlobFilter areaFilter = BlobFilters[FILTER_AREA];

                if (areaFilter.isUse)
                {
                    if (areaFilter.min > 0 && area < areaFilter.min)
                        continue;

                    if (areaFilter.max > 0 && area > areaFilter.max)
                        continue;

                    showArea = (int)(area + 0.5f);
                }

                Rect boundingRect = Cv2.BoundingRect(contour);
                RotatedRect rotatedRect = Cv2.MinAreaRect(contour);
                Size2d blobSize = new Size2d(boundingRect.Width, boundingRect.Height);

                // RotatedRect 정보 계산
                if (UseRotatedRect)
                {
                    // 너비와 높이 가져오기
                    float width = rotatedRect.Size.Width;
                    float height = rotatedRect.Size.Height;

                    // 장축과 단축 구분
                    blobSize.Width = Math.Max(width, height);
                    blobSize.Height = Math.Min(width, height);
                }

                BlobFilter widthFilter = BlobFilters[FILTER_WIDTH];
                if (widthFilter.isUse)
                {
                    if (widthFilter.min > 0 && blobSize.Width < widthFilter.min)
                        continue;

                    if (widthFilter.max > 0 && blobSize.Width > widthFilter.max)
                        continue;

                    showWidth = (int)(blobSize.Width + 0.5f);
                }

                BlobFilter heightFilter = BlobFilters[FILTER_HEIGHT];
                if (heightFilter.isUse)
                {
                    if (heightFilter.min > 0 && blobSize.Height < heightFilter.min)
                        continue;

                    if (heightFilter.max > 0 && blobSize.Height > heightFilter.max)
                        continue;

                    showHeight = (int)(blobSize.Height + 0.5f);
                }

                // 필터링된 객체를 이미지에 그림
                //Cv2.DrawContours(filteredImage, new Point[][] { contour }, -1, Scalar.White, -1);

                findBlobCount++;
                Rect blobRect = boundingRect + InspRect.TopLeft;

                string featureInfo = "";
                if (showArea > 0)
                    featureInfo += $"A:{showArea}";

                if (showWidth > 0)
                {
                    if (featureInfo != "")
                        featureInfo += "\r\n";

                    featureInfo += $"W:{showWidth}";
                }

                if (showHeight > 0)
                {
                    if (featureInfo != "")
                        featureInfo += "\r\n";

                    featureInfo += $"H:{showHeight}";
                }

                //검사된 정보를 문자열로 저장
                string blobInfo;
                blobInfo = $"Blob X:{blobRect.X}, Y:{blobRect.Y}, Size({blobRect.Width},{blobRect.Height})";
                ResultString.Add(blobInfo);

                //검사된 영역 정보를 DrawInspectInfo로 저장
                DrawInspectInfo rectInfo = new DrawInspectInfo(blobRect, featureInfo, InspectType.InspBinary, DecisionType.Info);

                if (UseRotatedRect)
                {
                    Point2f[] points = rotatedRect.Points().Select(p => p + InspRect.TopLeft).ToArray();
                    rectInfo.SetRotatedRectPoints(points);
                }

                _findArea.Add(rectInfo);
            }

            OutBlobCount = findBlobCount;

            IsDefect = false;
            string result = "OK";
            BlobFilter countFilter = BlobFilters[FILTER_COUNT];

            if (countFilter.isUse)
            {
                if (countFilter.min > 0 && findBlobCount < countFilter.min)
                    IsDefect = true;

                if (IsDefect == false && countFilter.max > 0 && findBlobCount > countFilter.max)
                    IsDefect = true;
            }

            if (IsDefect)
            {
                string rectInfo = $"Count:{findBlobCount}";
                _findArea.Add(new DrawInspectInfo(InspRect, rectInfo, InspectType.InspBinary, DecisionType.Defect));

                result = "NG";

                string resultInfo = "";
                resultInfo = $"[{result}] Blob count [in : {countFilter.min},{countFilter.max},out : {findBlobCount}]";
                ResultString.Add(resultInfo);
            }

            return true;
        }

        //#8_INSPECT_BINARY#7 검사 결과 영역 영역 반환
        public override int GetResultRect(out List<DrawInspectInfo> resultArea)
        {
            resultArea = null;

            //검사가 완료되지 않았다면, 리턴
            if (!IsInspected)
                return -1;

            if (_findArea is null || _findArea.Count <= 0)
                return -1;

            resultArea = _findArea;
            return resultArea.Count;
        }
    }
}
