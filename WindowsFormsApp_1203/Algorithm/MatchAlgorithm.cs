using JYVision.Core;

using JYVision.Util;

using OpenCvSharp;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Xml.Serialization;



namespace JYVision.Algorithm

{

    // 💡 볼트 좌표와 점수를 함께 관리하기 위한 구조체

    public struct MatchResult

    {

        public Point Center; // 볼트 중심 좌표

        public int Score;    // 해당 볼트의 매칭 점수

    }



    public class MatchAlgorithm : InspAlgorithm

    {

        [XmlIgnore]

        private List<Mat> _templateImages = new List<Mat>();



        public int MatchScore { get; set; } = 60; // 기본 임계값 60%



        // 💡 상세 결과(좌표+점수)를 저장하는 리스트 (XmlIgnore하여 저장하지 않음)

        [XmlIgnore]

        public List<MatchResult> MatchResults { get; set; } = new List<MatchResult>();



        // 전체 이미지 검색을 위해 기본값 수정

        public Size ExtSize { get; set; } = new Size(0, 0);

        public bool InvertResult { get; set; } = false;

        public int OutScore { get; set; } = 0; // 화면 표시용 최고 점수

        public Point OutPoint { get; set; } = new Point(0, 0);

        public List<Point> OutPoints { get; set; } = new List<Point>(); // 호환용

        public int MatchCount { get; set; } = 1;



        public MatchAlgorithm()

        {

            InspectType = InspectType.InspMatch;

        }



        public override InspAlgorithm Clone()

        {

            var cloneAlgo = new MatchAlgorithm();

            CopyBaseTo(cloneAlgo);

            cloneAlgo.MatchScore = this.MatchScore;

            cloneAlgo.ExtSize = this.ExtSize;

            cloneAlgo.InvertResult = this.InvertResult;

            cloneAlgo.MatchCount = this.MatchCount;

            return cloneAlgo;

        }



        public override bool CopyFrom(InspAlgorithm sourceAlgo)

        {

            MatchAlgorithm matchAlgo = (MatchAlgorithm)sourceAlgo;

            this.MatchScore = matchAlgo.MatchScore;

            this.ExtSize = matchAlgo.ExtSize;

            this.InvertResult = matchAlgo.InvertResult;

            this.MatchCount = matchAlgo.MatchCount;

            return true;

        }



        public void AddTemplateImage(Mat templateImage)

        {

            _templateImages.Add(templateImage.Clone());

        }



        public void ResetTemplateImages()

        {

            _templateImages.Clear();

        }



        public List<Mat> GetTemplateImages()

        {

            return _templateImages;

        }



        // 매칭 알고리즘 검사 구현

        public override bool DoInspect()

        {



            if (_srcImage == null || _templateImages.Count == 0) return false;



            // 💡 1. 검사 시작 즉시 모든 이전 결과 리스트를 완전히 비움 (잔상 제거 핵심)

            ResetResult();

            OutPoints.Clear();

            MatchResults.Clear();

            OutScore = 0;



            Mat template = _templateImages[0];

            if (template == null || template.Empty()) return false;



            // 💡 메모리 누수 방지를 위해 using 블록 사용

            using (Mat res = new Mat())

            {

                // 2. 전체 이미지 영역에서 매칭 수행

                Cv2.MatchTemplate(_srcImage, template, res, TemplateMatchModes.CCoeffNormed);



                float matchThreshold = MatchScore / 100.0f;

                int halfWidth = template.Width / 2;

                int halfHeight = template.Height / 2;



                // 3. 다중 검출 루프 (최고점부터 차례로 찾기)

                while (true)

                {

                    double minVal, maxVal;

                    Point minLoc, maxLoc;

                    // 결과 맵에서 현재 최고점 찾기

                    Cv2.MinMaxLoc(res, out minVal, out maxVal, out minLoc, out maxLoc);



                    // 임계값 미달이면 종료

                    if (maxVal < matchThreshold) break;



                    // 중심점 계산

                    Point center = new Point(maxLoc.X + halfWidth, maxLoc.Y + halfHeight);



                    // 💡 상세 결과 저장 (좌표 + 해당 위치의 점수)

                    MatchResult resData = new MatchResult

                    {

                        Center = center,

                        Score = (int)(maxVal * 100)

                    };



                    MatchResults.Add(resData);

                    OutPoints.Add(resData.Center);



                    // 최고 점수 업데이트 (대표값)

                    if (resData.Score > OutScore) OutScore = resData.Score;



                    // 💡 찾은 영역 지우기 (중복 검출 방지)

                    Cv2.Rectangle(res,

                        new Rect(maxLoc.X - halfWidth, maxLoc.Y - halfHeight, template.Width, template.Height),

                        new Scalar(0), -1);



                    // 무한 루프 안전장치

                    if (MatchResults.Count > 50) break;

                }

            }



            IsInspected = true;



            // 💡 볼트가 정확히 2개일 때만 정상(Good), 아니면 불량(Defect)

            IsDefect = (OutPoints.Count != 2);



            ResultString.Add($"검출 수: {OutPoints.Count}, 최고 점수: {OutScore}%");

            return true;

        }



