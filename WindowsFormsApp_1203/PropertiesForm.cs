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
    public partial class PropertiesForm : DockContent
    {
        //속성탭을 관리하기 위한 딕셔너리
        Dictionary<string, TabPage> _allTabs = new Dictionary<string, TabPage>();
        public PropertiesForm()
        {
            InitializeComponent();
        }
        private void LoadOptionControl(InspectType inspType)   //속성 탭이 이미 있다면 그것을 반환(1), 없다면 새로 생성(2)
        {
            string tabName = inspType.ToString();

            foreach (TabPage tabPage in tabPropControl.TabPages)    //(1)
            {
                if (tabPage.Text == tabName) return;
            }

            if (_allTabs.TryGetValue(tabName, out TabPage page))    //딕셔너리에 있으면 추가
            {
                tabPropControl.TabPages.Add(page);
                return;
            }

            UserControl _inspProp = CreateUserControl(inspType);    //새 UserControl 생성
            if (_inspProp == null) return;

            TabPage newTab = new TabPage(tabName)     //(2)
            {
                Dock = DockStyle.Fill
            };
            _inspProp.Dock = DockStyle.Fill;
            newTab.Controls.Add(_inspProp);
            tabPropControl.TabPages.Add(newTab);
            tabPropControl.SelectedTab = newTab;    //새 탭 선택

            _allTabs[tabName] = newTab;
        }

        private UserControl CreateUserControl(InspectType inspPropType)    //속성 탭 생성하는 매서드
        {
            UserControl curProp = null;
            switch (inspPropType)
            {
                case InspectType.InspBinary:
                    BinaryProp blobProp = new BinaryProp();

                    blobProp.RangeChanged += RangeSlider_RangeChaged;
                    //blobProp.PropertyChanged += PropertyChanged;
                    blobProp.ImageChannelChanged += ImageChannelchaged;
                    curProp = blobProp;
                    break;
                case InspectType.InspMatch:
                    MatchInspProp matchProp = new MatchInspProp();
                    //matchProp.PropertyChanged += PropertyChanged;
                    curProp = matchProp;
                    break;
                case InspectType.InspBoltROI:
                    xylophone xylophone = new xylophone();
                    // 필요한 이벤트 연결이 있다면 여기에 작성 (예: xylophone.PropertyChanged += ...)
                    curProp = xylophone;
                    break;
                case InspectType.InspFilter:
                    ImageFilterProp filterProp = new ImageFilterProp();
                    curProp = filterProp;
                    break;
                case InspectType.InspAIModule:
                    AIModuleProp aiModuleProp = new AIModuleProp();
                    curProp = aiModuleProp;
                    break;
                default:
                    MessageBox.Show("유효하지 않은 옵션입니다.");
                    return null;
            }
            return curProp;
        }

        public void ShowProperty(InspWindow window)
        {
            foreach (InspAlgorithm algo in window.AlgorithmList)
            {
                // 기본 알고리즘 탭 생성
                LoadOptionControl(algo.InspectType);

                // 💡 만약 Match(볼트) 알고리즘이 있다면 xylophone 탭도 추가로 생성
                if (algo.InspectType == InspectType.InspMatch)
                {
                    LoadOptionControl(InspectType.InspBoltROI);
                }
            }
        }

        public void ResetProperty() {  tabPropControl.TabPages.Clear(); }
        public void UpdateProperty(InspWindow window)
        {
            if (window == null) return;

            foreach (TabPage tabPage in tabPropControl.TabPages)
            {
                if (tabPage.Controls.Count > 0)
                {
                    UserControl uc = tabPage.Controls[0] as UserControl;

                    // 1. 이진화(Binary) 속성창 업데이트
                    if (uc is BinaryProp binaryProp)
                    {
                        BlobAlgorithm blobAlgo = (BlobAlgorithm)window.FindInspAlgorithm(InspectType.InspBinary);
                        if (blobAlgo == null) continue;

                        binaryProp.SetAlgorithm(blobAlgo);
                    }
                    // 2. 매칭(Match/Bolt) 속성창 업데이트
                    else if (uc is MatchInspProp matchProp)
                    {
                        MatchAlgorithm matchAlgo = (MatchAlgorithm)window.FindInspAlgorithm(InspectType.InspMatch);
                        if (matchAlgo == null) continue;

                        // 패턴 매칭 알고리즘은 화면 갱신 전 티칭 데이터 확인을 위해 호출
                        window.PatternLearn();
                        matchProp.SetAlgorithm(matchAlgo);
                    }
                    // 3. ✨ 볼트 ROI 테스트 속성창 업데이트 (추가된 부분)
                    else if (uc is xylophone boltProp)
                    {
                        // xylophone은 버튼 동작을 위해 MatchAlgorithm 정보가 필요할 수 있습니다.
                        MatchAlgorithm matchAlgo = (MatchAlgorithm)window.FindInspAlgorithm(InspectType.InspMatch);
                        if (matchAlgo == null) continue;

                        // 만약 xylophone 내부에 SetAlgorithm(matchAlgo) 메서드를 만드셨다면 호출하세요.
                        // boltProp.SetAlgorithm(matchAlgo); 
                    }
                    // 4. 이미지 필터 속성창 업데이트
                    else if (uc is ImageFilterProp filterProp)
                    {
                        // Filter 알고리즘 업데이트 로직 (필요시 추가)
                    }
                    // 5. AI 모듈 속성창 업데이트
                    else if (uc is AIModuleProp aiModuleProp)
                    {
                        // AI 알고리즘 업데이트 로직 (필요시 추가)
                    }
                }
            }
        }
        private void RangeSlider_RangeChaged(object sender, RangeChangedEventArgs e)
        {
            int lowerValue = e.LowerValue;
            int upperValue = e.UpperValue;
            bool invert = e.Invert;
            ShowBinaryMode showBinMode = e.ShowBinMode;
            Global.Inst.InspStage.PreView?.SetBinary(lowerValue, upperValue, invert, showBinMode);
        }

        //private void PropertyChanged(object sender, EventArgs e)
        //{
        //    Global.Inst.InspStage.RedrawMainView();
        //}
        private void ImageChannelchaged(object sender, ImageChannelEventArgs e)
        {
            Global.Inst.InspStage.SetPreviewImage(e.Channel);
        }
    }
}
