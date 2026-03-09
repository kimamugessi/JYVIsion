using BrightIdeasSoftware;
using JYVision.Core;
using JYVision.Inspect;
using JYVision.Teach;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

using Size = System.Drawing.Size;
using Point = System.Drawing.Point;

namespace JYVision
{
    // ===== 누적 행 데이터 모델 =====
    public class InspSummaryRow
    {
        public int No { get; set; }
        public string Time { get; set; }
        public int BoltNg { get; set; }
        public int MarkNg { get; set; }
        public int TotalNg => BoltNg + MarkNg;
        public string Status => TotalNg > 0 ? "NG" : "OK";
        public Bitmap Thumbnail { get; set; }
        public Bitmap PreviewImage { get; set; }
    }

    public partial class ResultForm : DockContent
    {
        private Panel _topPanel;
        private Label _lblTotal;
        private Button _btnClear;
        private SplitContainer _split;
        private ObjectListView _listView;
        private ImageList _imgList;

        private Panel _detailPanel;
        private PictureBox _picPreview;
        private Label _lblBoltNg;
        private Label _lblMarkNg;
        private Label _lblTotalNg;
        private Label _lblStatus;

        private readonly List<InspSummaryRow> _rows = new List<InspSummaryRow>();
        private int _runCount = 0;

        public ResultForm()
        {
            InitializeComponent();
            InitResultLayout();
        }

        private void InitResultLayout()
        {
            // ── 상단 버튼바 ──────────────────────────────────
            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(45, 45, 48)
            };

            _btnClear = new Button
            {
                Text = "Clear",
                Width = 70,
                Height = 26,
                Location = new Point(6, 5),
                BackColor = Color.FromArgb(80, 80, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnClear.FlatAppearance.BorderColor = Color.Gray;
            _btnClear.Click += (s, e) => ClearAll();

            _lblTotal = new Label
            {
                AutoSize = false,
                Width = 340,
                Height = 26,
                Location = new Point(86, 5),
                ForeColor = Color.White,
                Font = new Font("Arial", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "총 0건  |  OK: 0  |  NG: 0"
            };

            _topPanel.Controls.AddRange(new Control[] { _btnClear, _lblTotal });

            // ── SplitContainer ──────────────────────────────
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 83,
                Panel2MinSize = 50
            };
            _split.SizeChanged += (s, e) =>
            {
                if (_split.Width > 100 && _split.SplitterDistance < 50)
                    _split.SplitterDistance = (int)(_split.Width * 0.6);
            };

            // ── ImageList ───────────────────────────────────
            _imgList = new ImageList
            {
                ImageSize = new Size(80, 60),
                ColorDepth = ColorDepth.Depth32Bit
            };

            // ── ObjectListView ──────────────────────────────
            _listView = new ObjectListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                ShowGroups = false,
                GridLines = true,
                RowHeight = 64,
                SmallImageList = _imgList,
                UseAlternatingBackColors = true,
                AlternateRowBackColor = Color.FromArgb(245, 245, 255),
                MultiSelect = false,
                HideSelection = false
            };

            var colThumb = new OLVColumn("이미지", "")
            {
                Width = 75,
                IsEditable = false,
                TextAlign = HorizontalAlignment.Center,
                AspectGetter = _ => "",
                ImageGetter = obj =>
                {
                    if (obj is InspSummaryRow row && row.Thumbnail != null)
                    {
                        string key = $"row_{row.No}";
                        if (!_imgList.Images.ContainsKey(key))
                            _imgList.Images.Add(key, row.Thumbnail);
                        return key;
                    }
                    return null;
                }
            };
            var colNo = new OLVColumn("No", nameof(InspSummaryRow.No)) { Width = 40, TextAlign = HorizontalAlignment.Center, IsEditable = false };
            var colTime = new OLVColumn("시간", nameof(InspSummaryRow.Time)) { Width = 75, TextAlign = HorizontalAlignment.Center, IsEditable = false };
            var colBolt = new OLVColumn("Bolt NG", nameof(InspSummaryRow.BoltNg)) { Width = 65, TextAlign = HorizontalAlignment.Center, IsEditable = false };
            var colMark = new OLVColumn("Mark NG", nameof(InspSummaryRow.MarkNg)) { Width = 65, TextAlign = HorizontalAlignment.Center, IsEditable = false };
            var colStatus = new OLVColumn("판정", nameof(InspSummaryRow.Status)) { Width = 55, TextAlign = HorizontalAlignment.Center, IsEditable = false };

            _listView.Columns.AddRange(new OLVColumn[] { colThumb, colNo, colTime, colBolt, colMark, colStatus });

            _listView.RowFormatter = item =>
            {
                if (item.RowObject is InspSummaryRow r && r.TotalNg > 0)
                {
                    item.ForeColor = Color.Red;
                    item.Font = new Font(_listView.Font, FontStyle.Bold);
                }
            };

            _listView.SelectionChanged += OnRowSelected;
            _split.Panel1.Controls.Add(_listView);

            // ── 상세 패널 ───────────────────────────────────
            _detailPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 35),
                Padding = new Padding(8)
            };

