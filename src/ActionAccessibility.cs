using System;
using System.Drawing;
using System.Windows.Forms;

namespace MD301Plus
{
    internal sealed class ChoiceRadioButton : RadioButton
    {
        internal ChoiceRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e) { Theme.DrawButton(e.Graphics, this, Focused && ShowFocusCues); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ChoiceAccessibility(this);
        }

        private sealed class ChoiceAccessibility : Control.ControlAccessibleObject
        {
            private readonly ChoiceRadioButton owner;
            internal ChoiceAccessibility(ChoiceRadioButton owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override string DefaultAction { get { return "Choose"; } }
            public override string Description { get { return owner.Checked ? "Selected choice" : "Choose this option"; } }
            public override AccessibleStates State
            {
                get { return base.State | (owner.Checked ? AccessibleStates.Pressed : AccessibleStates.None); }
            }
            public override void DoDefaultAction()
            {
                if (!owner.Enabled || owner.IsDisposed) return;
                Action choose = delegate { owner.Checked = true; };
                if (owner.IsHandleCreated) owner.BeginInvoke(choose);
                else choose();
            }
        }
    }

    internal sealed class ActionAccessibility : Control.ControlAccessibleObject
    {
        private readonly AccessibleObject[] children;

        internal ActionAccessibility(Control owner, int count, Func<int, string> name,
            Func<int, Rectangle> bounds, Action<int> invoke, Func<int, bool> selected = null)
            : base(owner)
        {
            children = new AccessibleObject[count];
            for (int i = 0; i < count; i++)
                children[i] = new ActionButton(owner, this, i, name, bounds, invoke, selected);
        }

        public override AccessibleRole Role { get { return AccessibleRole.Grouping; } }
        public override int GetChildCount() { return children.Length; }
        public override AccessibleObject GetChild(int index)
        {
            return index >= 0 && index < children.Length ? children[index] : null;
        }

        private sealed class ActionButton : AccessibleObject
        {
            private readonly Control owner;
            private readonly AccessibleObject parent;
            private readonly int index;
            private readonly Func<int, string> name;
            private readonly Func<int, Rectangle> bounds;
            private readonly Action<int> invoke;
            private readonly Func<int, bool> selected;

            internal ActionButton(Control owner, AccessibleObject parent, int index, Func<int, string> name,
                Func<int, Rectangle> bounds, Action<int> invoke, Func<int, bool> selected)
            {
                this.owner = owner; this.parent = parent; this.index = index; this.name = name;
                this.bounds = bounds; this.invoke = invoke; this.selected = selected;
            }

            public override string Name { get { return name(index); } set { base.Name = value; } }
            public override string DefaultAction { get { return "Choose"; } }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override AccessibleObject Parent { get { return parent; } }
            public override Rectangle Bounds
            {
                get { return owner.IsHandleCreated && owner.Visible ? owner.RectangleToScreen(bounds(index)) : Rectangle.Empty; }
            }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = owner.Enabled ? AccessibleStates.Focusable : AccessibleStates.Unavailable;
                    if (!owner.Visible) state |= AccessibleStates.Invisible;
                    if (selected != null && selected(index)) state |= AccessibleStates.Selected;
                    return state;
                }
            }
            public override void DoDefaultAction()
            {
                if (owner.IsDisposed || !owner.Enabled) return;
                Action action = delegate { invoke(index); };
                if (owner.IsHandleCreated) owner.BeginInvoke(action);
                else action();
            }
        }
    }
}
