using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MD301Plus
{
    internal static class RingPainter
    {
        internal static void Draw(Graphics graphics, RingGeometry geometry, IList<RingAction> actions, int hovered, bool preview, bool editing = false)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float scale = geometry.Scale;
            PointF center = geometry.Center;
            using (Pen border = new Pen(Theme.Border, Math.Max(1, scale)))
            using (Pen orbit = new Pen(Color.FromArgb(49, 53, 61), 2 * scale))
            using (SolidBrush surface = new SolidBrush(Theme.Surface))
            using (SolidBrush accent = new SolidBrush(Theme.Accent))
            using (SolidBrush background = new SolidBrush(Theme.Background))
            using (Font icon = new Font("Segoe MDL2 Assets", 26 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font label = new Font("Segoe UI Semibold", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font title = new Font("Segoe UI Semibold", 18 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.Clear(Theme.Background);
                float margin = 4 * scale;
                graphics.DrawEllipse(border, margin, margin,
                    geometry.Bounds.Width - 2 * margin - 1, geometry.Bounds.Height - 2 * margin - 1);
                graphics.DrawEllipse(orbit, center.X - 154 * scale, center.Y - 154 * scale, 308 * scale, 308 * scale);
                for (int i = 0; i < RingGeometry.ActionCount; i++)
                {
                    RectangleF bubble = geometry.Bubble(i);
                    bool selected = i == hovered;
                    graphics.FillEllipse(selected ? accent : surface, bubble);
                    graphics.DrawEllipse(border, bubble);
                    Color foreground = selected ? Theme.Background : Theme.Text;
                    Color secondary = selected ? Color.FromArgb(67, 52, 33) : Theme.Muted;
                    Text(graphics, (i + 1).ToString(), small, secondary, new RectangleF(
                        bubble.X, bubble.Y + 5 * scale, bubble.Width, 13 * scale));
                    Text(graphics, ActionIcons.Glyph(actions[i].Icon), icon, foreground, new RectangleF(
                        bubble.X, bubble.Y + 22 * scale, bubble.Width, 28 * scale));
                    Text(graphics, actions[i].Label, label, foreground, new RectangleF(
                        bubble.X + 4 * scale, bubble.Y + 46 * scale, bubble.Width - 8 * scale, 38 * scale), true);
                    string command = "Open";
                    if (actions[i].Kind == "shortcut")
                    {
                        KeyboardShortcut shortcut = KeyboardShortcut.Parse(actions[i].Command, false);
                        command = shortcut.Modifiers == 0 &&
                            ((shortcut.Key >= 0xAD && shortcut.Key <= 0xB3) || shortcut.Key == 0xB5) ?
                            "Media key" : shortcut.Display;
                    }
                    Text(graphics, command, small, secondary, new RectangleF(
                        bubble.X + 4 * scale, bubble.Y + 84 * scale, bubble.Width - 8 * scale, 16 * scale));
                }
                RectangleF core = new RectangleF(center.X - 59 * scale, center.Y - 59 * scale, 118 * scale, 118 * scale);
                graphics.FillEllipse(background, core);
                graphics.DrawEllipse(border, core);
                Text(graphics, "MD301 +", title, Theme.Text, new RectangleF(
                    core.X, center.Y - 29 * scale, core.Width, 28 * scale));
                Text(graphics, editing ? "YOUR RING" : preview ? "PREVIEW" : "ACTION RING", small, Theme.Accent, new RectangleF(
                    core.X, center.Y + 1 * scale, core.Width, 15 * scale));
                Text(graphics, hovered >= 0 ? actions[hovered].Label : editing ? "Drag actions here" : "Click / 1-8", small, Theme.Muted, new RectangleF(
                    core.X + 5 * scale, center.Y + 21 * scale, core.Width - 10 * scale, 16 * scale));
                Text(graphics, editing ? "Click to edit" : "Esc closes", small, Theme.Muted, new RectangleF(
                    core.X, center.Y + 38 * scale, core.Width, 14 * scale));
            }
        }

        private static void Text(Graphics graphics, string text, Font font, Color color, RectangleF area, bool wrap = false)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = wrap ? StringFormatFlags.LineLimit : StringFormatFlags.NoWrap
            })
                graphics.DrawString(text, font, brush, area, format);
        }
    }

    internal sealed class RingForm : Form
    {
        private readonly Timer focusTimer;
        private RingGeometry geometry;
        private RingSettings settings;
        private MenuInputCapture capture;
        private IntPtr source;
        private int hovered = -1;
        private bool preview;
        private int generation;
        internal event Action<RingAction, IntPtr, bool> ActionChosen;

        internal RingForm()
        {
            Text = "MD301 Plus action ring";
            AccessibleName = "MD301 Plus action ring";
            AccessibleDescription = "Eight actions, clockwise from the top. Press 1 through 8 to choose, or Escape to cancel.";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            focusTimer = new Timer { Interval = 50 };
            focusTimer.Tick += delegate
            {
                if (source != IntPtr.Zero && (!Native.IsWindow(source) || Native.GetForegroundWindow() != source))
                    CloseMenu();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ActionAccessibility(this, 8,
                delegate(int index) { return settings == null ? "Position " + (index + 1) : settings.Actions[index].Label; },
                delegate(int index) { return geometry == null ? Rectangle.Empty : Rectangle.Round(geometry.Bubble(index)); },
                CompleteSelection);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080;
                return parameters;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021)
            {
                message.Result = new IntPtr(3);
                return;
            }
            base.WndProc(ref message);
        }

        internal void ShowMenu(RingSettings next, IntPtr target, bool isPreview, TriggerBinding activeTrigger = null)
        {
            next.Validate();
            CloseMenu();
            Point pointer = Cursor.Position;
            geometry = new RingGeometry(pointer, Screen.FromPoint(pointer).WorkingArea, Native.DpiScale(pointer));
            settings = next.Copy();
            source = target;
            preview = isPreview;
            hovered = -1;
            Bounds = geometry.Bounds;
            using (GraphicsPath path = new GraphicsPath())
            {
                float inset = 3 * geometry.Scale;
                path.AddEllipse(inset, inset, Width - 2 * inset, Height - 2 * inset);
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
            TriggerBinding trigger = activeTrigger ?? TriggerBinding.Parse(settings.Trigger);
            capture = new MenuInputCapture(geometry.HitTest, SetHovered, CompleteSelection, trigger);
            try
            {
                capture.Start();
                Show();
                Invalidate();
                focusTimer.Start();
            }
            catch (Win32Exception)
            {
                CloseMenu();
                throw;
            }
        }

        private void SetHovered(int index)
        {
            if (hovered == index) return;
            hovered = index;
            Invalidate();
        }

        internal void CompleteSelection(int index)
        {
            if (index < -1 || index >= RingGeometry.ActionCount)
                throw new ArgumentOutOfRangeException("index");
            if (!IsHandleCreated || IsDisposed) return;
            int expectedGeneration = generation;
            BeginInvoke(new Action(delegate
            {
                if (!Visible || generation != expectedGeneration) return;
                RingAction action = index >= 0 ? settings.Actions[index] : null;
                IntPtr target = source;
                bool wasPreview = preview;
                CloseMenu();
                Action<RingAction, IntPtr, bool> handler = ActionChosen;
                if (action != null && handler != null) handler(action, target, wasPreview);
            }));
        }

        internal void CloseMenu()
        {
            unchecked { generation++; }
            focusTimer.Stop();
            Hide();
            if (capture != null)
            {
                capture.Dispose();
                capture = null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (geometry != null)
                RingPainter.Draw(e.Graphics, geometry, settings.Actions, hovered, preview);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                CloseMenu();
                focusTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
