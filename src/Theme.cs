using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MD301Plus
{
    internal static class Theme
    {
        internal const float BodyFontSize = 11;
        internal static readonly Color Background = Color.FromArgb(23, 26, 32);
        internal static readonly Color Surface = Color.FromArgb(37, 42, 51);
        internal static readonly Color Hover = Color.FromArgb(49, 56, 67);
        internal static readonly Color Border = Color.FromArgb(75, 84, 98);
        internal static readonly Color Text = Color.FromArgb(244, 246, 249);
        internal static readonly Color Muted = Color.FromArgb(186, 194, 206);
        internal static readonly Color Accent = Color.FromArgb(224, 187, 133);
        internal static readonly Color Selection = Color.FromArgb(66, 57, 44);

        internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void DrawButton(Graphics graphics, Control control, bool focus)
        {
            float scale = control.DeviceDpi / 96f;
            bool hovered = control.Enabled && control.ClientRectangle.Contains(control.PointToClient(Cursor.Position));
            bool pressed = control.Capture && Control.MouseButtons == MouseButtons.Left;
            bool primary = control.BackColor == Accent;
            Color fill = primary ? Accent : hovered ? Hover : Surface;
            if (pressed) fill = primary ? Color.FromArgb(204, 165, 111) : Background;
            if (!control.Enabled) fill = Surface;
            Color foreground = !control.Enabled ? Muted : primary ? Background : Text;
            graphics.Clear(control.Parent == null ? Background : control.Parent.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(scale, scale,
                Math.Max(1, control.Width - 2 * scale - 1), Math.Max(1, control.Height - 2 * scale - 1));
            using (GraphicsPath path = RoundedRectangle(bounds, 10 * scale))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen border = new Pen(focus || primary ? Accent : Border, focus ? 2 * scale : scale))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(border, path);
            }
            Rectangle text = Rectangle.Inflate(control.ClientRectangle, -(int)(8 * scale), -(int)(3 * scale));
            ButtonBase button = control as ButtonBase;
            bool mnemonic = button != null && button.UseMnemonic;
            TextRenderer.DrawText(graphics, control.Text, control.Font, text, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                (mnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix));
            if (focus)
            {
                RectangleF inner = RectangleF.Inflate(bounds, -4 * scale, -4 * scale);
                using (GraphicsPath path = RoundedRectangle(inner, 6 * scale))
                using (Pen pen = new Pen(foreground, scale) { DashStyle = DashStyle.Dot })
                    graphics.DrawPath(pen, path);
            }
        }

        internal static void DrawCard(Graphics graphics, Control control, bool focused)
        {
            float scale = control.DeviceDpi / 96f;
            graphics.Clear(control.Parent == null ? Background : control.Parent.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRectangle(new RectangleF(scale / 2, scale / 2,
                Math.Max(1, control.Width - scale - 1), Math.Max(1, control.Height - scale - 1)), 12 * scale))
            using (SolidBrush brush = new SolidBrush(control.BackColor))
            using (Pen border = new Pen(focused ? Accent : Border, scale))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        internal RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e) { Theme.DrawButton(e.Graphics, this, Focused && ShowFocusCues); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    internal sealed class RoundedTableLayoutPanel : TableLayoutPanel
    {
        internal RoundedTableLayoutPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Theme.DrawCard(e.Graphics, this, false); }
    }

    internal sealed class RoundedField : Panel
    {
        private readonly TextBox input;

        internal RoundedField(TextBox input)
        {
            this.input = input;
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            input.BorderStyle = BorderStyle.None;
            input.Dock = DockStyle.None;
            input.BackColor = Theme.Surface;
            input.ForeColor = Theme.Text;
            Controls.Add(input);
            input.GotFocus += delegate { Invalidate(); };
            input.LostFocus += delegate { Invalidate(); };
            input.FontChanged += delegate { PerformLayout(); };
            Height = 38;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (input == null) return;
            int inset = 11 * DeviceDpi / 96;
            int height = input.PreferredHeight;
            input.SetBounds(inset, Math.Max(0, (Height - height) / 2), Math.Max(1, Width - 2 * inset), height);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Theme.DrawCard(e.Graphics, this, ContainsFocus); }
    }
}