        public override int GetResultRect(out List<DrawInspectInfo> resultArea)

        {

            // 💡 1. 결과 리스트 초기화 (이전 잔상 제거의 핵심)

            resultArea = new List<DrawInspectInfo>();



            // 💡 2. 검사가 안 되었거나, 검출된 볼트가 없으면 빈 리스트를 반환하여 화면 지움

            if (!IsInspected || MatchResults.Count == 0) return 0;



            int w = _templateImages[0].Width;

            int h = _templateImages[0].Height;



            foreach (var res in MatchResults)

            {

                // 💡 3. 개별 점수에 따른 색상 변경 (MatchScore 기준)

                DecisionType individualColor = (res.Score >= MatchScore)

                                               ? DecisionType.Good   // 기준 이상: 초록색

                                               : DecisionType.Defect; // 기준 미달: 빨간색



                resultArea.Add(new DrawInspectInfo(

                    new Rect(res.Center.X - w / 2, res.Center.Y - h / 2, w, h),

                    $"{res.Score}%",

                    InspectType.InspMatch,

                    individualColor

                ));

            }



            return resultArea.Count;

        }



        public Point GetOffset()

        {

            // 💡 2개의 볼트 위치를 가지고 캘리브레이션(기울기) 계산을 해야 함

            if (IsInspected && OutPoints.Count == 2)

            {

                // 두 볼트의 중심을 기준으로 한 오프셋 계산

                Point centerOfBolts = new Point(

                    (OutPoints[0].X + OutPoints[1].X) / 2,

                    (OutPoints[0].Y + OutPoints[1].Y) / 2

                );

                return new Point(centerOfBolts.X - InspRect.X, centerOfBolts.Y - InspRect.Y);

            }

            return new Point(0, 0);

        }

        public List<Rect> GetBoltPairROIs()
        {
            var pairROIs = new List<Rect>();
            if (!IsInspected || MatchResults.Count < 2) return pairROIs;

            // 1. X축 기준으로 정렬
            var sortedByX = MatchResults.OrderBy(b => b.Center.X).ToList();

            // 2. X축 픽셀 차이가 넉넉히(예: 100px) 나도 같은 열로 인식하도록 설정
            int xTolerance = 100; // 💡 이 값을 키우면 멀리 떨어진 볼트도 같은 줄로 묶습니다.
            var xGroups = new List<List<MatchResult>>();

            foreach (var res in sortedByX)
            {
                // 현재 볼트와 X좌표가 비슷한 그룹 찾기
                var targetGroup = xGroups.FirstOrDefault(g => Math.Abs(g[0].Center.X - res.Center.X) < xTolerance);
                if (targetGroup == null) xGroups.Add(new List<MatchResult> { res });
                else targetGroup.Add(res);
            }

            int w = _templateImages[0].Width;
            int h = _templateImages[0].Height;

            // 3. 각 그룹(세로 줄) 내에서 Y축 짝짓기
            foreach (var group in xGroups)
            {
                var sortedInGroup = group.OrderBy(b => b.Center.Y).ToList();
                for (int i = 0; i < sortedInGroup.Count - 1; i += 2)
                {
                    var b1 = sortedInGroup[i];
                    var b2 = sortedInGroup[i + 1];

                    int minX = Math.Min(b1.Center.X, b2.Center.X) - w / 2;
                    int maxX = Math.Max(b1.Center.X, b2.Center.X) + w / 2;
                    int minY = b1.Center.Y - h / 2; // 위쪽 볼트 끝
                    int maxY = b2.Center.Y + h / 2; // 아래쪽 볼트 끝

                    pairROIs.Add(new Rect(minX, minY, maxX - minX, maxY - minY));
                }
            }
            return pairROIs;
        }

    }

}