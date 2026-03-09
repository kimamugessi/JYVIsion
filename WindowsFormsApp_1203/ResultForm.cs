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

// ✅ OpenCvSharp과의 모호한 참조 해결
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
        public Bitmap Thumbnail { get; set; }   // 리스트 썸네일 (80x60)
        public Bitmap PreviewImage { get; set; }   // 상세 패널용 큰 이미지
    }

    public partial class ResultForm : DockContent
    {
        private Panel _topPanel;
        private Label _lblTotal;
        private Button _btnClear;
        private SplitContainer _split;
        private ObjectListView _listView;
        private ImageList _imgList;

        // 상세 패널 컨트롤
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
            InitResultLayout(); // ✅ Control.InitLayout() 충돌 방지
        }

        // ✅ InitResultLayout으로 rename (Control.InitLayout 숨김 방지)
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
                Text = "초기화",
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
                Text = "총 0건  |  Bolt NG: 0  |  Mark NG: 0"
            };

            _topPanel.Controls.AddRange(new Control[] { _btnClear, _lblTotal });

            // ── SplitContainer (좌: 누적 리스트 / 우: 상세) ──
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 80,  // ✅ 작게 설정 (SplitterDistance 에러 방지)
                Panel2MinSize = 50
            };

            // ✅ 폼 크기 확정 후 비율 설정 (SizeChanged 사용)
            _split.SizeChanged += (s, e) =>
            {
                if (_split.Width > 100 && _split.SplitterDistance < 50)
                    _split.SplitterDistance = (int)(_split.Width * 0.6);
            };

            // ── 좌: ImageList ─────────────────────────────────
            _imgList = new ImageList
            {
                ImageSize = new Size(80, 60),
                ColorDepth = ColorDepth.Depth32Bit
            };

            // ── 좌: ObjectListView ────────────────────────────
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

            // ── 컬럼 정의 ─────────────────────────────────────
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
            var colNo = new OLVColumn("No", nameof(InspSummaryRow.No))
            {
                Width = 40,
                TextAlign = HorizontalAlignment.Center,
                IsEditable = false
            };
            var colTime = new OLVColumn("시간", nameof(InspSummaryRow.Time))
            {
                Width = 75,
                TextAlign = HorizontalAlignment.Center,
                IsEditable = false
            };
            var colBolt = new OLVColumn("Bolt NG", nameof(InspSummaryRow.BoltNg))
            {
                Width = 65,
                TextAlign = HorizontalAlignment.Center,
                IsEditable = false
            };
            var colMark = new OLVColumn("Mark NG", nameof(InspSummaryRow.MarkNg))
            {
                Width = 65,
                TextAlign = HorizontalAlignment.Center,
                IsEditable = false
            };
            var colStatus = new OLVColumn("판정", nameof(InspSummaryRow.Status))
            {
                Width = 55,
                TextAlign = HorizontalAlignment.Center,
                IsEditable = false
            };

            _listView.Columns.AddRange(new OLVColumn[]
                { colThumb, colNo, colTime, colBolt, colMark, colStatus });

            // ✅ NG 행 빨강 Bold 강조
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

            // ── 우: 상세 패널 ─────────────────────────────────
            _detailPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 35),
                Padding = new Padding(8) // 테두리 여백
            };

            // ✅ 1. 프리뷰 이미지를 왼쪽에 '고정 크기'로 배치 (스케치처럼 정사각형 비율)
            _picPreview = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = 150, // 필요에 따라 이미지 너비를 조절하세요 (ex: 160~200)
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            // ✅ 2. 라벨 패널을 남은 영역(Fill)에 배치하여 텍스트가 이미지 바로 옆에 오도록 설정
            Panel _labelPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            // ✅ 3. 수치 라벨 생성 헬퍼 (위치 지정 방식 변경)
            Label MakeLabel(string text, int y, Color color) => new Label
            {
                AutoSize = true, // 텍스트 길이에 맞춰 딱 맞게 표시
                Location = new Point(15, y), // 이미지 우측 경계로부터 15px 떨어져서 시작
                ForeColor = color,
                Font = new Font("Arial", 11, FontStyle.Bold),
                BackColor = Color.Transparent,
                Text = text
            };

            // ✅ 4. y좌표 간격을 주며 세로로 배치
            _lblBoltNg = MakeLabel("Bolt NG  : -", 20, Color.OrangeRed);
            _lblMarkNg = MakeLabel("Mark NG  : -", 60, Color.OrangeRed);
            _lblTotalNg = MakeLabel("Total NG : -", 100, Color.Yellow);
            _lblStatus = MakeLabel("판  정   : -", 140, Color.White);

            // 라벨 패널에 추가
            _labelPanel.Controls.Add(_lblBoltNg);
            _labelPanel.Controls.Add(_lblMarkNg);
            _labelPanel.Controls.Add(_lblTotalNg);
            _labelPanel.Controls.Add(_lblStatus);

            // ✅ 5. 상세 패널에 조립 (순서 중요: Left 도킹인 _picPreview가 나중에 들어가야 정상 배치됨)
            _detailPanel.Controls.Add(_labelPanel);
            _detailPanel.Controls.Add(_picPreview);

            _split.Panel2.Controls.Add(_detailPanel);

            Controls.Add(_split);
            Controls.Add(_topPanel); // ✅ Top은 마지막에 추가해야 Fill과 충돌 없음
        }

        // ===== InspWorker → 누적 행 추가 =====
        public void UpdateNgSummary(int boltNg, int markNg, Bitmap capturedImage = null)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateNgSummary(boltNg, markNg, capturedImage)));
                return;
            }

            _runCount++;

            // 썸네일 생성 (80x60)
            Bitmap thumb = null;
            if (capturedImage != null)
                thumb = ResizeBitmap(capturedImage, 80, 60);

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

            // ✅ 최신 행 자동 선택 → 상세 패널 갱신
            _listView.SelectObject(row);
            RefreshSummaryLabel();
        }

        // ===== 행 선택 → 상세 패널 갱신 =====
        private void OnRowSelected(object sender, EventArgs e)
        {
            if (_listView.SelectedObject is InspSummaryRow row)
                ShowDetail(row);
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

        // ===== 누적 통계 라벨 갱신 =====
        private void RefreshSummaryLabel()
        {
            int total = _rows.Count;
            int ngCount = _rows.Count(r => r.TotalNg > 0);
            int okCount = total - ngCount;

            _lblTotal.Text = $"총 {total}건  |  OK: {okCount}  |  NG: {ngCount}";
            _lblTotal.ForeColor = ngCount > 0 ? Color.OrangeRed : Color.LightGreen;
        }

        // ===== 전체 초기화 =====
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
            _lblTotal.Text = "총 0건  |  Bolt NG: 0  |  Mark NG: 0";
            _lblTotal.ForeColor = Color.White;
        }

        // ===== 비트맵 리사이즈 헬퍼 =====
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

        // ===== 기존 호환 메서드 유지 =====
        public void AddModelResult(Model curModel) { }
        public void AddWindowResult(InspWindow w) { }
        public void AddInspResult(InspResult r) { }
    }
}