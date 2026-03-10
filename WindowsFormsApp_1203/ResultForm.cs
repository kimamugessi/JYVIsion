using BrightIdeasSoftware;
using JYVision.Core;
using JYVision.Inspect;
using JYVision.Teach;
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

    // ===== 트렌드용 내부 레코드 =====
    internal struct TrendRecord
    {
        public int Index;
        public int BoltNg;
        public int MarkNg;
    }

    public partial class ResultForm : DockContent
    {
        // ── 기존 결과 컨트롤 ──
        private Panel _topPanel;
        private Label _lblTotal;
        private Button _btnClear;
        private SplitContainer _split;
        private ObjectListView _listView;
        private ImageList _imgList;
        private Panel _detailPanel;
        private PictureBox _picPreview;
        private Label _lblBoltNg, _lblMarkNg, _lblTotalNg, _lblStatus;

        // ── 트렌드 차트 컨트롤 ──
        private Panel _chartPanel;
        private ComboBox _cmbRange;
        private readonly List<TrendRecord> _trendRecords = new List<TrendRecord>();
        private static readonly int[] RangeLimits = { 20, 50, 100, 200, 0 };

        // ── 공통 데이터 ──
        private readonly List<InspSummaryRow> _rows = new List<InspSummaryRow>();
        private int _runCount = 0;

        // ── 트렌드 색상 ──
        private static readonly Color ColBolt = Color.FromArgb(200, 50, 20);
        private static readonly Color ColMark = Color.FromArgb(0, 100, 200);
        private static readonly Color ColTotal = Color.FromArgb(180, 120, 0);
        private static readonly Color ColGrid = Color.FromArgb(210, 210, 215);

        public ResultForm()
        {
            InitializeComponent();
            InitResultLayout();
        }

        private void InitResultLayout()
        {
            // ── 상단 버튼바 ──
            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(0, 20, 40)
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

            // ── SplitContainer ──
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

            // ── ImageList + ObjectListView ──
            _imgList = new ImageList
            {
                ImageSize = new Size(80, 60),
                ColorDepth = ColorDepth.Depth32Bit
            };
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

            // ── Panel2: TabControl (상세 / 트렌드) ──
            var tabCtrl = new TabControl { Dock = DockStyle.Fill };

            // --- 탭1: 상세 ---
            var tabDetail = new TabPage("상세") { BackColor = Color.FromArgb(30, 30, 35) };
            _detailPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 35), Padding = new Padding(8) };

            _picPreview = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = 150,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            var labelPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
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
            labelPanel.Controls.AddRange(new Control[] { _lblBoltNg, _lblMarkNg, _lblTotalNg, _lblStatus });

            _detailPanel.Controls.Add(labelPanel);
            _detailPanel.Controls.Add(_picPreview);
            tabDetail.Controls.Add(_detailPanel);

            // --- 탭2: 트렌드 ---
            var tabTrend = new TabPage("트렌드") { BackColor = Color.White };

            // 하단 컨트롤
            var trendBottom = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(243, 243, 243) };
            trendBottom.Controls.Add(new Label { Text = "범위:", AutoSize = true, Location = new Point(4, 7) });
            _cmbRange = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(40, 4),
                Width = 75,
                Font = new Font("맑은 고딕", 8f)
            };
            _cmbRange.Items.AddRange(new object[] { "20건", "50건", "100건", "200건", "전체" });
            _cmbRange.SelectedIndex = 1;
            _cmbRange.SelectedIndexChanged += (s, e) => _chartPanel?.Invalidate();

            var btnTrendClear = new Button
            {
                Text = "초기화",
                Location = new Point(122, 3),
                Size = new Size(48, 22),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 8f)
            };
            btnTrendClear.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            btnTrendClear.Click += (s, e) => ClearTrend();
            trendBottom.Controls.AddRange(new Control[] { _cmbRange, btnTrendClear });

            // 차트 패널
            _chartPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _chartPanel.Paint += OnChartPaint;

            tabTrend.Controls.Add(_chartPanel);
            tabTrend.Controls.Add(trendBottom);

            tabCtrl.TabPages.Add(tabDetail);
            tabCtrl.TabPages.Add(tabTrend);
            _split.Panel2.Controls.Add(tabCtrl);

            Controls.Add(_split);
            Controls.Add(_topPanel);
        }

        // ===== 검사 결과 추가 =====
        public void UpdateNgSummary(int boltNg, int markNg, Bitmap capturedImage = null)
        {
            if (InvokeRequired) { Invoke(new Action(() => UpdateNgSummary(boltNg, markNg, capturedImage))); return; }

            _runCount++;
            Bitmap thumb = capturedImage != null ? ResizeBitmap(capturedImage, 80, 60) : null;
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

            // 트렌드에도 추가
            AddTrendRecord(boltNg, markNg);
        }

        // ===== 트렌드 차트 =====
        private void AddTrendRecord(int boltNg, int markNg)
        {
            _trendRecords.Add(new TrendRecord
            {
                Index = _trendRecords.Count + 1,
                BoltNg = boltNg,
                MarkNg = markNg
            });
            _chartPanel?.Invalidate();
        }

        private void OnChartPaint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int W = _chartPanel.Width, H = _chartPanel.Height;
            int ml = 36, mr = 10, mt = 14, mb = 28;
            var plot = new Rectangle(ml, mt, W - ml - mr, H - mt - mb);

            g.FillRectangle(Brushes.White, plot);
            g.DrawRectangle(new Pen(ColGrid, 1), plot);

            int limit = RangeLimits[_cmbRange.SelectedIndex];
            var view = (limit == 0 || _trendRecords.Count <= limit)
                ? _trendRecords.ToList()
                : _trendRecords.Skip(_trendRecords.Count - limit).ToList();
            int n = view.Count;

            if (n == 0)
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("검사 데이터 없음", new Font("맑은 고딕", 9f),
                    new SolidBrush(Color.FromArgb(180, 180, 180)),
                    new RectangleF(plot.X, plot.Y, plot.Width, plot.Height), sf);
                return;
            }

            int maxVal = view.Max(r => r.BoltNg + r.MarkNg);
            if (maxVal < 1) maxVal = 1;
            int yMax = (int)(Math.Ceiling(maxVal * 1.3));
            if (yMax < 3) yMax = 3;

            var lf = new Font("맑은 고딕", 7f);
            var lb = new SolidBrush(Color.FromArgb(80, 80, 80));
            var gPen = new Pen(ColGrid, 1) { DashStyle = DashStyle.Dash };
            int denom = n - 1 == 0 ? 1 : n - 1;

            // Y 그리드
            for (int i = 0; i <= 4; i++)
            {
                float fy = plot.Bottom - plot.Height * i / 4f;
                int val = (int)Math.Round(yMax * i / 4.0);
                g.DrawLine(gPen, plot.Left, fy, plot.Right, fy);
                string lbl = val.ToString();
                SizeF sz = g.MeasureString(lbl, lf);
                g.DrawString(lbl, lf, lb, plot.Left - sz.Width - 2, fy - sz.Height / 2);
            }

            // X 레이블
            int xStep = Math.Max(1, n / 8);
            for (int i = 0; i < n; i += xStep)
            {
                float fx = plot.Left + plot.Width * i / (float)denom;
                string lbl = view[i].Index.ToString();
                SizeF sz = g.MeasureString(lbl, lf);
                g.DrawString(lbl, lf, lb, fx - sz.Width / 2, plot.Bottom + 3);
            }

            // 범례
            float lx = plot.Left + 4, ly = plot.Top + 3;
            foreach (var item in new[] {
                Tuple.Create("■ Bolt",  ColBolt),
                Tuple.Create("■ Mark",  ColMark),
                Tuple.Create("-- Total", ColTotal) })
            {
                SizeF sz = g.MeasureString(item.Item1, lf);
                g.DrawString(item.Item1, lf, new SolidBrush(item.Item2), lx, ly);
                lx += sz.Width + 8;
            }

            // 라인
            if (n >= 2)
            {
                DrawLine(g, plot, view, n, yMax, denom, r => r.BoltNg, ColBolt, false);
                DrawLine(g, plot, view, n, yMax, denom, r => r.MarkNg, ColMark, false);
                DrawLine(g, plot, view, n, yMax, denom, r => r.BoltNg + r.MarkNg, ColTotal, true);
            }
        }

        private void DrawLine(Graphics g, Rectangle plot, List<TrendRecord> view,
            int n, int yMax, int denom, Func<TrendRecord, int> getValue, Color color, bool dashed)
        {
            var pen = new Pen(color, 1.5f);
            if (dashed) pen.DashStyle = DashStyle.Dash;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(
                    plot.Left + plot.Width * i / (float)denom,
                    plot.Bottom - plot.Height * getValue(view[i]) / (float)yMax);
            g.DrawLines(pen, pts);
            if (!dashed)
            {
                var br = new SolidBrush(color);
                foreach (var pt in pts)
                    g.FillEllipse(br, pt.X - 2.5f, pt.Y - 2.5f, 5, 5);
            }
        }

        private void ClearTrend()
        {
            _trendRecords.Clear();
            _chartPanel?.Invalidate();
        }

        // ===== 기존 메서드 =====
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

        private void ClearAll()
        {
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
            ClearTrend();
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