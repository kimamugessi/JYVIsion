using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision.Inspect
{
    public class TrendChartForm : DockContent
    {
        private struct InspRecord
        {
            public int Index;
            public int BoltNg;
            public int MarkNg;
        }

        private readonly List<InspRecord> _records = new List<InspRecord>();
        private int _totalCount = 0;

        private Panel _chartPanel;
        private Label _lblTotal;
        private Label _lblBoltNg;
        private Label _lblMarkNg;
        private Label _lblNgRate;
        private ComboBox _cmbRange;
        private Button _btnClear;

        // VS 속성창 스타일 색상
        private static readonly Color ColBg = Color.FromArgb(243, 243, 243);
        private static readonly Color ColHeader = Color.FromArgb(0, 122, 204); // VS 파란색
        private static readonly Color ColBolt = Color.FromArgb(200, 50, 20);
        private static readonly Color ColMark = Color.FromArgb(0, 100, 200);
        private static readonly Color ColTotal = Color.FromArgb(180, 120, 0);
        private static readonly Color ColGrid = Color.FromArgb(210, 210, 215);
        private static readonly Color ColText = Color.FromArgb(50, 50, 50);
        private static readonly Color ColPlotBg = Color.White;

        private static readonly int[] RangeLimits = { 20, 50, 100, 200, 0 };

        public TrendChartForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "불량률 트렌드";
            BackColor = ColBg;
            ForeColor = ColText;
            Font = new Font("Segoe UI", 8.5f);
            DockAreas = DockAreas.DockLeft | DockAreas.DockRight |
                          DockAreas.DockBottom | DockAreas.Float | DockAreas.Document;

            // ── 헤더 바 ──
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 24,
                BackColor = ColHeader
            };
            header.Controls.Add(new Label
            {
                Text = "▶ 불량률 트렌드",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            });

            // ── 요약 그리드 (속성창 스타일) ──
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 72,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = Color.White,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < 4; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            _lblTotal = MakeGridCell("검사", "0 건", ColText);
            _lblBoltNg = MakeGridCell("Bolt NG", "0", ColBolt);
            _lblMarkNg = MakeGridCell("Mark NG", "0", ColMark);
            _lblNgRate = MakeGridCell("NG율", "0.0 %", ColTotal);

            // 왼쪽: 항목명, 오른쪽: 값
            grid.Controls.Add(MakeKeyLabel("검사"), 0, 0);
            grid.Controls.Add(_lblTotal, 1, 0);
            grid.Controls.Add(MakeKeyLabel("Bolt NG"), 0, 1);
            grid.Controls.Add(_lblBoltNg, 1, 1);
            grid.Controls.Add(MakeKeyLabel("Mark NG"), 0, 2);
            grid.Controls.Add(_lblMarkNg, 1, 2);
            grid.Controls.Add(MakeKeyLabel("NG율"), 0, 3);
            grid.Controls.Add(_lblNgRate, 1, 3);

            // ── 하단 컨트롤 바 ──
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = ColBg
            };
            bottomPanel.Controls.Add(new Label
            {
                Text = "범위:",
                AutoSize = true,
                ForeColor = ColText,
                Location = new Point(4, 7)
            });
            _cmbRange = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(38, 4),
                Width = 80,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f)
            };
            _cmbRange.Items.AddRange(new object[] { "20건", "50건", "100건", "200건", "전체" });
            _cmbRange.SelectedIndex = 1;
            _cmbRange.SelectedIndexChanged += (s, e) => _chartPanel.Invalidate();

            _btnClear = new Button
            {
                Text = "Clear",
                Location = new Point(124, 3),
                Size = new Size(48, 22),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            _btnClear.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            _btnClear.Click += (s, e) => ClearData();
            bottomPanel.Controls.AddRange(new Control[] { _cmbRange, _btnClear });

            // ── 차트 패널 ──
            _chartPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColPlotBg,
                BorderStyle = BorderStyle.None
            };
            _chartPanel.Paint += OnChartPaint;

            // ── 구분선 ──
            var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = ColGrid };

            Controls.Add(_chartPanel);
            Controls.Add(sep);
            Controls.Add(grid);
            Controls.Add(header);
            Controls.Add(bottomPanel);
        }

        private Label MakeKeyLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ColText,
                BackColor = Color.FromArgb(248, 248, 248),
                Font = new Font("Segoe UI", 8f),
                Padding = new Padding(4, 0, 0, 0),
                Margin = new Padding(0)
            };
        }

        private Label MakeGridCell(string key, string val, Color color)
        {
            return new Label
            {
                Text = val,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = color,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(0)
            };
        }

        // ===== GDI+ 차트 렌더링 =====
        private void OnChartPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int W = _chartPanel.Width;
            int H = _chartPanel.Height;
            int ml = 36, mr = 10, mt = 14, mb = 28;
            var plot = new Rectangle(ml, mt, W - ml - mr, H - mt - mb);

            g.FillRectangle(new SolidBrush(ColPlotBg), plot);
            g.DrawRectangle(new Pen(ColGrid, 1), plot);

            int limit = RangeLimits[_cmbRange.SelectedIndex];
            var view = (limit == 0 || _records.Count <= limit)
                ? _records.ToList()
                : _records.Skip(_records.Count - limit).ToList();
            int n = view.Count;

            if (n == 0)
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("검사 데이터 없음",
                    new Font("Segoe UI", 9f),
                    new SolidBrush(Color.FromArgb(180, 180, 180)),
                    new RectangleF(plot.X, plot.Y, plot.Width, plot.Height), sf);
                return;
            }

            int maxVal = view.Max(r => r.BoltNg + r.MarkNg);
            if (maxVal < 1) maxVal = 1;
            int yMax = (int)(Math.Ceiling(maxVal * 1.3));
            if (yMax < 3) yMax = 3;

            var lf = new Font("Segoe UI", 7f);
            var lb = new SolidBrush(ColText);
            var gPen = new Pen(ColGrid, 1) { DashStyle = DashStyle.Dash };

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
            int denom = n - 1 == 0 ? 1 : n - 1;
            for (int i = 0; i < n; i += xStep)
            {
                float fx = plot.Left + plot.Width * i / (float)denom;
                string lbl = view[i].Index.ToString();
                SizeF sz = g.MeasureString(lbl, lf);
                g.DrawString(lbl, lf, lb, fx - sz.Width / 2, plot.Bottom + 3);
            }

            // 범례
            DrawLegend(g, plot);

            // 라인
            if (n >= 2)
            {
                DrawLine(g, plot, view, n, yMax, r => r.BoltNg, ColBolt, false);
                DrawLine(g, plot, view, n, yMax, r => r.MarkNg, ColMark, false);
                DrawLine(g, plot, view, n, yMax, r => r.BoltNg + r.MarkNg, ColTotal, true);
            }

            // 연속 NG 경고
            DrawWarning(g, plot, view, n, yMax, denom);
        }

        private void DrawLine(Graphics g, Rectangle plot, List<InspRecord> view,
            int n, int yMax, Func<InspRecord, int> getValue, Color color, bool dashed)
        {
            int denom = n - 1 == 0 ? 1 : n - 1;
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

        private void DrawLegend(Graphics g, Rectangle plot)
        {
            var items = new[]
            {
                Tuple.Create("■ Bolt", ColBolt),
                Tuple.Create("■ Mark", ColMark),
                Tuple.Create("-- Total", ColTotal)
            };
            var lf = new Font("Segoe UI", 7f);
            float lx = plot.Left + 4;
            float ly = plot.Top + 3;
            foreach (var item in items)
            {
                SizeF sz = g.MeasureString(item.Item1, lf);
                g.DrawString(item.Item1, lf, new SolidBrush(item.Item2), lx, ly);
                lx += sz.Width + 8;
            }
        }

        private void DrawWarning(Graphics g, Rectangle plot,
            List<InspRecord> view, int n, int yMax, int denom)
        {
            int streak = 0;
            var wf = new Font("Segoe UI", 7f, FontStyle.Bold);
            for (int i = 0; i < n; i++)
            {
                var r = view[i];
                streak = (r.BoltNg > 0 || r.MarkNg > 0) ? streak + 1 : 0;
                if (streak >= 5)
                {
                    float fx = plot.Left + plot.Width * i / (float)denom;
                    float fy = plot.Bottom - plot.Height * (r.BoltNg + r.MarkNg) / (float)yMax;
                    string msg = string.Format("연속 {0}건!", streak);
                    SizeF sz = g.MeasureString(msg, wf);
                    float bx = Math.Min(fx + 2, plot.Right - sz.Width - 2);
                    float by = Math.Max(fy - sz.Height - 4, plot.Top + 2);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, 60, 40)), bx - 2, by - 1, sz.Width + 4, sz.Height + 2);
                    g.DrawString(msg, wf, new SolidBrush(Color.White), bx, by);
                    break;
                }
            }
        }

        // ===== 외부 호출 =====
        public void AddRecord(int boltNg, int markNg)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => AddRecord(boltNg, markNg))); return; }
            _totalCount++;
            _records.Add(new InspRecord { Index = _totalCount, BoltNg = boltNg, MarkNg = markNg });
            UpdateSummary();
            _chartPanel.Invalidate();
        }

        private void UpdateSummary()
        {
            int tb = _records.Sum(r => r.BoltNg);
            int tm = _records.Sum(r => r.MarkNg);
            int ng = _records.Count(r => r.BoltNg > 0 || r.MarkNg > 0);
            double rate = _records.Count == 0 ? 0.0 : ng * 100.0 / _records.Count;

            _lblTotal.Text = string.Format("{0} 건", _records.Count);
            _lblBoltNg.Text = string.Format("{0}", tb);
            _lblMarkNg.Text = string.Format("{0}", tm);
            _lblNgRate.Text = string.Format("{0:F1} %", rate);

            _lblNgRate.ForeColor = rate >= 20.0
                ? Color.FromArgb(200, 40, 20)
                : rate >= 10.0
                    ? Color.FromArgb(180, 120, 0)
                    : Color.FromArgb(0, 140, 60);
        }

        private void ClearData()
        {
            _records.Clear();
            _totalCount = 0;
            _lblTotal.Text = "0 건";
            _lblBoltNg.Text = "0";
            _lblMarkNg.Text = "0";
            _lblNgRate.Text = "0.0 %";
            _lblNgRate.ForeColor = ColTotal;
            _chartPanel.Invalidate();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // TrendChartForm
            // 
            this.ClientSize = new System.Drawing.Size(282, 256);
            this.Font = new System.Drawing.Font("Segoe UI", 9.163636F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "TrendChartForm";
            this.ResumeLayout(false);

        }
    }
}