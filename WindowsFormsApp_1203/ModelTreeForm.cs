using JYVision.Core;
using JYVision.Teach;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision
{
    //===== 모델 구성 요소 트리 관리 폼 =====
    public partial class ModelTreeForm : DockContent
    {
        //===== [그룹 1] 필드 및 초기화 =====

        private ContextMenuStrip _contextMenu; // 트리 노드 우클릭 팝업 메뉴

        //------- 생성자: 트리 기본 노드 생성 및 컨텍스트 메뉴 초기화 -------
        public ModelTreeForm()
        {
            InitializeComponent();

            // 초기 트리 루트 노드 설정
            tvModelTree.Nodes.Add("ROI");

            // InspWindowType 열거형을 기반으로 추가 메뉴 항목 구성
            _contextMenu = new ContextMenuStrip();
            List<InspWindowType> windowTypeList = Enum.GetValues(typeof(InspWindowType)).Cast<InspWindowType>().ToList();

            foreach (InspWindowType windowType in windowTypeList)
            {
                var item = new ToolStripMenuItem(windowType.ToString(), null, AddNode_Click);
                item.Tag = windowType;
                _contextMenu.Items.Add(item);
            }
        }

        //===== [그룹 2] 트리뷰 이벤트 핸들러 =====

        //------- 트리뷰 마우스 다운 이벤트: 우클릭 시 ROI 추가 메뉴 표시 -------
        private void tvModelTree_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                TreeNode clickedNode = tvModelTree.GetNodeAt(e.X, e.Y);

                // "ROI" 루트 노드를 클릭했을 때만 메뉴 표시
                if (clickedNode != null && clickedNode.Text == "ROI")
                {
                    tvModelTree.SelectedNode = clickedNode;
                    _contextMenu.Show(tvModelTree, e.Location);
                }
            }
        }

        //------- 메뉴 아이템 클릭 이벤트: 선택된 타입의 ROI 추가 로직 실행 -------
        private void AddNode_Click(object sender, EventArgs e)
        {
            if (tvModelTree.SelectedNode != null && sender is ToolStripMenuItem menuItem)
            {
                InspWindowType windowType = (InspWindowType)menuItem.Tag;
                AddNewROI(windowType);
            }
        }

        //===== [그룹 3] ROI 조작 및 외부 연동 =====

        //------- CameraForm에 새로운 ROI 추가 명령 전달 -------
        private void AddNewROI(InspWindowType inspWindowType)
        {
            CameraForm cameraForm = MainForm.GetDockForm<CameraForm>();
            if (cameraForm != null)
            {
                cameraForm.AddRoi(inspWindowType);
            }
        }

        //------- 현재 모델의 모든 검사 윈도우 정보를 트리 노드에 갱신 -------
        public void UpdateDiagramEntity()
        {
            tvModelTree.Nodes.Clear();
            TreeNode ROINode = tvModelTree.Nodes.Add("ROI");

            Model model = Global.Inst.InspStage.CurModel;
            if (model == null || model.InspWindowList.Count <= 0)
                return;

            // 모델에 등록된 윈도우 리스트를 순회하며 하위 노드 생성
            foreach (InspWindow window in model.InspWindowList)
            {
                if (window == null) continue;

                TreeNode node = new TreeNode(window.UID);
                ROINode.Nodes.Add(node);
            }

            // 시인성을 위해 트리 전체 확장
            tvModelTree.ExpandAll();
        }
    }
}