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
using OpenCvSharp;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision
{
    public enum PropertyType    //속성창에서 사용할 타입 선언
    {
        Binary,
        Filter,
        AIModule
    }
    public partial class PropertiesForm : DockContent
    {
        //속성탭을 관리하기 위한 딕셔너리
        Dictionary<string, TabPage> _allTabs = new Dictionary<string, TabPage>();
        public PropertiesForm()
        {
            InitializeComponent();

            LoadOptionControl(PropertyType.Filter); //속성 속 텝들 초기화
            LoadOptionControl(PropertyType.Binary);
            LoadOptionControl(PropertyType.AIModule);
        }
        private void LoadOptionControl(PropertyType propType)   //속성 탭이 이미 있다면 그것을 반환(1), 없다면 새로 생성(2)
        {
            string tabName = propType.ToString();

            foreach (TabPage tabPage in tabPropControl.TabPages)    //(1)
            {
                if (tabPage.Text == tabName) return;
            }

            if (_allTabs.TryGetValue(tabName, out TabPage page))    //딕셔너리에 있으면 추가
            {
                tabPropControl.TabPages.Add(page);
                return;
            }

            UserControl _inspProp = CreateUserControl(propType);    //새 UserControl 생성
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

        private UserControl CreateUserControl(PropertyType propType)    //속성 탭 생성하는 매서드
        {
            UserControl curProp = null;
            switch (propType)
            {
                case PropertyType.Binary:
                    BinaryProp blobProp = new BinaryProp();

                    blobProp.RangeChanged += RangeSlider_RangeChaged;
                    blobProp.PropertyChanged += PropertyChanged;
                    curProp = blobProp;
                    break;
                case PropertyType.Filter:
                    ImageFilterProp filterProp = new ImageFilterProp();
                    curProp = filterProp;
                    break;
                case PropertyType.AIModule:
                    AIModuleProp aiModuleProp = new AIModuleProp();
                    curProp = aiModuleProp;
                    break;
                default:
                    MessageBox.Show("유효하지 않은 옵션입니다.");
                    return null;
            }
            return curProp;
        }
        public void UpdateProperty(BlobAlgorithm blobAlgorithm)
        {
            if (blobAlgorithm == null) return;
            foreach (TabPage tabPage in tabPropControl.TabPages)
            {
                if (tabPage.Controls.Count > 0)
                {
                    UserControl uc = tabPage.Controls[0] as UserControl;
                    if (uc is BinaryProp binaryProp) binaryProp.SetAlgorithm(blobAlgorithm);
                }
            }
        }
        private void RangeSlider_RangeChaged(object sender, RangeChangedEventArgs e)
        {
            int lowerValue = e.LowerValue;
            int upperValue = e.UpperValue;
            bool invert = e.Invert;
            ShowBinaryMode showBinaryMode = e.ShowBinMode;
            Global.Inst.InspStage.PreView?.SetBinary(lowerValue, upperValue, invert, showBinaryMode);
        }
        private void PropertyChanged(object sender, EventArgs e)
        {
            Global.Inst.InspStage.RedrawMainView();
        }
    }
}
