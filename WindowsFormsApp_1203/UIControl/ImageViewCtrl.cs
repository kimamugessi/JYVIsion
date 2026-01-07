using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JYVision.Algorithm;
using JYVision.Core;

namespace JYVision.UIControl
{
    public partial class ImageViewCtrl : UserControl
    {
        private bool _isInitialized = false;

        private Bitmap _bitmapImage = null; //로드된 이미지

        private Bitmap Canvas= null;    //화면 깜빡이는 현상 없애기 위함, 숨겨진 상태에서 그리고 나중에 보여지는 기능

        private RectangleF ImageRect = new RectangleF(0, 0, 0, 0);  //표시 이미지 크기 및 위치

        private float _curZoom = 1.0f;  //배율
        private float _zoomFactor = 1.1f;   //확대,축소 변경 단위
        private float MinZoom = 1.0f;   //Zoom 최소 크기
        private float MaxZoom = 100.0f; //Zoom 최대 크기

        private List<DrawInspectInfo> _rectInfos = new List<DrawInspectInfo>();

        public ImageViewCtrl()
        {
            InitializeComponent();
            initializeCanvas();

            MouseWheel += new MouseEventHandler(ImageViewCCtrl_MouseWheel);
        }

        private void initializeCanvas() //캔버스 초기화 및 설정
        {
            ResizeCanvas(); //캔버스 userControl 크기만큼 생성
            DoubleBuffered = true;  //깜빡임 방지 더블 버퍼 설정
        }

        public Bitmap GetCurBitmap()
        {
            return _bitmapImage;
        }
        private void ResizeCanvas() //도킹펜이 변할때마다 이미지 사이즈 재계산을 위함
        {
            if (Width <= 0 || Height <= 0 || _bitmapImage == null) return;
            Canvas=new Bitmap(Width, Height);
            if(Canvas==null) return;

            float virtualWidth = _bitmapImage.Width * _curZoom;
            float virtualHeight = _bitmapImage.Height * _curZoom;

            float offsetX = virtualWidth < Width ? (Width - virtualWidth) / 2f : 0f;
            float offsetY = virtualHeight < Height ? (Height - virtualHeight) / 2f : 0f;

            ImageRect=new RectangleF(offsetX, offsetY, virtualWidth, virtualHeight);
        }

        public void LoadBitmap(Bitmap bitmap)   //이미지 로드 함수
        {
            if (_bitmapImage != null)   //이미지가 있다면 해제 후 초기화
            {
                //이미지 크기가 같다면 이미지 변경 후 화면 갱신
                if (_bitmapImage.Width == bitmap.Width && _bitmapImage.Height == bitmap.Height) 
                {
                    _bitmapImage=bitmap;
                    Invalidate();
                    return;
                }
                _bitmapImage.Dispose(); //birmap 객체가 사요하던 메모리 리소스 해제
                _bitmapImage = null;  //객체 해제하여 GC을 수집할 수 있도록 설정
            }
            _bitmapImage = bitmap;  //새 이미지 로드;
            if (_isInitialized == false)    ////bitmap==null 예외처리도 초기화되지않은 변수들 초기화
            {
                _isInitialized = true;
                ResizeCanvas();
            }
            FitImageToScreen();
        }

        private void FitImageToScreen()
        {
            if (_bitmapImage is null)
                return;

            RecalcZoomRatio();

            float NewWidth = _bitmapImage.Width * _curZoom;
            float NewHeight = _bitmapImage.Height * _curZoom;

            ImageRect = new RectangleF( //이미지가 UserControl중앙에 배치되도록 정렬
                (Width - NewWidth) / 2,
                (Height - NewHeight) / 2,
                NewWidth,
                NewHeight
            );

            Invalidate();   //내부 함수, 화면 갱신 기능
        }
        private void RecalcZoomRatio()  //줌비율 재계산(모르것음)
        {
            if (_bitmapImage == null || Width <= 0 || Height <= 0) return;

            Size imageSize = new Size(_bitmapImage.Width, _bitmapImage.Height);

            float aspectRatio = (float)imageSize.Height / (float)imageSize.Width;
            float clientAspect = (float)Height / (float)Width;

            float ratio;

            if (aspectRatio <= clientAspect)
                ratio = (float)Width / (float)imageSize.Width;
            else
                ratio = (float)Height / (float)imageSize.Height;

            float minZoom = ratio;

            MinZoom = minZoom;

            _curZoom = Math.Max(MinZoom, Math.Min(MaxZoom, ratio)); //min, max값을 벗어나지 않게 설정

            Invalidate();   //내부 함수, 화면 갱신 기능
        }

