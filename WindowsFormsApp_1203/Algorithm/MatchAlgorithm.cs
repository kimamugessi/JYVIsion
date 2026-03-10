using JYVision.Core;
using JYVision.Util;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace JYVision.Algorithm
{
    //===== 볼트 좌표와 점수 관리용 구조체 =====
    public struct MatchResult
    {
        public Point Center; // 볼트의 중심 좌표
        public int Score;    // 매칭 신뢰도 점수 (0~100)
    }

    //===== 템플릿 매칭 알고리즘 클래스 =====
    public class MatchAlgorithm : InspAlgorithm
    {
        //===== [그룹 1] 필드 및 속성 설정 =====
        [XmlIgnore]
        private List<Mat> _templateImages = new List<Mat>(); // 매칭에 사용할 마스터 이미지들

        public int MatchScore { get; set; } = 10; // 합격 기준 최소 점수 (Threshold)

        [XmlIgnore]
        public List<MatchResult> MatchResults { get; set; } = new List<MatchResult>(); // 검출된 모든 결과 리스트

        public Size ExtSize { get; set; } = new Size(0, 0);
        public bool InvertResult { get; set; } = false;
        public int OutScore { get; set; } = 0; // 검출된 결과 중 최고 점수
        public List<Point> OutPoints { get; set; } = new List<Point>(); // 검출된 중심점 좌표들
        public int MatchCount { get; set; } = 1; // 찾고자 하는 대상의 개수

        public MatchAlgorithm()
        {
            InspectType = InspectType.InspMatch;
        }

        //===== [그룹 2] 알고리즘 복제 및 데이터 복사 =====

        //------- 알고리즘 객체 복제 -------
        public override InspAlgorithm Clone()
        {
            var cloneAlgo = new MatchAlgorithm();
            CopyBaseTo(cloneAlgo); // 기본 설정값 복사
            cloneAlgo.MatchScore = this.MatchScore;
            cloneAlgo.ExtSize = this.ExtSize;
            cloneAlgo.InvertResult = this.InvertResult;
            cloneAlgo.MatchCount = this.MatchCount;
            return cloneAlgo;
        }

        //------- 타 알고리즘으로부터 설정값 가져오기 -------
        public override bool CopyFrom(InspAlgorithm sourceAlgo)
        {
            MatchAlgorithm matchAlgo = (MatchAlgorithm)sourceAlgo;
            this.MatchScore = matchAlgo.MatchScore;
            this.ExtSize = matchAlgo.ExtSize;
            this.InvertResult = matchAlgo.InvertResult;
            this.MatchCount = matchAlgo.MatchCount;
            return true;
        }

        //===== [그룹 3] 템플릿(마스터) 이미지 관리 =====

        //------- 템플릿 이미지 등록 -------
        public void AddTemplateImage(Mat templateImage) => _templateImages.Add(templateImage.Clone());

        //------- 템플릿 리스트 초기화 -------
        public void ResetTemplateImages() => _templateImages.Clear();

        //------- 등록된 템플릿 목록 반환 -------
        public List<Mat> GetTemplateImages() => _templateImages;

        //===== [그룹 4] 검사 실행 및 결과 처리 =====

        //------- 메인 템플릿 매칭 실행 -------
        public override bool DoInspect()
        {
            if (_srcImage == null || _templateImages.Count == 0) return false;

            // 이전 검사 결과 데이터 싹 비우기 (잔상 방지)
            ResetResult();
            OutPoints.Clear();
            MatchResults.Clear();
            OutScore = 0;

            Mat template = _templateImages[0]; // 첫 번째 등록된 마스터 이미지를 기준으로 사용
            if (template == null || template.Empty()) return false;

            using (Mat res = new Mat())
            {
                // OpenCV의 MatchTemplate 실행 (정규화된 상관계수 매칭 방식 사용)
                Cv2.MatchTemplate(_srcImage, template, res, TemplateMatchModes.CCoeffNormed);

                float matchThreshold = MatchScore / 100.0f; // 0.0 ~ 1.0 사이 값으로 변환
                int halfWidth = template.Width / 2;
                int halfHeight = template.Height / 2;

                // 다중 검출 루프 (가장 높은 점수부터 차례대로 찾음)
                while (true)
                {
                    double minVal, maxVal;
                    Point minLoc, maxLoc;
                    // 매칭 결과 맵에서 최대값(MaxVal)과 그 위치(MaxLoc)를 찾음
                    Cv2.MinMaxLoc(res, out minVal, out maxVal, out minLoc, out maxLoc);

                    // 최고 점수가 기준치보다 낮으면 루프 종료
                    if (maxVal < matchThreshold) break;

                    // 결과 데이터 저장 (검출 영역의 정중앙 좌표 계산)
                    Point center = new Point(maxLoc.X + halfWidth, maxLoc.Y + halfHeight);
                    MatchResult resData = new MatchResult { Center = center, Score = (int)(maxVal * 100) };

                    MatchResults.Add(resData);
                    OutPoints.Add(resData.Center);
                    if (resData.Score > OutScore) OutScore = resData.Score;

                    // [중요] 중복 검출 방지: 이미 찾은 지점 주변을 0(검정)으로 덮어버림
                    Cv2.Rectangle(res, new Rect(maxLoc.X - halfWidth, maxLoc.Y - halfHeight, template.Width, template.Height), new Scalar(0), -1);

                    // 무한 루프 방지를 위한 최대 검출 수 제한
                    if (MatchResults.Count > 50) break;
                }
            }

            IsInspected = true;
            // 볼트 검사 특화: 검출된 볼트가 정확히 2개일 때만 정상으로 판단
            IsDefect = (OutPoints.Count != 2);
            ResultString.Add($"검출 수: {OutPoints.Count}, 최고 점수: {OutScore}%");
            return true;
        }

        //------- 화면에 그릴 결과 사각형 정보 생성 -------
        public override int GetResultRect(out List<DrawInspectInfo> resultArea)
        {
            resultArea = new List<DrawInspectInfo>();
            if (!IsInspected || MatchResults.Count == 0) return 0;

            int w = _templateImages[0].Width;
            int h = _templateImages[0].Height;

            foreach (var res in MatchResults)
            {
                // 점수에 따라 합격(Good) / 불합격(Defect) 색상 결정
                DecisionType color = (res.Score >= MatchScore) ? DecisionType.Good : DecisionType.Defect;
                resultArea.Add(new DrawInspectInfo(new Rect(res.Center.X - w / 2, res.Center.Y - h / 2, w, h), $"{res.Score}%", InspectType.InspMatch, color));
            }
            return resultArea.Count;
        }

        //===== [그룹 5] 보정 및 데이터 활용 유틸리티 =====

        //------- 두 볼트의 중심점과 검사 영역 간의 오차 계산 -------
        public Point GetOffset()
        {
            // 볼트가 정확히 2개 검출되었을 때만 보정값 계산
            if (IsInspected && OutPoints.Count == 2)
            {
                // 두 볼트 사이의 정중앙 지점 계산
                Point centerOfBolts = new Point((OutPoints[0].X + OutPoints[1].X) / 2, (OutPoints[0].Y + OutPoints[1].Y) / 2);
                // 기준 영역(InspRect)의 시작점으로부터 얼마나 떨어져 있는지 반환
                return new Point(centerOfBolts.X - InspRect.X, centerOfBolts.Y - InspRect.Y);
            }
            return new Point(0, 0);
        }

        //------- 상하 볼트 쌍을 묶어 하나의 ROI 리스트로 반환 -------
        public List<Rect> GetBoltPairROIs()
        {
            var pairROIs = new List<Rect>();
            if (!IsInspected || MatchResults.Count < 2) return pairROIs;

            // 1. X축 좌표 기준으로 정렬 (왼쪽 라인 볼트부터)
            var sortedByX = MatchResults.OrderBy(b => b.Center.X).ToList();
            int xTolerance = 100; // 동일한 열(Column)로 판단할 X거리 허용치
            var xGroups = new List<List<MatchResult>>();

            // 2. 같은 라인(X좌표가 비슷한) 볼트끼리 그룹핑
            foreach (var res in sortedByX)
            {
                var targetGroup = xGroups.FirstOrDefault(g => Math.Abs(g[0].Center.X - res.Center.X) < xTolerance);
                if (targetGroup == null) xGroups.Add(new List<MatchResult> { res });
                else targetGroup.Add(res);
            }

            int w = _templateImages[0].Width;
            int h = _templateImages[0].Height;

            // 3. 각 그룹 내에서 상하(Y좌표) 볼트를 짝지어 하나의 영역으로 통합
            foreach (var group in xGroups)
            {
                var sortedInGroup = group.OrderBy(b => b.Center.Y).ToList();
                for (int i = 0; i < sortedInGroup.Count - 1; i += 2)
                {
                    var b1 = sortedInGroup[i];   // 위쪽 볼트
                    var b2 = sortedInGroup[i + 1]; // 아래쪽 볼트

                    int minX = Math.Min(b1.Center.X, b2.Center.X) - w / 2;
                    int maxX = Math.Max(b1.Center.X, b2.Center.X) + w / 2;
                    int minY = b1.Center.Y - h / 2;
                    int maxY = b2.Center.Y + h / 2;

                    // 두 볼트를 모두 포함하는 커다란 사각형 ROI 생성
                    pairROIs.Add(new Rect(minX, minY, maxX - minX, maxY - minY));
                }
            }
            return pairROIs;
        }
    }
}