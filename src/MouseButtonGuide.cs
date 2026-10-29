using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MD301Plus
{
    internal enum EditorButton
    {
        Unassigned,
        Dpi,
        Back,
        Forward,
        Middle,
        Keyboard
    }

    internal sealed class MouseButtonGuide : Control
    {
        private const float CanvasWidth = 224;
        private const float CanvasHeight = 266;
        private EditorButton selected;
        private EditorButton hovered;
        internal event Action<EditorButton> ButtonChosen;
        internal EditorButton SelectedButton
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        internal MouseButtonGuide()
        {
            Name = "MouseButtonGuide";
            DoubleBuffered = true;
            BackColor = Theme.Background;
            AccessibleName = "ASUS ProArt MD301 button guide";
            AccessibleDescription = "Top view with thumb rest, thumb wheel and ridged center strip. The DPI switch is below the main scroll wheel.";
        }

        private float DrawingScale { get { return Math.Min(Width / CanvasWidth, Height / CanvasHeight); } }
        private float OffsetX { get { return (Width - CanvasWidth * DrawingScale) / 2; } }
        private float OffsetY { get { return (Height - CanvasHeight * DrawingScale) / 2; } }

        private static RectangleF Hotspot(EditorButton button)
        {
            switch (button)
            {
                case EditorButton.Dpi: return new RectangleF(118, 91, 34, 48);
                case EditorButton.Middle: return new RectangleF(118, 36, 34, 48);
                case EditorButton.Back: return new RectangleF(43, 126, 28, 27);
                case EditorButton.Forward: return new RectangleF(43, 96, 28, 27);
                default: throw new ArgumentOutOfRangeException("button");
            }
        }

        internal Rectangle ButtonBounds(EditorButton button)
        {
            RectangleF area = Hotspot(button);
            float scale = DrawingScale;
            return Rectangle.Round(new RectangleF(OffsetX + area.X * scale, OffsetY + area.Y * scale,
                area.Width * scale, area.Height * scale));
        }

        internal EditorButton ButtonAt(Point point)
        {
            if (DrawingScale <= 0) return EditorButton.Unassigned;
            foreach (EditorButton button in new[] { EditorButton.Dpi, EditorButton.Middle, EditorButton.Back, EditorButton.Forward })
                if (ButtonBounds(button).Contains(point)) return button;
            return EditorButton.Unassigned;
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            EditorButton[] buttons = { EditorButton.Dpi, EditorButton.Back, EditorButton.Forward, EditorButton.Middle };
            string[] names = { "DPI switch below the scroll wheel", "Back side button", "Forward side button", "Scroll-wheel click" };
            return new ActionAccessibility(this, 4,
                delegate(int index) { return names[index]; },
                delegate(int index) { return ButtonBounds(buttons[index]); },
                delegate(int index)
                {
                    Action<EditorButton> handler = ButtonChosen;
                    if (handler != null) handler(buttons[index]);
                }, delegate(int index) { return selected == buttons[index]; });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float scale = DrawingScale;
            if (scale <= 0) return;
            Graphics graphics = e.Graphics;
            graphics.TranslateTransform(OffsetX, OffsetY);
            graphics.ScaleTransform(scale, scale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (GraphicsPath body = Body())
            using (LinearGradientBrush shell = new LinearGradientBrush(new RectangleF(22, 12, 178, 246),
                Color.FromArgb(78, 84, 94), Color.FromArgb(28, 32, 39), 35))
            using (Pen edge = new Pen(Color.FromArgb(107, 114, 127), 1.4f))
            using (Pen seam = new Pen(Color.FromArgb(19, 22, 28), 2))
            using (Pen highlight = new Pen(Color.FromArgb(122, 130, 143), 0.7f))
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                {
                    GraphicsState state = graphics.Save();
                    graphics.TranslateTransform(3, 5);
                    graphics.FillPath(shadow, body);
                    graphics.Restore(state);
                }
                graphics.FillPath(shell, body);
                graphics.DrawPath(edge, body);
                DrawThumbGrip(graphics);

                using (GraphicsPath left = new GraphicsPath())
                using (GraphicsPath right = new GraphicsPath())
                using (LinearGradientBrush paddles = new LinearGradientBrush(new RectangleF(59, 15, 137, 130),
                    Color.FromArgb(83, 89, 99), Color.FromArgb(44, 49, 59), 90))
                {
                    left.AddBezier(122, 16, 86, 13, 61, 17, 61, 51);
                    left.AddBezier(61, 51, 61, 86, 59, 122, 56, 144);
                    left.AddBezier(56, 144, 78, 140, 101, 140, 122, 141);
                    left.CloseFigure();
                    right.AddBezier(147, 16, 179, 17, 193, 24, 195, 55);
                    right.AddBezier(195, 55, 198, 83, 197, 118, 190, 141);
                    right.AddBezier(190, 141, 174, 143, 162, 141, 147, 141);
                    right.CloseFigure();
                    graphics.FillPath(paddles, left);
                    graphics.FillPath(paddles, right);
                    graphics.DrawPath(seam, left);
                    graphics.DrawPath(seam, right);
                    graphics.DrawBezier(highlight, 64, 46, 65, 20, 92, 17, 118, 18);
                    graphics.DrawBezier(highlight, 150, 18, 176, 19, 190, 27, 192, 50);
                }
                using (GraphicsPath spine = Theme.RoundedRectangle(new RectangleF(124, 17, 21, 124), 3))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(22, 25, 30)))
                    graphics.FillPath(dark, spine);

                DrawRibbedButton(graphics, new RectangleF(126, 18, 17, 18), EditorButton.Unassigned, false);
                DrawRibbedButton(graphics, new RectangleF(126, 42, 17, 40), EditorButton.Middle, true);
                DrawRibbedButton(graphics, new RectangleF(126, 94, 17, 43), EditorButton.Dpi, false);
                DrawRibbedButton(graphics, new RectangleF(36, 103, 10, 28), EditorButton.Unassigned, true);
                DrawRibbedButton(graphics, new RectangleF(54, 99, 9, 21), EditorButton.Forward, false);
                DrawRibbedButton(graphics, new RectangleF(54, 129, 9, 21), EditorButton.Back, false);

                using (Pen line = new Pen(Theme.Accent, 1.2f))
                using (Font label = new Font("Segoe UI Semibold", 12, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font model = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush accent = new SolidBrush(Theme.Accent))
                using (SolidBrush muted = new SolidBrush(Theme.Muted))
                {
                    graphics.DrawLine(line, 149, 113, 193, 113);
                    graphics.DrawString("DPI", label, accent, 195, 104);
                    graphics.DrawString("ProArt", label, muted, 105, 223);
                    graphics.DrawString("MD301", model, muted, 106, 239);
                }
            }
        }

        private static GraphicsPath Body()
        {
            GraphicsPath path = new GraphicsPath();
            path.AddBezier(122, 15, 86, 10, 60, 16, 59, 53);
            path.AddBezier(59, 53, 58, 68, 61, 86, 54, 96);
            path.AddBezier(54, 96, 27, 103, 24, 122, 23, 154);
            path.AddBezier(23, 154, 17, 199, 31, 228, 70, 247);
            path.AddBezier(70, 247, 113, 271, 163, 257, 185, 225);
            path.AddBezier(185, 225, 209, 195, 201, 168, 199, 126);
            path.AddBezier(199, 126, 197, 96, 200, 66, 195, 43);
            path.AddBezier(195, 43, 190, 18, 170, 15, 147, 16);
            path.CloseFigure();
            return path;
        }

        private static void DrawThumbGrip(Graphics graphics)
        {
            using (Pen wave = new Pen(Color.FromArgb(65, 73, 86), 1.1f))
            using (Pen seam = new Pen(Color.FromArgb(24, 28, 35), 1.5f))
            {
                graphics.DrawBezier(seam, 56, 145, 57, 178, 67, 220, 92, 246);
                for (int i = 0; i < 7; i++)
                {
                    float y = 157 + 8 * i;
                    graphics.DrawBezier(wave, 29, y, 35, y - 5, 42, y - 5, 53 + i, y + 3);
                }
            }
        }

        private void DrawRibbedButton(Graphics graphics, RectangleF area, EditorButton button, bool wheel)
        {
            bool active = button != EditorButton.Unassigned && selected == button;
            bool hot = button != EditorButton.Unassigned && hovered == button;
            Color top = active ? Theme.Accent : hot ? Color.FromArgb(151, 155, 164) : Color.FromArgb(112, 117, 127);
            Color bottom = active ? Color.FromArgb(174, 132, 77) : Color.FromArgb(41, 46, 56);
            using (GraphicsPath path = Theme.RoundedRectangle(area, wheel ? 4 : 2))
            using (LinearGradientBrush fill = new LinearGradientBrush(area, top, bottom, 0f))
            using (Pen edge = new Pen(active || hot ? Theme.Accent : Color.FromArgb(130, 137, 149), active ? 1.8f : 0.7f))
            using (Pen groove = new Pen(active ? Color.FromArgb(123, 87, 43) : Color.FromArgb(35, 40, 49), 0.65f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(edge, path);
                for (float x = area.Left + 3; x < area.Right - 1; x += 3)
                    graphics.DrawLine(groove, x, area.Top + 2, x, area.Bottom - 2);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            EditorButton next = ButtonAt(e.Location);
            Cursor = next == EditorButton.Unassigned ? Cursors.Default : Cursors.Hand;
            if (hovered != next) { hovered = next; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = EditorButton.Unassigned;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            EditorButton button = ButtonAt(e.Location);
            Action<EditorButton> handler = ButtonChosen;
            if (e.Button == MouseButtons.Left && button != EditorButton.Unassigned && handler != null)
                handler(button);
        }
    }
}