        // Windows Forms에서 컨트롤이 다시 그려질 때 자동으로 호출되는 메서드
        // 화면새로고침(Invalidate()), 창 크기변경, 컨트롤이 숨겨졌다가 나타날때 실행
        protected override void OnPaint(PaintEventArgs e)
        {
           base.OnPaint(e); //base.____:부모 클래스의 것을 가져다 씀

            if (_bitmapImage != null && Canvas != null)
            {
                using(Graphics g = Graphics.FromImage(Canvas))  //캔버스 초기화, 이미지 그리기
                {
                    g.Clear(Color.Transparent); //배경을 투명하게

                    g.InterpolationMode = InterpolationMode.NearestNeighbor;    //이미지 확대or축소때 화질 최적화 방식(Interpolation Mode) 설정   
                    g.DrawImage(_bitmapImage, ImageRect);

                    DrawDiagram(g);
                    e.Graphics.DrawImage(Canvas, 0, 0); // 캔버스를 UserControl 화면에 표시
                }
            }
        }
        private void DrawDiagram(Graphics g)
        {
            // 이미지 좌표 → 화면 좌표 변환 후 사각형 그리기
            if (_rectInfos != null)
            {
                foreach (DrawInspectInfo rectInfo in _rectInfos)
                {
                    Color lineColor = Color.LightCoral;
                    if (rectInfo.decision == DecisionType.Defect)
                        lineColor = Color.Red;
                    else if (rectInfo.decision == DecisionType.Good)
                        lineColor = Color.LightGreen;

                    Rectangle rect = new Rectangle(rectInfo.rect.X, rectInfo.rect.Y, rectInfo.rect.Width, rectInfo.rect.Height);
                    Rectangle screenRect = VirtualToScreen(rect);

                    using (Pen pen = new Pen(lineColor, 2))
                    {
                        if (rectInfo.UseRotatedRect)
                        {
                            PointF[] screenPoints = rectInfo.rotatedPoints
                                                    .Select(p => VirtualToScreen(new PointF(p.X, p.Y))) // 화면 좌표계로 변환
                                                    .ToArray();

                            if (screenPoints.Length == 4)
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    g.DrawLine(pen, screenPoints[i], screenPoints[(i + 1) % 4]); // 시계방향으로 선 연결
                                }
                            }
                        }
                        else
                        {
                            g.DrawRectangle(pen, screenRect);
                        }
                    }

                    if (rectInfo.info != "")
                    {
                        float baseFontSize = 20.0f;

                        if (rectInfo.decision == DecisionType.Info)
                        {
                            baseFontSize = 3.0f;
                            lineColor = Color.LightBlue;
                        }

                        float fontSize = baseFontSize * _curZoom;

                        // 스코어 문자열 그리기 (우상단)
                        string infoText = rectInfo.info;
                        PointF textPos = new PointF(screenRect.Left, screenRect.Top); // 위로 약간 띄우기

                        if (rectInfo.inspectType == InspectType.InspBinary
                            && rectInfo.decision != DecisionType.Info)
                        {
                            textPos.Y = screenRect.Bottom - fontSize;
                        }

                        DrawText(g, infoText, textPos, fontSize, lineColor);
                    }
                }
            }
        }
        private void DrawText(Graphics g, string text, PointF position, float fontSize, Color color)
        {
            using (Font font = new Font("Arial", fontSize, FontStyle.Bold))
            // 테두리용 검정색 브러시
            using (Brush outlineBrush = new SolidBrush(Color.Black))
            // 본문용 노란색 브러시
            using (Brush textBrush = new SolidBrush(color))
            {
                // 테두리 효과를 위해 주변 8방향으로 그리기
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue; // 가운데는 제외
                        PointF borderPos = new PointF(position.X + dx, position.Y + dy);
                        g.DrawString(text, font, outlineBrush, borderPos);
                    }
                }

                // 본문 텍스트
                g.DrawString(text, font, textBrush, position);
            }
        }
        //위자드가 없어서 ImageViewCtrl에 직접 작성해서 생성
        //휠을 움직일 때 
        private void ImageViewCCtrl_MouseWheel(object sender, MouseEventArgs e) 
        {
            if (e.Delta < 0) ZoomMove(_curZoom/_zoomFactor,e.Location); //휠을 아래로 내렸을 때 ZomMove 함수 실행
            else ZoomMove(_curZoom*_zoomFactor,e.Location); //위로 올렸을 때 ZomMove 함수 실행

            if (_bitmapImage != null){   //새 이미지 위치 반영?
                ImageRect.Width = _bitmapImage.Width * _curZoom;
                ImageRect.Height = _bitmapImage.Height * _curZoom;
            }
            Invalidate();   //내부 함수, 화면 갱신 기능
        }
        private void ZoomMove(float zoom,Point zoomOrigin)  //Zoom 확대/축소 값 계산
        {
            PointF virtualOrigin=ScreenToVirtual(new PointF(zoomOrigin.X, zoomOrigin.Y));

            _curZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));  //Min, Max 값을 벗어나지 않게 설정
            if (_curZoom <= MinZoom) return;

            PointF zoomedOrigin = VirtualToScreen(virtualOrigin);   //?

            float dx=zoomedOrigin.X - zoomOrigin.X;     //?
            float dy=zoomedOrigin .Y - zoomOrigin.Y;    //?

            ImageRect.X -= dx;  //?
            ImageRect.Y-=dy;    //?
        }
        #region 좌표계 변환
        private PointF GetScreenOffset() //특정 지점이 화면 상에 얼마나 떨어져있는지
        {
            return new PointF(ImageRect.X, ImageRect.Y);
        }

        private Rectangle ScreenToVirtual(Rectangle screenRect)
        {
            PointF offset = GetScreenOffset();
            return new Rectangle(
                (int)((screenRect.X - offset.X) / _curZoom + 0.5f),
                (int)((screenRect.Y - offset.Y) / _curZoom + 0.5f),
                (int)(screenRect.Width / _curZoom + 0.5f),
                (int)(screenRect.Height / _curZoom + 0.5f));
        }

        private Rectangle VirtualToScreen(Rectangle virtualRect)
        {
            PointF offset = GetScreenOffset();
            return new Rectangle(
                (int)(virtualRect.X * _curZoom + offset.X + 0.5f),
                (int)(virtualRect.Y * _curZoom + offset.Y + 0.5f),
                (int)(virtualRect.Width * _curZoom + 0.5f),
                (int)(virtualRect.Height * _curZoom + 0.5f));
        }

        private PointF ScreenToVirtual(PointF screenPos) //창 외곽: Screen, 이미지 외곽: Virtual
        {
            PointF offset = GetScreenOffset();
            return new PointF(
                (screenPos.X-offset.X)/_curZoom,
                (screenPos.Y - offset.Y)/_curZoom);

        }

        private PointF VirtualToScreen(PointF virtualPos)   //창 외곽: Screen, 이미지 외곽: Virtual
        {
            PointF offset = GetScreenOffset();
            return new PointF(
                virtualPos.X *_curZoom + offset.X,
                virtualPos.Y * _curZoom + offset.Y);

        }
        #endregion

        private void ImageViewCtrl_Resize(object sender, EventArgs e)
        {
            ResizeCanvas();
            Invalidate();
        }

        private void ImageViewCtrl_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            FitImageToScreen();
        }

        //#8_INSPECT_BINARY#17 화면에 보여줄 영역 정보를 표시하기 위해, 위치 입력 받는 함수
        public void AddRect(List<DrawInspectInfo> rectInfos)
        {
            _rectInfos.AddRange(rectInfos);
            Invalidate();
        }

        public void ResetEntity()
        {
            _rectInfos.Clear();
            Invalidate();
        }
    }
}

