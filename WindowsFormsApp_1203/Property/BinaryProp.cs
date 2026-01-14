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
using System.IO;

namespace JYVision.Property
{
    //#7_BINARY_PREVIEW#6 이진화 검사를 위한 속성창 구현
    /*
    1) UIControl / RangeTracbar 유저콘트롤 생성 후, 내부 코드만 복사&붙여넣기
    2) 이진화 속성창 디자인
    3) BinaryProp 클래스 구현
     */

    public enum ShowBinaryMode : int
    {
        ShowBinaryNone = 0,             //이진화 하이라이트 끄기
        ShowBinaryHighlightRed,         //Red 하이라이트 보기
        ShowBinaryHighlightGreen,         //Green 하이라이트 보기
        ShowBinaryHighlightBlue,         //Blue 하이라이트 보기
        ShowBinaryOnly                  //배경 없이 이진화 이미지만 보기
    }

    public partial class BinaryProp: UserControl
    {
        //속성창의 값이 변경시 발생하는 이벤트
        //public event EventHandler<EventArgs> PropertyChanged;
        //양방향 슬라이더 값 변경시 발생하는 이벤트
        public event EventHandler<RangeChangedEventArgs> RangeChanged;

        BlobAlgorithm _blobAlgo = null;

        // 속성값을 이용하여 이진화 임계값 설정
        public int LeftValue => binRangeTrackbar.ValueLeft;
        public int RightValue => binRangeTrackbar.ValueRight;

        private bool _updateDataGridView =true;
        private readonly int COL_USE = 1;
        private readonly int COL_MIN = 2;
        private readonly int COL_MAX = 3;

        public BinaryProp()
        {
            InitializeComponent();
            
            cbBinMethod.DataSource=Enum.GetValues(typeof(BinaryMethod)).Cast<BinaryMethod>().ToList();
            cbBinMethod.SelectedIndex = (int)BinaryMethod.Feature;

            InitializeFilterDataGridView();

            // TrackBar 초기 설정
            binRangeTrackbar.RangeChanged += Range_RangeChanged;

            binRangeTrackbar.ValueLeft = 0;
            binRangeTrackbar.ValueRight = 128;

            //이진화 프리뷰 콤보박스 초기화 설정
            cbHighlight.Items.Add("사용안함");
            cbHighlight.Items.Add("빨간색");
            cbHighlight.Items.Add("녹색");
            cbHighlight.Items.Add("파란색");
            cbHighlight.Items.Add("흑백");
            cbHighlight.SelectedIndex = 0; // 기본값으로 "사용안함" 선택
        }
        private void InitializeFilterDataGridView()
        {
            // 컬럼 설정
            dataGridViewFilter.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "필터명",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 70
            });

            dataGridViewFilter.Columns.Add(new DataGridViewCheckBoxColumn()
            {
                HeaderText = "사용",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 40
            });

