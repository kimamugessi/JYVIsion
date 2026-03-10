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

        public int MatchCount { get; set; } = 2; // 찾고자 하는 대상의 개수 (기본값 2개)
        public int ColumnTolerance { get; set; } = 100; // 동일한 열(Column)로 판단할 X거리 허용치

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
            cloneAlgo.ColumnTolerance = this.ColumnTolerance;

            foreach (var img in this._templateImages)
            {
                cloneAlgo.AddTemplateImage(img);
            }

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
            this.ColumnTolerance = matchAlgo.ColumnTolerance;

            this.ResetTemplateImages();
            foreach (var img in matchAlgo.GetTemplateImages())
            {
                this.AddTemplateImage(img);
            }

            return true;
        }

        //===== [그룹 3] 템플릿(마스터) 이미지 관리 =====

        //------- 템플릿 이미지 등록 -------
        public void AddTemplateImage(Mat templateImage) => _templateImages.Add(templateImage.Clone());

        //------- 템플릿 리스트 초기화 -------
        public void ResetTemplateImages()
        {
            foreach (var mat in _templateImages)
            {
                mat?.Dispose(); // 메모리 누수 방지
            }
            _templateImages.Clear();
        }

        //------- 등록된 템플릿 목록 반환 -------
        public List<Mat> GetTemplateImages() => _templateImages;

        //===== [그룹 4] 검사 실행 및 결과 처리 =====

        //------- 메인 템플릿 매칭 실행 -------
        public override bool DoInspect()
        {
            if (_srcImage == null || _templateImages.Count == 0) return false;

            // 이전 검사 결과 데이터 비우기
            ResetResult();
            OutPoints.Clear();
            MatchResults.Clear();
            OutScore = 0;

            Mat template = _templateImages[0]; // 첫 번째 마스터 이미지 기준
            if (template == null || template.Empty()) return false;

            using (Mat res = new Mat())
            {
                // OpenCV의 MatchTemplate 실행 (정규화된 상관계수 매칭 방식)
                Cv2.MatchTemplate(_srcImage, template, res, TemplateMatchModes.CCoeffNormed);

                float matchThreshold = MatchScore / 100.0f; // 0.0 ~ 1.0 사이 값 변환
                int halfWidth = template.Width / 2;
                int halfHeight = template.Height / 2;

                // 다중 검출 루프
                while (true)
                {
                    double minVal, maxVal;
                    Point minLoc, maxLoc;

                    // 매칭 결과 맵에서 최대값(MaxVal)과 위치 찾기
                    Cv2.MinMaxLoc(res, out minVal, out maxVal, out minLoc, out maxLoc);

                    // 기준치 미달 시 종료
                    if (maxVal < matchThreshold) break;

                    // 결과 데이터 저장
                    Point center = new Point(maxLoc.X + halfWidth, maxLoc.Y + halfHeight);
                    MatchResult resData = new MatchResult { Center = center, Score = (int)(maxVal * 100) };

                    MatchResults.Add(resData);
                    OutPoints.Add(resData.Center);
                    if (resData.Score > OutScore) OutScore = resData.Score;

                    // 중복 검출 방지: 찾은 지점 주변을 0(검정)으로 마스킹
                    Cv2.Rectangle(res, new Rect(maxLoc.X - halfWidth, maxLoc.Y - halfHeight, template.Width, template.Height), new Scalar(0), -1);

                    // 최대 검출 개수를 초과하면 루프 종료 (안전장치)
                    if (MatchResults.Count >= MatchCount * 5) break;
                }
            }

            IsInspected = true;
            // 설정한 타겟 개수(MatchCount)와 일치할 때만 정상 판단
            IsDefect = (OutPoints.Count != MatchCount);
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
    }
}