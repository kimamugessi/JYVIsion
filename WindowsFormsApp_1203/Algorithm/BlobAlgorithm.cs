using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JYVision.Algorithm
{
    public struct BinaryThreshold
    {
        public int lower { get; set; }
        public int upper { get; set; }
        public bool invert { get; set; }
    }
    public class BlobAlgorithm :InspAlgorithm
    {
        public BinaryThreshold BinThreshold {  get; set; }= new BinaryThreshold();
        public BlobAlgorithm() { IspectType = IspectType.inspBinary; }

        public override bool DoInspect()
        {
            ResetResult();
            IsInspected = true;
            return true;
        }
        public override void ResetResult()
        {
            base.ResetResult();
        }
    }
}