            dataGridViewFilter.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "최소값",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 65
            });

            dataGridViewFilter.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "최대값",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 65
            });

            // 항목 추가
            AddFilterRow("Area");
            AddFilterRow("Length");
            AddFilterRow("Width");
            AddFilterRow("Count");

            dataGridViewFilter.AllowUserToAddRows = false;
            dataGridViewFilter.RowHeadersVisible = false;
            dataGridViewFilter.AllowUserToResizeColumns = false;
            dataGridViewFilter.AllowUserToResizeRows = false;
            dataGridViewFilter.AllowUserToOrderColumns = false;
            dataGridViewFilter.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        }

        private void AddFilterRow(string itemName)
        {
            dataGridViewFilter.Rows.Add(itemName, false, "", "");
        }

        public void SetAlgorithm(BlobAlgorithm blobAlgo)
        {
            _blobAlgo = blobAlgo;

            if (_blobAlgo.BlobFilters.Count <= 0) blobAlgo.SetDefault();

            SetProperty();
        }

  
        public void SetProperty()
        {
            if (_blobAlgo == null)
                return;

            chkUse.Checked = _blobAlgo.IsUse;

            BinaryThreshold threshold = _blobAlgo.BinThreshold;

            if (threshold.invert)
            {
                binRangeTrackbar.SetThreshold(threshold.upper, threshold.lower);
            }
            else
            {
                binRangeTrackbar.SetThreshold(threshold.lower, threshold.upper);
            }
            
            cbBinMethod.SelectedIndex=(int)_blobAlgo.BinMethod;

            UpdateDataGridView(true);
            chkRotatedRect.Checked = _blobAlgo.UseRotatedRect;
        }


        public void GetProperty()
        {
            if (_blobAlgo == null)
                return;

            _blobAlgo.IsUse = chkUse.Checked;

            BinaryThreshold threshold = new BinaryThreshold();

            int leftValue = LeftValue;
            int rightValue = RightValue;

            if (leftValue < rightValue)
            {
                threshold.lower = leftValue;
                threshold.upper = rightValue;
                threshold.invert = false;
            }
            else
            {
                threshold.lower = rightValue;
                threshold.upper = leftValue;
                threshold.invert = true;
            }

            _blobAlgo.BinThreshold = threshold;

            UpdateDataGridView(false);
        }

        private void UpdateDataGridView(bool update)
        {
            if (_blobAlgo == null) return;

            if (update)
            {
                _updateDataGridView = false;
                List<BlobFilter> blobFilters = _blobAlgo.BlobFilters;

                for (int i = 0; i < blobFilters.Count; i++)
                {
                    if (i >= dataGridViewFilter.Rows.Count)
                        break;

                    dataGridViewFilter.Rows[i].Cells[COL_USE].Value = blobFilters[i].isUse;
                    dataGridViewFilter.Rows[i].Cells[COL_MIN].Value = blobFilters[i].min;
                    dataGridViewFilter.Rows[i].Cells[COL_MAX].Value = blobFilters[i].max;
                }
                _updateDataGridView = true;
            }
            else
            {
                if (_updateDataGridView == false)
                    return;

                List<BlobFilter> blobFilters = _blobAlgo.BlobFilters;

                for (int i = 0; i < blobFilters.Count; i++)
                {
                    BlobFilter blobFilter = blobFilters[i];
                    blobFilter.isUse = (bool)dataGridViewFilter.Rows[i].Cells[COL_USE].Value;

                    object value = dataGridViewFilter.Rows[i].Cells[COL_MIN].Value;

                    int min = 0;
                    if (value != null && int.TryParse(value.ToString(), out min))
                        blobFilter.min = min;

                    value = dataGridViewFilter.Rows[i].Cells[COL_MAX].Value;

                    int max = 0;
                    if (value != null && int.TryParse(value.ToString(), out max))
                        blobFilter.max = max;
                }
            }
        }

        //이진화 옵션을 선택할때마다, 이진화 이미지가 갱신되도록 하는 함수
        private void UpdateBinary()
        {
            GetProperty();

            int leftValue = LeftValue;
            int rightValue = RightValue;
            bool invert = false;

            if (leftValue > rightValue)
            {
                leftValue = RightValue;
                rightValue = LeftValue;
                invert = true;
            }

            ShowBinaryMode showBinaryMode = (ShowBinaryMode)cbHighlight.SelectedIndex;
            RangeChanged?.Invoke(this, new RangeChangedEventArgs(leftValue, rightValue, invert, showBinaryMode));
        }

        //GUI 이벤트와 UpdateBinary함수 연동
        private void Range_RangeChanged(object sender, EventArgs e)
        {
            UpdateBinary();
        }

        private void chkUse_CheckedChanged(object sender, EventArgs e)
        {
            bool useBinary = chkUse.Checked;
            grpBinary.Enabled = useBinary;

            dataGridViewFilter.Enabled = useBinary;

            GetProperty();
        }

        //콤보박스 변경시 이진화 프리뷰 갱신
        private void cbHighlight_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateBinary();
        }

        private void dataGridViewFilter_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_updateDataGridView == true)
                UpdateDataGridView(false);
        }

        // DataGridView안에 있는 체크박스의 경우, CellValueChanged가 발생하지 않아,
        // CellDirtyStateChanged 이벤트를 사용하여 체크박스의 상태가 변경될 때 CommitEdit을 호출합니다.
        private void dataGridViewFilter_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dataGridViewFilter.CurrentCell is DataGridViewCheckBoxCell)
            {
                dataGridViewFilter.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void chkRotatedRect_CheckedChanged(object sender, EventArgs e)
        {
            if (_blobAlgo == null)
                return;

            _blobAlgo.UseRotatedRect = chkRotatedRect.Checked;
        }

        private void cbBinMethod_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blobAlgo == null)
                return;

            _blobAlgo.BinMethod = (BinaryMethod)cbBinMethod.SelectedIndex;
            chkRotatedRect.Enabled = _blobAlgo.BinMethod == BinaryMethod.Feature;

            if (_blobAlgo.BinMethod == BinaryMethod.PixelCount)
            {
                for (int i = 0; i < dataGridViewFilter.Rows.Count; i++)
                {
                    bool useFeature = i == 0 ? true : false; // Area 필터만 사용 가능
                    dataGridViewFilter.Rows[i].Cells[COL_USE].Value = useFeature;
                }
                dataGridViewFilter.Columns[COL_USE].ReadOnly = true;
            }
            else
            {
                dataGridViewFilter.Columns[COL_USE].ReadOnly = false;
            }

            _updateDataGridView = true;
        }

    }

    //이진화 관련 이벤트 발생시, 전달할 값 추가
    public class RangeChangedEventArgs : EventArgs
    {
        public int LowerValue { get; }
        public int UpperValue { get; }
        public bool Invert { get; }
        public ShowBinaryMode ShowBinMode { get; }

        public RangeChangedEventArgs(int lowerValue, int upperValue, bool invert, ShowBinaryMode showBinaryMode)
        {
            LowerValue = lowerValue;
            UpperValue = upperValue;
            Invert = invert;
            ShowBinMode = showBinaryMode;
        }
    }
}
