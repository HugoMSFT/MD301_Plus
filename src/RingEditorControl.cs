using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MD301Plus
{
    internal sealed class ActionDragSession
    {
        private string token;
        private RingAction action;
        private int sourceSlot;
        internal bool IsActive { get { return action != null; } }

        internal DataObject Begin(RingAction value, int source)
        {
            if (value == null) throw new ArgumentNullException("value");
            value.Validate(0);
            if (source < -1 || source >= 8) throw new ArgumentOutOfRangeException("source");
            token = "MD301Plus.Action:" + Guid.NewGuid().ToString("N");
            action = value.Copy();
            sourceSlot = source;
            DataObject data = new DataObject();
            data.SetData(DataFormats.UnicodeText, token);
            return data;
        }

        internal bool TryRead(IDataObject data, out RingAction value, out int source)
        {
            value = null;
            source = -1;
            if (action == null || data == null || !data.GetDataPresent(DataFormats.UnicodeText, false))
                return false;
            string text = data.GetData(DataFormats.UnicodeText, false) as string;
            if (!String.Equals(text, token, StringComparison.Ordinal)) return false;
            value = action.Copy();
            source = sourceSlot;
            return true;
        }

        internal void Drag(Control owner, RingAction value, int source)
        {
            try { owner.DoDragDrop(Begin(value, source), DragDropEffects.Copy | DragDropEffects.Move); }
            finally { End(); }
        }

        internal void End()
        {
            token = null;
            action = null;
            sourceSlot = -1;
        }
    }

    internal sealed class RingEditorControl : Control
    {
        private readonly ActionDragSession drag;
        private List<RingAction> actions;
        private Point downPoint;
        private int downSlot = -1;
        private int dropSlot = -1;
        internal int SelectedSlot { get; private set; }
        internal event Action Changed;
        internal event Action SelectionChanged;
        internal event Action EditRequested;

        internal RingEditorControl(ActionDragSession drag, IList<RingAction> values)
        {
            this.drag = drag;
            Name = "RingEditor";
            BackColor = Theme.Background;
            DoubleBuffered = true;
            AllowDrop = true;
            TabStop = true;
            AccessibleName = "Eight-position action ring editor";
            AccessibleDescription = "Drag an action from the palette onto a circle. Drag between circles to swap positions. Arrow keys select a position.";
            SetActions(values);
        }

        internal RingAction ActionAt(int slot)
        {
            if (slot < 0 || slot >= 8) throw new ArgumentOutOfRangeException("slot");
            return actions[slot].Copy();
        }

        internal List<RingAction> CopyActions()
        {
            List<RingAction> result = new List<RingAction>();
            foreach (RingAction action in actions) result.Add(action.Copy());
            return result;
        }

        internal void SetActions(IList<RingAction> values)
        {
            if (values == null || values.Count != 8) throw new SettingsException("The editor needs eight actions.");
            List<RingAction> next = new List<RingAction>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null) throw new SettingsException("A ring position is missing its action.");
                values[i].Validate(i);
                next.Add(values[i].Copy());
            }
            actions = next;
            Invalidate();
        }

        internal void SelectSlot(int slot)
        {
            if (slot < 0 || slot >= 8) throw new ArgumentOutOfRangeException("slot");
            SelectedSlot = slot;
            Invalidate();
            Action handler = SelectionChanged;
            if (handler != null) handler();
        }

        internal void Assign(int slot, RingAction action)
        {
            if (slot < 0 || slot >= 8) throw new ArgumentOutOfRangeException("slot");
            action.Validate(slot);
            actions[slot] = action.Copy();
            SelectSlot(slot);
            RaiseChanged();
        }

        internal bool DropOnSlot(int slot, IDataObject data)
        {
            if (slot < 0 || slot >= 8) throw new ArgumentOutOfRangeException("slot");
            RingAction action;
            int source;
            if (!drag.TryRead(data, out action, out source)) return false;
            if (source >= 0)
            {
                RingAction previous = actions[slot];
                actions[slot] = actions[source];
                actions[source] = previous;
                SelectSlot(slot);
                RaiseChanged();
            }
            else
            {
                Assign(slot, action);
            }
            return true;
        }

        internal void MoveSelected(int direction)
        {
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException("direction");
            int next = (SelectedSlot + direction + 8) % 8;
            RingAction previous = actions[next];
            actions[next] = actions[SelectedSlot];
            actions[SelectedSlot] = previous;
            SelectSlot(next);
            RaiseChanged();
        }

        private RingGeometry Geometry()
        {
            return new RingGeometry(new Point(Width / 2, Height / 2),
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                Math.Max(1, Math.Min(Width, Height)) / (float)RingGeometry.BaseSize);
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ActionAccessibility(this, 9,
                delegate(int index) { return index == 8 ? "Edit selected action" : "Position " + (index + 1) + ": " + actions[index].Label; },
                delegate(int index)
                {
                    RingGeometry layout = Geometry();
                    Rectangle bounds = index == 8 ? Rectangle.Round(new RectangleF(
                        layout.Center.X - 59 * layout.Scale, layout.Center.Y - 59 * layout.Scale,
                        118 * layout.Scale, 118 * layout.Scale)) : Rectangle.Round(layout.Bubble(index));
                    bounds.Offset(layout.Bounds.Location);
                    return bounds;
                }, delegate(int index) { if (index == 8) RequestEdit(); else SelectSlot(index); },
                delegate(int index) { return index == SelectedSlot; });
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RingGeometry geometry = Geometry();
            e.Graphics.TranslateTransform(geometry.Bounds.Left, geometry.Bounds.Top);
            RingPainter.Draw(e.Graphics, geometry, actions, dropSlot >= 0 ? dropSlot : SelectedSlot, false, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            downSlot = e.Button == MouseButtons.Left ? Geometry().HitTest(e.Location) : -1;
            downPoint = e.Location;
            if (downSlot >= 0) SelectSlot(downSlot);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (downSlot < 0 || e.Button != MouseButtons.Left) return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - downPoint.X) < threshold.Width / 2 &&
                Math.Abs(e.Y - downPoint.Y) < threshold.Height / 2) return;
            int source = downSlot;
            downSlot = -1;
            drag.Drag(this, actions[source], source);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            downSlot = -1;
            base.OnMouseUp(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            RingGeometry layout = Geometry();
            float dx = e.X - layout.Bounds.Left - layout.Center.X;
            float dy = e.Y - layout.Bounds.Top - layout.Center.Y;
            if (e.Button == MouseButtons.Left && dx * dx + dy * dy <= 59 * 59 * layout.Scale * layout.Scale)
                RequestEdit();
        }

        private void RequestEdit()
        {
            Action handler = EditRequested;
            if (handler != null) handler();
        }

        protected override void OnDragEnter(DragEventArgs e) { UpdateDrop(e); }
        protected override void OnDragOver(DragEventArgs e) { UpdateDrop(e); }

        private void UpdateDrop(DragEventArgs e)
        {
            RingAction value;
            int source;
            dropSlot = Geometry().HitTest(PointToClient(new Point(e.X, e.Y)));
            e.Effect = dropSlot >= 0 && drag.TryRead(e.Data, out value, out source) ?
                (source >= 0 ? DragDropEffects.Move : DragDropEffects.Copy) : DragDropEffects.None;
            Invalidate();
        }

        protected override void OnDragLeave(EventArgs e)
        {
            dropSlot = -1;
            Invalidate();
            base.OnDragLeave(e);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            int slot = Geometry().HitTest(PointToClient(new Point(e.X, e.Y)));
            if (slot >= 0) DropOnSlot(slot, e.Data);
            dropSlot = -1;
            Invalidate();
            base.OnDragDrop(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up)
            {
                if (e.Control) MoveSelected(-1); else SelectSlot((SelectedSlot + 7) % 8);
                e.Handled = true;
            }
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down)
            {
                if (e.Control) MoveSelected(1); else SelectSlot((SelectedSlot + 1) % 8);
                e.Handled = true;
            }
            if (e.KeyCode == Keys.Enter) { RequestEdit(); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    internal sealed class ActionPalette : ListBox
    {
        private readonly ActionDragSession drag;
        private Point downPoint;
        private bool leftPressed;
        private int hovered = -1;
        internal event Action<RingAction> AssignRequested;

        internal ActionPalette(ActionDragSession drag)
        {
            this.drag = drag;
            Name = "ActionPalette";
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            AccessibleName = "Shortcut palette. Drag an action to the ring or press Enter to assign it to the selected position.";
        }

        internal void ShowCategory(string name)
        {
            Items.Clear();
            foreach (RingAction action in RingSettings.Preset(name)) Items.Add(action);
            if (Items.Count > 0) SelectedIndex = 0;
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ActionAccessibility(this, 8,
                delegate(int index) { return index < Items.Count ? "Assign " + ((RingAction)Items[index]).Label : "Action"; },
                delegate(int index) { return index < Items.Count ? GetItemRectangle(index) : Rectangle.Empty; },
                delegate(int index)
                {
                    if (index >= Items.Count) return;
                    SelectedIndex = index;
                    AssignSelected();
                }, delegate(int index) { return index == SelectedIndex; });
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ItemHeight = Math.Max(54 * DeviceDpi / 96, Font.Height * 2 + 16 * DeviceDpi / 96);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            RingAction action = (RingAction)Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool hot = hovered == e.Index;
            using (SolidBrush background = new SolidBrush(selected ? Theme.Selection : hot ? Theme.Hover : Theme.Surface))
            using (Font icon = new Font("Segoe MDL2 Assets", Font.Size + 5))
            using (Font labelFont = new Font("Segoe UI Semibold", Font.Size))
            using (Font detail = new Font(Font.FontFamily, Math.Max(10, Font.Size - 0.5f)))
            {
                float scale = DeviceDpi / 96f;
                using (SolidBrush canvas = new SolidBrush(BackColor))
                    e.Graphics.FillRectangle(canvas, e.Bounds);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF card = new RectangleF(e.Bounds.X + 2 * scale, e.Bounds.Y + 3 * scale,
                    e.Bounds.Width - 4 * scale - 1, e.Bounds.Height - 6 * scale - 1);
                using (GraphicsPath path = Theme.RoundedRectangle(card, 10 * scale))
                using (Pen border = new Pen(selected ? Theme.Accent : Theme.Border, scale))
                {
                    e.Graphics.FillPath(background, path);
                    e.Graphics.DrawPath(border, path);
                }
                int inset = 10 * DeviceDpi / 96;
                int iconWidth = 37 * DeviceDpi / 96;
                TextRenderer.DrawText(e.Graphics, ActionIcons.Glyph(action.Icon), icon,
                    new Rectangle(e.Bounds.X + inset, e.Bounds.Y, iconWidth, e.Bounds.Height),
                    Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int left = e.Bounds.X + iconWidth + 2 * inset;
                Rectangle label = new Rectangle(left, e.Bounds.Y + inset / 2, e.Bounds.Right - left - inset, e.Bounds.Height / 2);
                TextRenderer.DrawText(e.Graphics, action.Label, labelFont, label, Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Rectangle command = new Rectangle(left, label.Bottom, label.Width, e.Bounds.Height / 2 - inset / 2);
                TextRenderer.DrawText(e.Graphics, action.Command, detail, command, Theme.Muted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            downPoint = e.Location;
            leftPressed = e.Button == MouseButtons.Left && IndexFromPoint(e.Location) >= 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = IndexFromPoint(e.Location);
            if (hovered != next)
            {
                if (hovered >= 0) Invalidate(GetItemRectangle(hovered));
                hovered = next;
                if (hovered >= 0) Invalidate(GetItemRectangle(hovered));
            }
            if (!leftPressed || e.Button != MouseButtons.Left || SelectedItem == null || drag.IsActive) return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - downPoint.X) < threshold.Width / 2 &&
                Math.Abs(e.Y - downPoint.Y) < threshold.Height / 2) return;
            leftPressed = false;
            drag.Drag(this, (RingAction)SelectedItem, -1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hovered >= 0) Invalidate(GetItemRectangle(hovered));
            hovered = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            leftPressed = false;
            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            AssignSelected();
            base.OnDoubleClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { AssignSelected(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void AssignSelected()
        {
            Action<RingAction> handler = AssignRequested;
            if (SelectedItem != null && handler != null) handler(((RingAction)SelectedItem).Copy());
        }
    }
}
