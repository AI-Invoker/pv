// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AI-Invoker
using System;
using System.Drawing;

namespace PV
{
    internal static class ImagePan
    {
        internal static PointF Limits(SizeF rendered,Size viewport)
        {
            return new PointF(Math.Max(0,(rendered.Width-Math.Max(1,viewport.Width))/2),Math.Max(0,(rendered.Height-Math.Max(1,viewport.Height))/2));
        }
        internal static bool CanPan(SizeF rendered,Size viewport)
        {
            PointF limits=Limits(rendered,viewport);return limits.X>.01f||limits.Y>.01f;
        }
        private static float ClampAxis(float value,float limit)
        {
            return float.IsNaN(value)||float.IsInfinity(value)?0:Math.Max(-limit,Math.Min(limit,value));
        }
        internal static PointF Clamp(PointF offset,SizeF rendered,Size viewport)
        {
            PointF limits=Limits(rendered,viewport);return new PointF(ClampAxis(offset.X,limits.X),ClampAxis(offset.Y,limits.Y));
        }
        internal static RectangleF Bounds(SizeF rendered,Size viewport,PointF offset)
        {
            return new RectangleF((viewport.Width-rendered.Width)/2+offset.X,(viewport.Height-rendered.Height)/2+offset.Y,rendered.Width,rendered.Height);
        }
    }
}
