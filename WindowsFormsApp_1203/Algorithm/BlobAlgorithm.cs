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
    public struct BinaryThreshold /*이진화 임계값 구조체*/
    {
        public int lower { get; set; } //이진화 하한값
        public int upper { get; set; } //이진화 상한값
        public bool invert { get; set; } //이진화 반전 여부

        public BinaryThreshold(int _lower, int _upper,bool _invert) //이진화 임계값 구조체 생성자
        {
            lower = _lower; upper = _upper; invert = _invert;
        }
    }

    public enum BinaryMethod : int /*이진화 검사 방법 열거형*/
    {
        [Description("필터")]
        Feature,
        [Description("픽셀갯수")]
        PixelCount
    }
    public class BlobFilter /*블롭 필터 클래스*/
    {
        public string name { get; set; } //필터 이름
        public bool isUse { get; set; } //필터 사용 유무
        public int min { get; set; } //필터 최소값
        public int max { get; set; } //필터 최대값

        public BlobFilter() { } //블롭 필터 클래스 생성자
    }

    public class BlobAlgorithm : InspAlgorithm  /*블롭 검사 알고리즘 클래스*/
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

        public override InspAlgorithm Clone()   /*검사 알고리즘 복제 함수*/
        {
            var cloneAlgo = new BlobAlgorithm(); // 새로운 BlobAlgorithm 객체 생성

            this.CopyBaseTo(cloneAlgo); // 기본 속성 복사

            cloneAlgo.CopyFrom(this); // BlobAlgorithm 고유 속성 복사

            return cloneAlgo; // 복제된 객체 반환
        }

        public override bool CopyFrom(InspAlgorithm sourceAlgo) /*검사 알고리즘 속성 복사 함수*/
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
            // 💡 1. 시작하자마자 무조건 상태 초기화
            ResetResult();
            if (_findArea == null) _findArea = new List<DrawInspectInfo>();
            _findArea.Clear();
            OutBlobCount = 0;

            if (_srcImage == null) return false;

            // 💡 2. 영역 이탈 체크
            if (InspRect.Right > _srcImage.Width || InspRect.Bottom > _srcImage.Height)
            {
                IsInspected = true; // 검사는 시도했음
                return false;
            }

            // 💡 3. 이미지 처리 로직
            using (Mat targetImage = _srcImage[InspRect])
            using (Mat grayImage = new Mat())
            using (Mat binaryImage = new Mat())
            {
                if (targetImage.Type() == MatType.CV_8UC3)
                    Cv2.CvtColor(targetImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    targetImage.CopyTo(grayImage);

                Cv2.InRange(grayImage, BinThreshold.lower, BinThreshold.upper, binaryImage);
                if (BinThreshold.invert) Cv2.BitwiseNot(binaryImage, binaryImage);

                // 💡 4. 실제 검사 수행
                bool success = false;
                if (BinMethod == BinaryMethod.PixelCount) success = InspPixelCount(binaryImage);
                else success = InspBlobFilter(binaryImage);

                // 만약 검출된 게 하나도 없다면 리스트를 다시 한번 비움
                if (!success || _findArea.Count == 0)
                {
                    _findArea.Clear();
                }
            }

            IsInspected = true;
            return true;
        }

        public override void ResetResult()
        {
            base.ResetResult();
            IsInspected = false;
            if (_findArea != null) _findArea.Clear();
        }

        //검사 영역에서 백색 픽셀의 갯수로 OK/NG 여부만 판단
        private bool InspPixelCount(Mat binImage)
        {
            if (binImage.Empty() || binImage.Type() != MatType.CV_8UC1)
                return false;

            int pixelCount=Cv2.CountNonZero(binImage);

            _findArea.Clear();

            IsDefect = false;
            string result = "OK";

            string featureInfo = $"A:{pixelCount}";

            BlobFilter areaFilter = BlobFilters[FILTER_AREA];
            if (areaFilter.isUse)
            {
                if ((areaFilter.min > 0 && pixelCount < areaFilter.min) ||
                    (areaFilter.max > 0 && pixelCount > areaFilter.max))
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

            if (_findArea == null)
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

            // 1. 카운트 필터 판정 (isUse가 true일 때만 판정하도록 엄격하게 제한)
            if (countFilter.isUse)
            {
                if ((countFilter.min > 0 && findBlobCount < countFilter.min) ||
                    (countFilter.max > 0 && findBlobCount > countFilter.max))
                {
                    IsDefect = true;
                }
            }
            // 💡 else일 때 IsDefect = false; 로 두면 '뭐라도 잡히면 NG'라는 상황이 안 생깁니다.

            // 2. 결과 처리
            if (IsDefect)
            {
                result = "NG";
                // 💡 의심하신 부분: InspRect 전체를 그리는 이 코드가 잔상의 주범입니다. 
                // 만약 전체 영역이 빨갛게 변하는 게 싫다면 아래 줄을 주석 처리하세요.
                // string rectInfo = $"Count:{findBlobCount}";
                // _findArea.Add(new DrawInspectInfo(InspRect, rectInfo, InspectType.InspBinary, DecisionType.Defect));

                string resultInfo = $"[{result}] Blob count [Set: {countFilter.min}~{countFilter.max}, Actual: {findBlobCount}]";
                ResultString.Add(resultInfo);
            }
            else if (findBlobCount == 0 && countFilter.isUse)
            {
                // 💡 추가 조치: 아무것도 안 잡혔는데 카운트 필터상 NG라면 리스트를 확실히 비워줍니다.
                _findArea.Clear();
            }

            return true;
        }

        //#8_INSPECT_BINARY#7 검사 결과 영역 영역 반환
        public override int GetResultRect(out List<DrawInspectInfo> resultArea)
        {
            // 💡 이전 잔상을 막기 위해 무조건 새 리스트 생성
            resultArea = new List<DrawInspectInfo>();

            // 검사를 안 했거나, 결과 리스트가 비어있으면 아무것도 안 함
            if (!IsInspected || _findArea == null || _findArea.Count == 0)
            {
                return 0;
            }

            // 현재 결과만 복사해서 전달
            resultArea.AddRange(_findArea);
            return resultArea.Count;
        }
    }
}
