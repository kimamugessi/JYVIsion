using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JYVision.Core;
using OpenCvSharp;

namespace JYVision.Algorithm
{
    public class DrawInspectInfo
    {
        public Rect rect;
        public Point2f[] rotatedPoints;
        public string info;
        public InspectType inspectType;
        public DecisionType decision;
        public bool UseRotatedRect=false;

        public DrawInspectInfo()
        {
            rect = new Rect();
            rotatedPoints = null;
            info=string.Empty;
            inspectType = InspectType.InspNone;
            decision=DecisionType.None;
        }

        public DrawInspectInfo(Rect _rect, string _info, InspectType _inspectType, DecisionType _decision)
        {
            rect = _rect;
            info = _info;
            inspectType = _inspectType;
            decision = _decision;
        }

        public void SetRotatedRectPoints(Point2f[] _rotatedPoints)
        {
            if (_rotatedPoints == null) return;
            rotatedPoints = new Point2f[_rotatedPoints.Length];
            for (int i = 0; i < _rotatedPoints.Length; i++)
            {
                rotatedPoints[i] = _rotatedPoints[i];
            }
            UseRotatedRect = true;
        }
    }
}
