using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using JYVision.Core;

namespace JYVision.Algorithm
{
    public enum InspectType
    {
        InspNone = -1, InspBinary, InspFilter, InspAlModule,InspCount
    }
    public abstract class InspAlgorithm
    {
        public InspectType InspectType { get; set; }= InspectType.InspNone;

        public bool IsUse { get; set; }= true;
        public bool IsInspected { get; set;}= false;

        public Rect TeachRect { get; set; }

        public Rect InspRect { get; set; }

        protected Mat _srcImage = null;
        public List<string> ResultString { get; set; }= new List<string>();

        public bool IsDefect { get; set; }

        public abstract InspAlgorithm Clone();
        public abstract bool CopyFrom(InspAlgorithm sourceAlgo);

        protected void CopyBaseTo(InspAlgorithm target)
        {
            target.InspectType = this.InspectType;
            target.IsUse = this.IsUse;
            target.IsInspected = this.IsInspected;
            target.TeachRect = this.TeachRect;
            target.InspRect = this.InspRect;
            //_srcImage 는 런타임 검사용이라 복사하지 않음
        }

        public virtual void SetInspData(Mat srcImage) { _srcImage = srcImage; }

        public abstract bool DoInspect();

        public virtual void ResetResult()
        {
            IsInspected = false;
            IsDefect = false;
            ResultString.Clear();
        }

        public virtual int GetResultRect(out List<DrawInspectInfo> resultArea)
        {
            resultArea = null;
            return 0;
        }
    }
}
