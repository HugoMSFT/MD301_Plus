using System;
using System.Drawing;

namespace MD301Plus
{
    public sealed class RingGeometry
    {
        public const int BaseSize = 440;
        public const int ActionCount = 8;
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

        public int HitTest(Point screenPoint)
        {
            float x = screenPoint.X - Bounds.Left;
            float y = screenPoint.Y - Bounds.Top;
            for (int i = 0; i < ActionCount; i++)
            {
                RectangleF bubble = Bubble(i);
                float dx = x - (bubble.X + bubble.Width / 2);
                float dy = y - (bubble.Y + bubble.Height / 2);
                if (dx * dx + dy * dy <= bubble.Width * bubble.Width / 4)
                    return i;
            }
            return -1;
        }
    }
}
