using System;
using System.Drawing;
using System.Windows.Forms;

namespace MD301Plus
{
    internal sealed class ButtonCaptureForm : Form
    {
        private MouseButtonListener listener;
        internal MouseButtonTrigger SelectedButton { get; private set; }

        internal ButtonCaptureForm()
        {
            SuspendLayout();
            Text = "MD301 Plus - Capture mouse button";
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", Theme.BodyFontSize);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Press Back (button 4), Forward (button 5), or middle-click anywhere.\r\n\r\n" +
                    "Left and right click are kept available. Press Escape to cancel.\r\n" +
                    "ASUS-only buttons such as DPI need a Gear Link keyboard mapping."
            }, 0, 0);
            Button cancel = new RoundedButton
            {
                Text = "Cancel", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 120,
                BackColor = Theme.Surface, ForeColor = Theme.Text
            };
            layout.Controls.Add(cancel, 0, 1);
            Controls.Add(layout);
            CancelButton = cancel;
            ResumeLayout(false);
            PerformLayout();
        }

        internal void StartCapture()
        {
            if (listener != null) throw new InvalidOperationException("Button capture is already running.");
            listener = new MouseButtonListener(MouseButtonTrigger.None, delegate(MouseButtonTrigger button)
            {
                SelectedButton = button;
                DialogResult = DialogResult.OK;
                Close();
            });
            listener.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && listener != null)
            {
                listener.Dispose();
                listener = null;
            }
            base.Dispose(disposing);
        }
    }
}
