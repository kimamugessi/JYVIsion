using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JYVision.Core
{
    public enum InspWindowType
    {
        None = 0,
        Base,
        Body,
        Sub
    }

    public enum DecisionType
    {
        None=0,
        Good,
        Defect,
        Info,
        Error,
        TImeout
    }
    public static class Define
    {
        public static readonly string ROI_IMAGE_NAME = "RoiImage.png";
    }
}
