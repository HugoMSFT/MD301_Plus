using System;
using System.Drawing;

namespace MD301Plus
{
    public sealed class RingGeometry
    {
        public const int BaseSize = 480;
        public const int ActionCount = 8;
        // The battery gauge circles the bubbles (outer edge 206) inside the window border (236).
        public const float GaugeRadius = 221;
        public const float GaugeWidth = 8;
        // The center hub shows the ring's title; a click there cancels.
        public const float HubRadius = 59;
        public Rectangle Bounds { get; private set; }
        public float Scale { get; private set; }
        public PointF Center { get { return new PointF(Bounds.Width / 2f, Bounds.Height / 2f); } }

        public RingGeometry(Point pointer, Rectangle workingArea, float dpiScale)
        {
            if (workingArea.Width <= 0 || workingArea.Height <= 0 ||
                dpiScale <= 0 || Single.IsNaN(dpiScale) || Single.IsInfinity(dpiScale))
                throw new ArgumentOutOfRangeException("dpiScale", "A positive working area and DPI scale are required.");
            Scale = Math.Min(dpiScale, Math.Min(workingArea.Width / (float)BaseSize, workingArea.Height / (float)BaseSize));
            int size = Math.Max(1, (int)Math.Floor(BaseSize * Scale));
            int left = Math.Max(workingArea.Left, Math.Min(pointer.X - size / 2, workingArea.Right - size));
            int top = Math.Max(workingArea.Top, Math.Min(pointer.Y - size / 2, workingArea.Bottom - size));
            Bounds = new Rectangle(left, top, size, size);
        }

        public RectangleF Bubble(int index)
        {
            if (index < 0 || index >= ActionCount)
                throw new ArgumentOutOfRangeException("index");
            double angle = (index * 45 - 90) * Math.PI / 180;
            float radius = 154 * Scale;
            float diameter = 104 * Scale;
            return new RectangleF(
                Center.X + (float)Math.Cos(angle) * radius - diameter / 2,
                Center.Y + (float)Math.Sin(angle) * radius - diameter / 2,
                diameter, diameter);
        }

        // Pie-menu hit test: every point on the ring outside the hub belongs to the action in that direction,
        // so a click between two bubbles or beside a bubble still selects it instead of silently canceling.
        public int HitTest(Point screenPoint)
        {
            float dx = screenPoint.X - Bounds.Left - Center.X;
            float dy = screenPoint.Y - Bounds.Top - Center.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= HubRadius * Scale || distance > Bounds.Width / 2.0)
                return -1;
            double degreesFromTop = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
            int index = (int)Math.Floor((degreesFromTop + 180.0 / ActionCount) / (360.0 / ActionCount));
            return (index % ActionCount + ActionCount) % ActionCount;
        }
    }
}
