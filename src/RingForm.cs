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
        internal const float BatteryGapDegrees = 11;
        internal static readonly Color BatteryTrack = Color.FromArgb(49, 53, 61);

        internal static void Draw(Graphics graphics, RingGeometry geometry, IList<RingAction> actions, int hovered, bool preview,
            bool editing = false, int battery = MouseBattery.Unknown)
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
            using (Font gaugeText = new Font("Segoe UI Semibold", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font gaugeIcon = new Font("Segoe MDL2 Assets", 17 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(Theme.Background);
                float margin = 4 * scale;
                graphics.DrawEllipse(border, margin, margin,
                    geometry.Bounds.Width - 2 * margin - 1, geometry.Bounds.Height - 2 * margin - 1);
                DrawBattery(graphics, geometry, battery, gaugeText, gaugeIcon);
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
                        command = shortcut.IsMediaKey ? "Media key" : shortcut.Display;
                    }
                    Text(graphics, command, small, secondary, new RectangleF(
                        bubble.X + 4 * scale, bubble.Y + 84 * scale, bubble.Width - 8 * scale, 16 * scale));
                }
                float hub = RingGeometry.HubRadius * scale;
                RectangleF core = new RectangleF(center.X - hub, center.Y - hub, 2 * hub, 2 * hub);
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

        internal static Color BatteryColor(int percent)
        {
            if (percent <= 10) return Color.FromArgb(232, 98, 86);
            if (percent <= 20) return Color.FromArgb(232, 170, 76);
            return Color.FromArgb(118, 200, 140);
        }

        // Clockwise sweep from just right of 12 o'clock; the gap at the top holds the readout.
        internal static float BatterySweep(int percent)
        {
            if (percent <= 0) return 0;
            return (360 - 2 * BatteryGapDegrees) * Math.Min(100, percent) / 100f;
        }

        internal static string BatteryDescription(int percent)
        {
            return percent >= 0 ? "Mouse battery " + percent + "%." : "Mouse battery level unavailable.";
        }

        private static void DrawBattery(Graphics graphics, RingGeometry geometry, int battery, Font font, Font glyphFont)
        {
            float scale = geometry.Scale;
            PointF center = geometry.Center;
            float radius = RingGeometry.GaugeRadius * scale;
            RectangleF arc = new RectangleF(center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
            float start = -90 + BatteryGapDegrees;
            using (Pen track = new Pen(BatteryTrack, RingGeometry.GaugeWidth * scale))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                graphics.DrawArc(track, arc, start, 360 - 2 * BatteryGapDegrees);
            }
            float sweep = BatterySweep(battery);
            if (sweep > 0)
            {
                using (Pen fill = new Pen(BatteryColor(battery), RingGeometry.GaugeWidth * scale))
                {
                    fill.StartCap = LineCap.Round;
                    fill.EndCap = LineCap.Round;
                    graphics.DrawArc(fill, arc, start, sweep);
                }
            }
            bool known = battery >= 0;
            string text = known ? battery + "%" : "--";
            // Segoe MDL2 Assets: Battery0-9 are U+E850-E859, Battery10 is U+E83F, BatteryUnknown is U+E996.
            int level = known ? (int)Math.Round(Math.Min(100, battery) / 10.0) : -1;
            string glyph = !known ? "\uE996" : level >= 10 ? "\uE83F" : ((char)(0xE850 + level)).ToString();
            SizeF textSize = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            float glyphWidth = 19 * scale, spacing = 3 * scale, height = 20 * scale;
            float left = center.X - (glyphWidth + spacing + textSize.Width) / 2;
            float top = center.Y - radius - height / 2;
            Text(graphics, glyph, glyphFont, known ? BatteryColor(battery) : Theme.Muted,
                new RectangleF(left, top, glyphWidth, height));
            float textCenter = left + glyphWidth + spacing + textSize.Width / 2;
            Text(graphics, text, font, known ? Theme.Text : Theme.Muted,
                new RectangleF(textCenter - textSize.Width / 2 - 8 * scale, top, textSize.Width + 16 * scale, height));
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
        private int battery = MouseBattery.Unknown;
        private bool preview;
        private int generation;
        internal event Action<RingAction, IntPtr, bool> ActionChosen;

        internal int BatteryPercent
        {
            get { return battery; }
            set
            {
                if (battery == value) return;
                battery = value;
                AccessibleDescription = Description;
                if (Visible) Invalidate();
            }
        }

        private string Description
        {
            get
            {
                return "Eight actions, clockwise from the top. Press 1 through 8 to choose, or Escape to cancel. " +
                    RingPainter.BatteryDescription(battery);
            }
        }

        internal RingForm()
        {
            Text = "MD301 Plus action ring";
            AccessibleName = "MD301 Plus action ring";
            AccessibleDescription = Description;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            focusTimer = new Timer { Interval = 50 };
            focusTimer.Tick += delegate
            {
                if (preview || source == IntPtr.Zero) return;
                IntPtr foreground = Native.GetForegroundWindow();
                if (Native.IsWindow(source) && foreground == source) return;
                AppLog.Note("Closed the action ring: " + Native.Describe(foreground) + " replaced " +
                    Native.Describe(source) + " in front.");
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
                // WS_EX_TOPMOST here instead of TopMost = true: WinForms focuses TopMost forms on Show,
                // which made the ring the foreground window and closed previews opened from Settings.
                parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008;
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
            int kind = message.Msg;
            if (capture != null && (kind == 0x0200 || kind == 0x0201 || kind == 0x0202 || kind == 0x0204 || kind == 0x0205))
            {
                int position = unchecked((int)message.LParam.ToInt64());
                Point client = new Point(unchecked((short)(position & 0xFFFF)), unchecked((short)((position >> 16) & 0xFFFF)));
                capture.WindowMouse(kind, PointToScreen(client));
                if (kind != 0x0200) return;
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
                Native.BringToTopmost(Handle);
                IntPtr cover;
                if (MostlyHidden(out cover))
                    throw new InvalidOperationException("Windows is showing " + Native.Describe(cover) +
                        " above every app, so the action ring would be hidden. Close Start, Search or Task View, then try again.");
                Invalidate();
                focusTimer.Start();
            }
            catch (Win32Exception)
            {
                CloseMenu();
                throw;
            }
            catch (InvalidOperationException)
            {
                CloseMenu();
                throw;
            }
        }

        // Start, Search and Task View are drawn above every always-on-top window. A ring hidden under them
        // would still capture the next click, so it is closed and reported instead.
        private bool MostlyHidden(out IntPtr cover)
        {
            cover = IntPtr.Zero;
            List<PointF> spots = new List<PointF> { geometry.Center };
            for (int i = 0; i < RingGeometry.ActionCount; i++)
            {
                RectangleF bubble = geometry.Bubble(i);
                spots.Add(new PointF(bubble.X + bubble.Width / 2, bubble.Y + bubble.Height / 2));
            }
            int hidden = 0;
            foreach (PointF spot in spots)
            {
                IntPtr top = Native.TopLevelWindowAt(new Point(geometry.Bounds.Left + (int)spot.X, geometry.Bounds.Top + (int)spot.Y));
                if (top == Handle) continue;
                hidden++;
                if (top != IntPtr.Zero) cover = top;
            }
            return hidden * 2 > spots.Count;
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
                if (capture != null && capture.Bypassed)
                    AppLog.Note("A click on the action ring bypassed the mouse listener (MD301 Plus was busy); the ring handled it directly.");
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
                RingPainter.Draw(e.Graphics, geometry, settings.Actions, hovered, preview, false, battery);
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
