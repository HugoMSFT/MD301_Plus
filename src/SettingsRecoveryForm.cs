using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MD301Plus
{
    internal class SettingsRecoveryForm : Form
    {
        private readonly SettingsStore store;
        private readonly RingSettings backup;
        internal string PreservedFile { get; private set; }

        internal SettingsRecoveryForm(SettingsStore store, SettingsException failure)
        {
            this.store = store;
            Text = "MD301 Plus - Recover settings";
            Font = new Font("Segoe UI", Theme.BodyFontSize);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 360);
            MinimumSize = new Size(600, 320);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            AppLog.Error("Settings need recovery", failure);
            string backupProblem = null;
            try { backup = store.LoadBackup(); }
            catch (SettingsException error) { backupProblem = error.Message; }
            catch (IOException error) { backupProblem = error.Message; }
            catch (UnauthorizedAccessException error) { backupProblem = error.Message; }

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(layout);
            layout.Controls.Add(new Label
            {
                Text = "Recover your action ring", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 18), ForeColor = Theme.Accent
            }, 0, 0);
            layout.Controls.Add(new TextBox
            {
                Name = "RecoveryDetails", Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surface, ForeColor = Theme.Text,
                Text = failure.Message + Environment.NewLine + Environment.NewLine + store.FilePath +
                    Environment.NewLine + Environment.NewLine +
                    (backup == null ? "The previous-settings backup is unavailable: " + backupProblem :
                        "A valid previous-settings backup is available. You can restore its eight actions.")
            }, 0, 1);
            layout.Controls.Add(new Label
            {
                Text = "Recovery keeps a separate copy of the original file and leaves your trigger unassigned. " +
                    "Cancel exits without changing your settings.",
                Dock = DockStyle.Fill, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 0, 0)
            }, 0, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Button restore = new RoundedButton
            {
                Name = "RestoreSettingsBackup", Text = "Restore previous actions", AutoSize = true,
                MinimumSize = new Size(200, 40), Enabled = backup != null
            };
            restore.Click += delegate { Recover(backup); };
            Button defaults = new RoundedButton
            {
                Name = "RecoverDefaultSettings", Text = "Use default actions", AutoSize = true, MinimumSize = new Size(180, 40)
            };
            defaults.Click += delegate { Recover(RingSettings.CreateDefault()); };
            Button cancel = new RoundedButton
            {
                Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(100, 40)
            };
            buttons.Controls.Add(restore);
            buttons.Controls.Add(defaults);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 3);
            AcceptButton = cancel;
            CancelButton = cancel;
        }

        private void Recover(RingSettings settings)
        {
            try
            {
                PreservedFile = store.Recover(settings);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (SettingsException error) { AppLog.ShowError(this, "Cannot recover settings", error); }
            catch (IOException error) { AppLog.ShowError(this, "Cannot recover settings", error); }
            catch (UnauthorizedAccessException error) { AppLog.ShowError(this, "Cannot recover settings", error); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Theme.FitToWorkingArea(this);
        }
    }
}