            _picPreview = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = 150,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            Panel labelPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            Label MakeLabel(string text, int y, Color color) => new Label
            {
                AutoSize = true,
                Location = new Point(15, y),
                ForeColor = color,
                Font = new Font("Arial", 11, FontStyle.Bold),
                BackColor = Color.Transparent,
                Text = text
            };

            _lblBoltNg = MakeLabel("Bolt NG  : -", 20, Color.OrangeRed);
            _lblMarkNg = MakeLabel("Mark NG  : -", 60, Color.OrangeRed);
            _lblTotalNg = MakeLabel("Total NG : -", 100, Color.Yellow);
            _lblStatus = MakeLabel("판  정   : -", 140, Color.White);

            labelPanel.Controls.Add(_lblBoltNg);
            labelPanel.Controls.Add(_lblMarkNg);
            labelPanel.Controls.Add(_lblTotalNg);
            labelPanel.Controls.Add(_lblStatus);

            _detailPanel.Controls.Add(labelPanel);
            _detailPanel.Controls.Add(_picPreview);
            _split.Panel2.Controls.Add(_detailPanel);

            Controls.Add(_split);
            Controls.Add(_topPanel);
        }

        // ===== 검사 결과 추가 =====
        public void UpdateNgSummary(int boltNg, int markNg, Bitmap capturedImage = null)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateNgSummary(boltNg, markNg, capturedImage)));
                return;
            }

            _runCount++;

            Bitmap thumb = null;
            if (capturedImage != null) thumb = ResizeBitmap(capturedImage, 80, 60);

            var row = new InspSummaryRow
            {
                No = _runCount,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                BoltNg = boltNg,
                MarkNg = markNg,
                Thumbnail = thumb,
                PreviewImage = capturedImage
            };

            _rows.Add(row);
            _listView.SetObjects(_rows);
            _listView.EnsureModelVisible(row);
            _listView.SelectObject(row);
            RefreshSummaryLabel();
        }

        private void OnRowSelected(object sender, EventArgs e)
        {
            if (_listView.SelectedObject is InspSummaryRow row) ShowDetail(row);
        }

        private void ShowDetail(InspSummaryRow row)
        {
            _picPreview.Image = row.PreviewImage;

            _lblBoltNg.Text = $"Bolt NG  : {row.BoltNg}";
            _lblBoltNg.ForeColor = row.BoltNg > 0 ? Color.OrangeRed : Color.LimeGreen;

            _lblMarkNg.Text = $"Mark NG  : {row.MarkNg}";
            _lblMarkNg.ForeColor = row.MarkNg > 0 ? Color.OrangeRed : Color.LimeGreen;

            _lblTotalNg.Text = $"Total NG : {row.TotalNg}";
            _lblTotalNg.ForeColor = row.TotalNg > 0 ? Color.Yellow : Color.LimeGreen;

            _lblStatus.Text = $"판  정   : {row.Status}";
            _lblStatus.ForeColor = row.TotalNg > 0 ? Color.Red : Color.LimeGreen;
            _lblStatus.Font = new Font("Arial", 13, FontStyle.Bold);
        }

        private void RefreshSummaryLabel()
        {
            int total = _rows.Count;
            int ngCount = _rows.Count(r => r.TotalNg > 0);
            int okCount = total - ngCount;
            _lblTotal.Text = $"총 {total}건  |  OK: {okCount}  |  NG: {ngCount}";
            _lblTotal.ForeColor = ngCount > 0 ? Color.OrangeRed : Color.LightGreen;
        }

        // ===== Clear 버튼 → 결과 + 이미지 카운터 초기화 =====
        private void ClearAll()
        {
            // 결과 목록 초기화
            _rows.Clear();
            _runCount = 0;
            _imgList.Images.Clear();
            _listView.SetObjects(_rows);
            _picPreview.Image = null;
            _lblBoltNg.Text = "Bolt NG  : -";
            _lblMarkNg.Text = "Mark NG  : -";
            _lblTotalNg.Text = "Total NG : -";
            _lblStatus.Text = "판  정   : -";
            _lblTotal.Text = "총 0건  |  OK: 0  |  NG: 0";
            _lblTotal.ForeColor = Color.White;

            // ✅ 이미지 카운터도 0으로 리셋 → 다음 검사 때 처음 이미지부터
            Global.Inst.InspStage.ResetImageLoader();
        }

        private static Bitmap ResizeBitmap(Bitmap src, int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return bmp;
        }

        public void AddModelResult(Model curModel) { }
        public void AddWindowResult(InspWindow w) { }
        public void AddInspResult(InspResult r) { }
    }
}