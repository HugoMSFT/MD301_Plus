using System;
using System.Drawing;
using System.Windows.Forms;

namespace MD301Plus
{
    internal class ActionEditorForm : Form
    {
        private readonly TextBox label;
        private readonly ComboBox kind;
        private readonly TextBox command;
        private readonly ComboBox icon;
        private readonly Label error;
        internal RingAction ResultAction { get; private set; }

        internal ActionEditorForm(RingAction action)
        {
            SuspendLayout();
            Text = "Edit ring action";
            Font = new Font("Segoe UI", Theme.BodyFontSize);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 7
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(layout);
            label = Field(action.Label);
            label.MaxLength = 24;
            kind = Choice(false);
            kind.Items.AddRange(new[] { "Keyboard shortcut", "Open app, file or website" });
            kind.SelectedIndex = action.Kind == "launch" ? 1 : 0;
            command = Field(action.Command);
            icon = Choice(true);
            icon.Items.AddRange(ActionIcons.Names);
            icon.SelectedItem = action.Icon;
            layout.Controls.Add(Caption("Name"), 0, 0);
            layout.Controls.Add(FieldSurface(label), 1, 0);
            layout.Controls.Add(Caption("Action"), 0, 1);
            layout.Controls.Add(kind, 1, 1);
            layout.Controls.Add(Caption("Command"), 0, 2);
            layout.Controls.Add(FieldSurface(command), 1, 2);
            layout.Controls.Add(Caption("Icon"), 0, 3);
            layout.Controls.Add(icon, 1, 3);
            Label help = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Theme.Muted,
                Text = "Examples: Ctrl+Shift+S, Win+E, MediaPlayPause, VolumeUp.\r\nFor a launcher, enter an absolute path or an HTTP(S) URL."
            };
            layout.Controls.Add(help, 0, 4);
            layout.SetColumnSpan(help, 2);
            error = new Label
            {
                Name = "ActionValidation", Text = " ",
                Dock = DockStyle.Fill, ForeColor = Theme.Accent, AutoEllipsis = true
            };
            layout.Controls.Add(error, 0, 5);
            layout.SetColumnSpan(error, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button apply = new RoundedButton { Text = "Use action", AutoSize = true, MinimumSize = new Size(120, 40), BackColor = Theme.Accent, ForeColor = Theme.Background };
            apply.Click += delegate { ApplyAction(); };
            Button cancel = new RoundedButton { Text = "Cancel", AutoSize = true, MinimumSize = new Size(100, 40), DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 6);
            layout.SetColumnSpan(buttons, 2);
            AcceptButton = apply;
            CancelButton = cancel;
            ResumeLayout(false);
            PerformLayout();
        }

        private static TextBox Field(string text)
        {
            return new TextBox { Text = text };
        }

        private static RoundedField FieldSurface(TextBox field)
        {
            return new RoundedField(field) { Dock = DockStyle.Fill, Margin = new Padding(3) };
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static ComboBox Choice(bool showIcons)
        {
            ComboBox result = new ComboBox
            {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.Surface, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed, Margin = new Padding(3)
            };
            result.FontChanged += delegate
            {
                float scale = result.DeviceDpi / 96f;
                result.ItemHeight = Math.Max(result.Font.Height + (int)Math.Ceiling(6 * scale), (int)Math.Ceiling(24 * scale));
            };
            result.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                string text = e.Index >= 0 ? Convert.ToString(result.Items[e.Index]) : Convert.ToString(result.SelectedItem);
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (SolidBrush background = new SolidBrush(selected ? Theme.Hover : Theme.Surface))
                    e.Graphics.FillRectangle(background, e.Bounds);
                int inset = 8 * result.DeviceDpi / 96;
                Rectangle bounds = Rectangle.Inflate(e.Bounds, -inset, 0);
                if (showIcons && Array.IndexOf(ActionIcons.Names, text) >= 0)
                {
                    int width = 28 * result.DeviceDpi / 96;
                    using (Font glyph = new Font("Segoe MDL2 Assets", result.Font.Size))
                        TextRenderer.DrawText(e.Graphics, ActionIcons.Glyph(text), glyph,
                            new Rectangle(bounds.Left, bounds.Top, width, bounds.Height), Theme.Accent,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    bounds.X += width;
                    bounds.Width -= width;
                }
                TextRenderer.DrawText(e.Graphics, text, result.Font, bounds, Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            return result;
        }

        private void ApplyAction()
        {
            RingAction result = new RingAction
            {
                Label = label.Text.Trim(), Kind = kind.SelectedIndex == 1 ? "launch" : "shortcut",
                Command = command.Text.Trim(), Icon = Convert.ToString(icon.SelectedItem)
            };
            try
            {
                result.Validate(0);
                ResultAction = result;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (SettingsException ex) { error.Text = ex.Message; }
        }
    }
}
