using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JYVision.Algorithm;
using JYVision.Core;
using JYVision.Property;
using JYVision.Teach;
using OpenCvSharp;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision
{
    //===== 알고리즘 세부 속성 제어 및 UI 관리 폼 =====
    public partial class PropertiesForm : DockContent
    {
        //===== [그룹 1] 필드 및 초기화 =====

        // 생성된 속성 탭을 다시 활용하기 위한 캐시 딕셔너리
        Dictionary<string, TabPage> _allTabs = new Dictionary<string, TabPage>();

        //------- 폼 생성자 -------
        public PropertiesForm()
        {
            InitializeComponent();
        }

        //===== [그룹 2] 동적 탭 제어 로직 =====

        //------- 검사 타입에 맞는 속성 제어 탭 로드 (기존 탭 재사용 또는 신규 생성) -------
        private void LoadOptionControl(InspectType inspType)
        {
            string tabName = inspType.ToString();

            // 1. 현재 화면에 이미 해당 탭이 떠 있다면 리턴
            foreach (TabPage tabPage in tabPropControl.TabPages)
            {
                if (tabPage.Text == tabName) return;
            }

            // 2. 캐시(Dictionary)에 이미 생성된 탭이 있다면 화면에만 추가
            if (_allTabs.TryGetValue(tabName, out TabPage page))
            {
                tabPropControl.TabPages.Add(page);
                return;
            }

            // 3. 완전히 새로운 타입이라면 UserControl을 생성하여 탭 구성
            UserControl _inspProp = CreateUserControl(inspType);
            if (_inspProp == null) return;

            TabPage newTab = new TabPage(tabName)
            {
                Dock = DockStyle.Fill
            };
            _inspProp.Dock = DockStyle.Fill;
            newTab.Controls.Add(_inspProp);
            tabPropControl.TabPages.Add(newTab);
            tabPropControl.SelectedTab = newTab;

            // 캐시에 등록하여 다음번에 재활용
            _allTabs[tabName] = newTab;
        }

        //------- 검사 알고리즘별 전용 속성 UI(UserControl) 인스턴스 생성 -------
        private UserControl CreateUserControl(InspectType inspPropType)
        {
            UserControl curProp = null;
            switch (inspPropType)
            {
                case InspectType.InspMatch: // 템플릿 매칭 설정창
                    curProp = new MatchInspProp();
                    break;
                case InspectType.InspROI: // 실로폰(볼트) 테스트 도구창
                    curProp = new xylophone();
                    break;
                case InspectType.InspFilter: // 이미지 전처리 필터 설정창
                    curProp = new ImageFilterProp();
                    break;
                case InspectType.InspAIModule: // AI 딥러닝 모듈 설정창
                    curProp = new AIModuleProp();
                    break;
                default:
                    MessageBox.Show("등록되지 않은 알고리즘 속성입니다.");
                    return null;
            }
            return curProp;
        }

        //===== [그룹 3] 모델 데이터 연동 (Logic -> UI) =====

        //------- 선택된 윈도우의 알고리즘 구성에 따라 UI 탭 레이아웃 재구성 -------
        public void ShowProperty(InspWindow window)
        {
            ResetProperty(); // 기존에 떠 있던 탭들을 모두 정리

            foreach (InspAlgorithm algo in window.AlgorithmList)
            {
                // 기본 등록된 알고리즘 탭 로드
                LoadOptionControl(algo.InspectType);

                // [특수 로직] 볼트 매칭 알고리즘일 경우, 보조 도구인 xylophone 탭을 자동 추가
                if (algo.InspectType == InspectType.InspMatch)
                {
                    LoadOptionControl(InspectType.InspROI);
                }
            }
        }

        //------- 속성창 화면 클리어 -------
        public void ResetProperty()
        {
            tabPropControl.TabPages.Clear();
        }

        //------- 현재 열려 있는 속성창들에 알고리즘 실시간 데이터 업데이트 -------
        public void UpdateProperty(InspWindow window)
        {
            if (window == null) return;

            foreach (TabPage tabPage in tabPropControl.TabPages)
            {
                if (tabPage.Controls.Count == 0) continue;

                UserControl uc = tabPage.Controls[0] as UserControl;

                // 1. 템플릿 매칭 속성창 데이터 동기화
                if (uc is MatchInspProp matchProp)
                {
                    MatchAlgorithm matchAlgo = (MatchAlgorithm)window.FindInspAlgorithm(InspectType.InspMatch);
                    if (matchAlgo == null) continue;

                    window.PatternLearn(); // 티칭 이미지 유효성 확인 및 학습
                    matchProp.SetAlgorithm(matchAlgo);
                }
                // 2. 볼트(실로폰) 검사 보조 도구 데이터 동기화
                else if (uc is xylophone boltProp)
                {
                    // 필요 시 xylophone 내부 메서드 호출 (예: boltProp.SetAlgorithm(...))
                }
                // 3. 이미지 필터 속성창 데이터 동기화
                else if (uc is ImageFilterProp filterProp)
                {
                    // 필터 알고리즘 업데이트 로직
                }
                // 4. AI 모듈 속성창 데이터 동기화
                else if (uc is AIModuleProp aiModuleProp)
                {
                    // AI 모델 파라미터 업데이트 로직
                }
            }
        }
    }
}