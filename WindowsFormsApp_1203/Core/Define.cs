using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JYVision.Core
{
    //#10_INSPWINDOW#1 검사 알고리즘 타입 추가
    public enum InspectType
    {
        InspNone = -1,
        InspBinary,
        InspMatch,
        InspFilter,
        InspAIModule,
        InspCount
    }

    //#10_INSPWINDOW#4 InspWindow 정의
    public enum InspWindowType
    {
        None = 0,
        Base,
        Body,
        Sub
    }

    public enum DecisionType
    {
        None = 0,
        Good,
        Defect,
        Info,
        Error,
        Timeout
    }
    public static class Define
    {
        public static readonly string ROI_IMAGE_NAME = "RoiImage.png";

        public static readonly string PROGRAM_NAME = "JYVision";
    }
}
