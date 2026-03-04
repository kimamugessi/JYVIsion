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

        public int MatchScore { get; set; } = 30; // 기본 임계값

        [XmlIgnore]
        public List<MatchResult> MatchResults { get; set; } = new List<MatchResult>();

        public Size ExtSize { get; set; } = new Size(0, 0);
        public bool InvertResult { get; set; } = false;
        public int OutScore { get; set; } = 0;
        public List<Point> OutPoints { get; set; } = new List<Point>();
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

        public void AddTemplateImage(Mat templateImage) => _templateImages.Add(templateImage.Clone());
        public void ResetTemplateImages() => _templateImages.Clear();
        public List<Mat> GetTemplateImages() => _templateImages;

        public override bool DoInspect()
        {
            if (_srcImage == null || _templateImages.Count == 0) return false;

            // 💡 결과 리스트 초기화 (잔상 제거)
            ResetResult();
            OutPoints.Clear();
            MatchResults.Clear();
            OutScore = 0;

            Mat template = _templateImages[0];
            if (template == null || template.Empty()) return false;

            using (Mat res = new Mat())
            {
                Cv2.MatchTemplate(_srcImage, template, res, TemplateMatchModes.CCoeffNormed);

                float matchThreshold = MatchScore / 100.0f;
                int halfWidth = template.Width / 2;
                int halfHeight = template.Height / 2;

                while (true)
                {
                    double minVal, maxVal;
                    Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(res, out minVal, out maxVal, out minLoc, out maxLoc);

                    if (maxVal < matchThreshold) break;

                    Point center = new Point(maxLoc.X + halfWidth, maxLoc.Y + halfHeight);
                    MatchResult resData = new MatchResult { Center = center, Score = (int)(maxVal * 100) };

                    MatchResults.Add(resData);
                    OutPoints.Add(resData.Center);
                    if (resData.Score > OutScore) OutScore = resData.Score;

                    // 중복 검출 방지
                    Cv2.Rectangle(res, new Rect(maxLoc.X - halfWidth, maxLoc.Y - halfHeight, template.Width, template.Height), new Scalar(0), -1);
                    if (MatchResults.Count > 50) break;
                }
            }

            IsInspected = true;
            IsDefect = (OutPoints.Count != 2); // 볼트가 2개일 때만 Good
            ResultString.Add($"검출 수: {OutPoints.Count}, 최고 점수: {OutScore}%");
            return true;
        }

        public override int GetResultRect(out List<DrawInspectInfo> resultArea)
        {
            resultArea = new List<DrawInspectInfo>();
            if (!IsInspected || MatchResults.Count == 0) return 0;

            int w = _templateImages[0].Width;
            int h = _templateImages[0].Height;

            foreach (var res in MatchResults)
            {
                DecisionType color = (res.Score >= MatchScore) ? DecisionType.Good : DecisionType.Defect;
                resultArea.Add(new DrawInspectInfo(new Rect(res.Center.X - w / 2, res.Center.Y - h / 2, w, h), $"{res.Score}%", InspectType.InspMatch, color));
            }
            return resultArea.Count;
        }

        public Point GetOffset()
        {
            if (IsInspected && OutPoints.Count == 2)
            {
                Point centerOfBolts = new Point((OutPoints[0].X + OutPoints[1].X) / 2, (OutPoints[0].Y + OutPoints[1].Y) / 2);
                return new Point(centerOfBolts.X - InspRect.X, centerOfBolts.Y - InspRect.Y);
            }
            return new Point(0, 0);
        }

        public List<Rect> GetBoltPairROIs()
        {
            var pairROIs = new List<Rect>();
            if (!IsInspected || MatchResults.Count < 2) return pairROIs;

            var sortedByX = MatchResults.OrderBy(b => b.Center.X).ToList();
            int xTolerance = 100;
            var xGroups = new List<List<MatchResult>>();

            foreach (var res in sortedByX)
            {
                var targetGroup = xGroups.FirstOrDefault(g => Math.Abs(g[0].Center.X - res.Center.X) < xTolerance);
                if (targetGroup == null) xGroups.Add(new List<MatchResult> { res });
                else targetGroup.Add(res);
            }

            int w = _templateImages[0].Width;
            int h = _templateImages[0].Height;

            foreach (var group in xGroups)
            {
                var sortedInGroup = group.OrderBy(b => b.Center.Y).ToList();
                for (int i = 0; i < sortedInGroup.Count - 1; i += 2)
                {
                    var b1 = sortedInGroup[i];
                    var b2 = sortedInGroup[i + 1];
                    int minX = Math.Min(b1.Center.X, b2.Center.X) - w / 2;
                    int maxX = Math.Max(b1.Center.X, b2.Center.X) + w / 2;
                    int minY = b1.Center.Y - h / 2;
                    int maxY = b2.Center.Y + h / 2;
                    pairROIs.Add(new Rect(minX, minY, maxX - minX, maxY - minY));
                }
            }
            return pairROIs;
        }
    }
}