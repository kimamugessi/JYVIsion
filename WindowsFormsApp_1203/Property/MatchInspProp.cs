using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JYVision.Algorithm;
using JYVision.Core;
using JYVision.UIControl;
using OpenCvSharp.Extensions;
using OpenCvSharp;

namespace JYVision.Property
{
    //===== 템플릿 매칭 알고리즘 속성 제어 UI 클래스 =====
    public partial class MatchInspProp : UserControl
    {
        //===== [그룹 1] 필드 및 초기화 =====

        public event EventHandler<EventArgs> PropertyChanged; // 속성 변경 시 외부(MainForm 등)에 알리기 위한 이벤트
        private MatchAlgorithm _matchAlgo = null; // 연결된 알고리즘 객체

        //------- UI 컨트롤 초기화 및 기본 이벤트 연동 -------
        public MatchInspProp()
        {
            InitializeComponent();

            // 텍스트 박스에서 포커스가 나갈 때(Leave) 값을 업데이트하도록 이벤트 연결
            txtExtendX.Leave += OnUpdateValue;
            txtExtendY.Leave += OnUpdateValue;
            txtScore.Leave += OnUpdateValue;

            // 패턴 이미지 에디터(등록/수정/삭제) 버튼 이벤트 연결
            patternImageEditor.ButtonChanged += PatternImage_ButtonChanged;
        }

        //===== [그룹 2] 알고리즘 데이터 연동 (Logic -> UI) =====

        //------- 외부(MainForm 등)에서 선택된 알고리즘 객체를 UI 컨트롤에 연결 -------
        public void SetAlgorithm(MatchAlgorithm matchAlgo)
        {
            _matchAlgo = matchAlgo;
            SetProperty(); // 객체 받자마자 화면에 값 뿌려줌
        }

        //------- 연결된 알고리즘의 현재 설정값들을 읽어와서 UI 화면에 그리기 -------
        public void SetProperty()
        {
            if (_matchAlgo is null) return;

            // 1. 기본 사용 여부 및 수치 셋팅
            chkUse.Checked = _matchAlgo.IsUse;

            OpenCvSharp.Size extendSize = _matchAlgo.ExtSize;
            txtExtendX.Text = extendSize.Width.ToString();
            txtExtendY.Text = extendSize.Height.ToString();
            txtScore.Text = _matchAlgo.MatchScore.ToString();

            // 2. 결과 반전 여부 셋팅
            chkInvertResult.Checked = _matchAlgo.InvertResult;

            // 3. 등록된 템플릿(마스터) 이미지들을 섬네일로 표시
            List<Mat> templateImages = _matchAlgo.GetTemplateImages();
            List<Bitmap> teachImages = new List<Bitmap>();

            // 마지막 남은 이미지 1개를 삭제했을 때 잔상이 남지 않도록 무조건 썸네일 갱신 호출
            foreach (var teachImage in templateImages)
            {
                // UI 출력을 위해 OpenCV Mat 타입을 WinForms용 Bitmap으로 변환
                Bitmap bmpImage = BitmapConverter.ToBitmap(teachImage);
                teachImages.Add(bmpImage);
            }

            // UI 컨트롤에 섬네일 리스트 전달하여 그리기 (이 안에서 기존 Bitmap Dispose 처리 필수)
            patternImageEditor.DrawThumbnails(teachImages);
        }

        //===== [그룹 3] UI 변경 사항 반영 (UI -> Logic) =====

        //------- 사용자가 텍스트박스에 값을 입력/수정했을 때 알고리즘 객체에 반영 -------
        private void OnUpdateValue(object sender, EventArgs e)
        {
            if (_matchAlgo == null) return;

            OpenCvSharp.Size extendSize = _matchAlgo.ExtSize;
            int score;

            // 입력값 검증: 숫자가 아니거나 음수면 에러 메시지 띄우고 기존 정상 값으로 원복
            if (!int.TryParse(txtExtendX.Text, out extendSize.Width) || extendSize.Width < 0)
            {
                MessageBox.Show("0 이상의 숫자만 입력 가능합니다.");
                txtExtendX.Text = _matchAlgo.ExtSize.Width.ToString();
                return;
            }

            if (!int.TryParse(txtExtendY.Text, out extendSize.Height) || extendSize.Height < 0)
            {
                MessageBox.Show("0 이상의 숫자만 입력 가능합니다.");
                txtExtendY.Text = _matchAlgo.ExtSize.Height.ToString();
                return;
            }

            if (!int.TryParse(txtScore.Text, out score) || score < 0 || score > 100)
            {
                MessageBox.Show("0~100 사이의 점수만 입력 가능합니다.");
                txtScore.Text = _matchAlgo.MatchScore.ToString();
                return;
            }

            // 알고리즘 객체에 최종 검증된 값 반영
            _matchAlgo.ExtSize = extendSize;
            _matchAlgo.MatchScore = score;

            // 값이 바뀌었음을 외부에 알림 (저장 버튼 활성화 등에 사용)
            PropertyChanged?.Invoke(this, null);
        }

        //------- 매칭 검사 사용 여부 체크박스 변경 이벤트 -------
        private void chkUse_CheckedChanged(object sender, EventArgs e)
        {
            bool useMatch = chkUse.Checked;

            // 사용 안 함 상태면 하위 설정창들을 비활성화해서 실수 방지
            grpMatch.Enabled = useMatch;
            patternImageEditor.Enabled = useMatch;

            if (_matchAlgo != null)
                _matchAlgo.IsUse = useMatch;
        }

        //------- 검사 결과(양불) 반전 체크박스 변경 이벤트 -------
        private void chkInvertResult_CheckedChanged(object sender, EventArgs e)
        {
            if (_matchAlgo is null) return;
            _matchAlgo.InvertResult = chkInvertResult.Checked;
        }

        //===== [그룹 4] 패턴 이미지 관리 핸들러 =====

        //------- 템플릿(마스터) 이미지 추가/수정/삭제 버튼 클릭 이벤트 -------
        private void PatternImage_ButtonChanged(object sender, PatternImageEventArgs e)
        {
            int index = e.Index; // 클릭된 이미지의 인덱스

            // 각 버튼 타입에 따라 글로벌 스테이지의 티칭 이미지 관리 메서드 호출
            switch (e.Button)
            {
                case PatternImageButton.UpdateImage:
                    // 기존 이미지 교체
                    Global.Inst.InspStage.UpdateTeachingImage(index);
                    break;
                case PatternImageButton.AddImage:
                    // 새 이미지 추가 (인덱스 -1 전달)
                    Global.Inst.InspStage.UpdateTeachingImage(-1);
                    break;
                case PatternImageButton.DelImage:
                    // 이미지 삭제
                    Global.Inst.InspStage.DelTeachingImage(index);
                    break;
            }

            // 이미지 조작 후 UI 썸네일 즉각 갱신
            SetProperty();
        }
    }
}