using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace JYVision.Inspect
{
    // ===== 불량률 트렌드 차트 폼 (순수 GDI+ - 외부 참조 없음) =====
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

        private static readonly Color ColBg = Color.FromArgb(28, 28, 34);
        private static readonly Color ColPlotBg = Color.FromArgb(20, 20, 26);
        private static readonly Color ColGrid = Color.FromArgb(48, 48, 60);
        private static readonly Color ColBolt = Color.FromArgb(255, 100, 60);
        private static readonly Color ColMark = Color.FromArgb(80, 160, 255);
        private static readonly Color ColTotal = Color.FromArgb(255, 200, 50);
        private static readonly Color ColText = Color.FromArgb(160, 160, 170);

        private static readonly int[] RangeLimits = { 20, 50, 100, 200, 0 };

        public TrendChartForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "불량률 트렌드";
            Size = new Size(800, 500);
            MinimumSize = new Size(600, 380);
            BackColor = ColBg;
            ForeColor = Color.White;
            Font = new Font("맑은 고딕", 9f);

            // 상단 요약 패널
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(18, 18, 24)
            };
            _lblTotal = MakeStat("검사: 0", Color.FromArgb(170, 170, 180), 10);
            _lblBoltNg = MakeStat("Bolt NG: 0", ColBolt, 170);
            _lblMarkNg = MakeStat("Mark NG: 0", ColMark, 330);
            _lblNgRate = MakeStat("NG율: 0.0%", ColTotal, 490);
            topPanel.Controls.AddRange(new Control[] { _lblTotal, _lblBoltNg, _lblMarkNg, _lblNgRate });

            // 하단 컨트롤 바
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(18, 18, 24)
            };
            bottomPanel.Controls.Add(new Label
            {
                Text = "표시 범위:",
                ForeColor = ColText,
                AutoSize = true,
                Location = new Point(10, 10)
            });
            _cmbRange = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 6),
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 58),
                ForeColor = Color.White
            };
            _cmbRange.Items.AddRange(new object[] { "최근 20건", "최근 50건", "최근 100건", "최근 200건", "전체" });
            _cmbRange.SelectedIndex = 1;
            _cmbRange.SelectedIndexChanged += (s, e) => _chartPanel.Invalidate();

            _btnClear = new Button
            {
                Text = "초기화",
                Location = new Point(182, 4),
                Size = new Size(62, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 70),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnClear.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 100);
            _btnClear.Click += (s, e) => ClearData();
            bottomPanel.Controls.AddRange(new Control[] { _cmbRange, _btnClear });

            // 차트 패널 (GDI+ 직접 렌더링)
            _chartPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColPlotBg
            };
            _chartPanel.Paint += OnChartPaint;

            Controls.Add(_chartPanel);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
        }

        private Label MakeStat(string text, Color color, int x)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Size = new Size(155, 64),
                Location = new Point(x, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = color,
                Font = new Font("맑은 고딕", 12f, FontStyle.Bold)
            };
        }

        // ===== GDI+ 차트 렌더링 =====
        private void OnChartPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int W = _chartPanel.Width;
            int H = _chartPanel.Height;

            int ml = 52, mr = 20, mt = 18, mb = 38;
            Rectangle plot = new Rectangle(ml, mt, W - ml - mr, H - mt - mb);

            int limit = RangeLimits[_cmbRange.SelectedIndex];
            var view = (limit == 0 || _records.Count <= limit)
                ? _records.ToList()
                : _records.Skip(_records.Count - limit).ToList();

            int n = view.Count;

            g.FillRectangle(new SolidBrush(ColPlotBg), plot);
            g.DrawRectangle(new Pen(ColGrid, 1), plot);

            if (n == 0)
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("검사 데이터 없음",
                    new Font("맑은 고딕", 11f),
                    new SolidBrush(ColGrid),
                    new RectangleF(plot.X, plot.Y, plot.Width, plot.Height), sf);
                return;
            }

            int maxVal = view.Max(r => r.BoltNg + r.MarkNg);
            if (maxVal < 1) maxVal = 1;
            int yMax = (int)(Math.Ceiling(maxVal * 1.3));
            if (yMax < 3) yMax = 3;

            var labelFont = new Font("맑은 고딕", 7.5f);
            var labelBrush = new SolidBrush(ColText);
            var gridPen = new Pen(ColGrid, 1) { DashStyle = DashStyle.Dash };

            // Y 그리드 + 레이블
            int gridCount = 4;
            for (int i = 0; i <= gridCount; i++)
            {
                float fy = plot.Bottom - plot.Height * i / (float)gridCount;
                int val = (int)Math.Round(yMax * i / (double)gridCount);
                g.DrawLine(gridPen, plot.Left, fy, plot.Right, fy);
                string lbl = val.ToString();
                SizeF sz = g.MeasureString(lbl, labelFont);
                g.DrawString(lbl, labelFont, labelBrush,
                    plot.Left - sz.Width - 3, fy - sz.Height / 2);
            }

            // X 레이블 (최대 10개)
            int xStep = Math.Max(1, n / 10);
            for (int i = 0; i < n; i += xStep)
            {
                int denom = (n - 1 == 0) ? 1 : n - 1;
                float fx = plot.Left + plot.Width * i / (float)denom;
                string lbl = view[i].Index.ToString();
                SizeF sz = g.MeasureString(lbl, labelFont);
                g.DrawString(lbl, labelFont, labelBrush,
                    fx - sz.Width / 2, plot.Bottom + 4);
                g.DrawLine(new Pen(ColGrid, 1), fx, plot.Bottom, fx, plot.Bottom + 3);
            }

            // 범례
            DrawLegend(g, plot);

            // 라인 시리즈
            if (n >= 2)
            {
                DrawLineSeries(g, plot, view, n, yMax, r => r.BoltNg, ColBolt, false);
                DrawLineSeries(g, plot, view, n, yMax, r => r.MarkNg, ColMark, false);
                DrawLineSeries(g, plot, view, n, yMax, r => r.BoltNg + r.MarkNg, ColTotal, true);
            }
            else
            {
                DrawDot(g, plot, view[0].BoltNg, yMax, ColBolt);
                DrawDot(g, plot, view[0].MarkNg, yMax, ColMark);
                DrawDot(g, plot, view[0].BoltNg + view[0].MarkNg, yMax, ColTotal);
            }

            // 연속 NG 경고
            DrawConsecutiveWarning(g, plot, view, n, yMax);

            // 축 제목
            g.DrawString("검사 번호",
                new Font("맑은 고딕", 8f), labelBrush,
                plot.Left + plot.Width / 2 - 20, plot.Bottom + 20);
        }

        private void DrawLineSeries(Graphics g, Rectangle plot,
            List<InspRecord> view, int n, int yMax,
            Func<InspRecord, int> getValue, Color color, bool dashed)
        {
            var pen = new Pen(color, 2f);
            if (dashed) pen.DashStyle = DashStyle.Dash;

            PointF[] pts = new PointF[n];
            int denom = (n - 1 == 0) ? 1 : n - 1;
            for (int i = 0; i < n; i++)
            {
                float fx = plot.Left + plot.Width * i / (float)denom;
                float fy = plot.Bottom - plot.Height * getValue(view[i]) / (float)yMax;
                pts[i] = new PointF(fx, fy);
            }

            g.DrawLines(pen, pts);

            if (!dashed)
            {
                var brush = new SolidBrush(color);
                var innerPen = new Pen(Color.FromArgb(40, 40, 50), 1);
                foreach (var pt in pts)
                {
                    g.FillEllipse(brush, pt.X - 3, pt.Y - 3, 6, 6);
                    g.DrawEllipse(innerPen, pt.X - 3, pt.Y - 3, 6, 6);
                }
            }
        }

        private void DrawDot(Graphics g, Rectangle plot, int val, int yMax, Color color)
        {
            float fx = plot.Left + plot.Width * 0.5f;
            float fy = plot.Bottom - plot.Height * val / (float)yMax;
            g.FillEllipse(new SolidBrush(color), fx - 4, fy - 4, 8, 8);
        }

        private void DrawLegend(Graphics g, Rectangle plot)
        {
            var items = new[]
            {
                Tuple.Create("■ Bolt NG",   ColBolt),
                Tuple.Create("◆ Mark NG",   ColMark),
                Tuple.Create("-- Total NG", ColTotal)
            };
            var lf = new Font("맑은 고딕", 8f);
            float lx = plot.Left + 8;
            float ly = plot.Top + 6;
            foreach (var item in items)
            {
                SizeF sz = g.MeasureString(item.Item1, lf);
                g.FillRectangle(new SolidBrush(Color.FromArgb(120, 0, 0, 0)),
                    lx - 2, ly - 1, sz.Width + 4, sz.Height + 2);
                g.DrawString(item.Item1, lf, new SolidBrush(item.Item2), lx, ly);
                lx += sz.Width + 14;
            }
        }

        private void DrawConsecutiveWarning(Graphics g, Rectangle plot,
            List<InspRecord> view, int n, int yMax)
        {
            const int threshold = 5;
            int streak = 0;
            var warnFont = new Font("맑은 고딕", 7.5f, FontStyle.Bold);
            var warnBrush = new SolidBrush(Color.White);
            var warnBg = new SolidBrush(Color.FromArgb(200, 60, 40));
            int denom = (n - 1 == 0) ? 1 : n - 1;

            for (int i = 0; i < n; i++)
            {
                var r = view[i];
                if (r.BoltNg > 0 || r.MarkNg > 0) streak++;
                else streak = 0;

                if (streak >= threshold)
                {
                    float fx = plot.Left + plot.Width * i / (float)denom;
                    int tot = r.BoltNg + r.MarkNg;
                    float fy = plot.Bottom - plot.Height * tot / (float)yMax;
                    string msg = string.Format("연속 NG {0}건!", streak);
                    SizeF sz = g.MeasureString(msg, warnFont);
                    float bx = Math.Min(fx + 4, plot.Right - sz.Width - 4);
                    float by = Math.Max(fy - sz.Height - 8, plot.Top + 2);

                    g.FillRectangle(warnBg, bx - 2, by - 1, sz.Width + 4, sz.Height + 2);
                    g.DrawString(msg, warnFont, warnBrush, bx, by);
                    break;
                }
            }
        }

        // ===== 외부 호출 =====
        public void AddRecord(int boltNg, int markNg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AddRecord(boltNg, markNg)));
                return;
            }
            _totalCount++;
            _records.Add(new InspRecord
            {
                Index = _totalCount,
                BoltNg = boltNg,
                MarkNg = markNg
            });
            UpdateSummary();
            _chartPanel.Invalidate();
        }

        private void UpdateSummary()
        {
            int totalBolt = _records.Sum(r => r.BoltNg);
            int totalMark = _records.Sum(r => r.MarkNg);
            int ngCount = _records.Count(r => r.BoltNg > 0 || r.MarkNg > 0);
            double ngRate = _records.Count == 0 ? 0.0 : ngCount * 100.0 / _records.Count;

            _lblTotal.Text = string.Format("검사: {0}", _records.Count);
            _lblBoltNg.Text = string.Format("Bolt NG: {0}", totalBolt);
            _lblMarkNg.Text = string.Format("Mark NG: {0}", totalMark);
            _lblNgRate.Text = string.Format("NG율: {0:F1}%", ngRate);

            _lblNgRate.ForeColor = ngRate >= 20.0
                ? Color.FromArgb(255, 80, 60)
                : ngRate >= 10.0
                    ? Color.FromArgb(255, 200, 50)
                    : Color.FromArgb(100, 220, 120);
        }

        private void ClearData()
        {
            _records.Clear();
            _totalCount = 0;
            _lblTotal.Text = "검사: 0";
            _lblBoltNg.Text = "Bolt NG: 0";
            _lblMarkNg.Text = "Mark NG: 0";
            _lblNgRate.Text = "NG율: 0.0%";
            _lblNgRate.ForeColor = ColTotal;
            _chartPanel.Invalidate();
        }
    }
}