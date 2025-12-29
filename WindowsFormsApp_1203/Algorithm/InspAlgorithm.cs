using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace JYVision.Algorithm
{
    public enum IspectType
    {
        InspNone = -1, inspBinary, inspCount
    }
    public abstract class InspAlgorithm
    {
        public IspectType IspectType { get; set; }= IspectType.InspNone;

        public bool IsUse { get; set; }= true;
        public bool IsInspected { get; set;}= false;
        protected Mat _srcImage = null;
        public List<string> ResultString { get; set; }= new List<string>();

        public bool IsDefect { get; set; }

        public abstract bool DoInspect();

        public virtual void ResetResult()
        {
            IsInspected = false;
            IsDefect = false;
            ResultString.Clear();
        }
    }
}
