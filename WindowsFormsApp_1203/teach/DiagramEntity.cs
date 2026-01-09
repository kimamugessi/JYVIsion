using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JYVision.Teach
{
    public class DiagramEntity
    {
        public InspWindow LinkedWindow { get; set; } //ROI에 연결된 InspWindow
        public Rectangle EntityROI { get; set; } //ROI 영역 정보
        public Color EntityColor { get; set; } //ROI 표시 색상
        public bool IsHold { get; set; }    //ROI 고정 여부

        public DiagramEntity()  //생성자에서 기본값 설정
        {
            LinkedWindow = null;
            EntityROI = new Rectangle(0, 0, 0, 0);
            EntityColor = Color.White;
            IsHold = false;
        }

        public DiagramEntity(Rectangle rect, Color entityColor, bool hold = false) //매개변수 있는 생성자
        {
            LinkedWindow = null;
            EntityROI = rect;
            EntityColor = entityColor;
            IsHold = hold;
        }
    }
}
