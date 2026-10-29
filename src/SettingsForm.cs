using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MD301Plus
{
    internal class SettingsForm : Form
    {
        private readonly Action<RingSettings> save;
        private readonly Action<RingSettings> preview;
        private readonly Action pauseForSetup;
        private readonly Action resumeAfterSetup;
        private readonly ActionDragSession drag = new ActionDragSession();
        private readonly RadioButton[] sourceChoices;
        private readonly TextBox dpiKey;
        private readonly TextBox keyboard;
        private readonly RoundedField dpiField;
        private readonly RoundedField keyboardField;
        private readonly FlowLayoutPanel keyRow;
        private readonly Label instructions;
        private readonly Button setup;
        private readonly Button capture;
        private readonly Label status;
        private readonly Label pausedNotice;
        private readonly Label selection;
        private readonly MouseButtonGuide guide;
        private readonly RingEditorControl ring;
        private readonly ActionPalette palette;
        private readonly RadioButton[] categories;
        private EditorButton selectedButton;
        private string currentCategory = "Productivity";
        private bool dirty;
        private bool initializing = true;
        private string savedTrigger;
        internal bool IsCapturingButton { get; private set; }
        internal bool IsSetupPaused { get; private set; }
        internal bool IsDragging { get { return drag.IsActive; } }
        internal bool HasModalEditor
        {
            get
            {
                foreach (Form owned in OwnedForms) if (owned.Visible && owned.Modal) return true;
                return false;
            }
        }
        internal RingEditorControl RingEditor { get { return ring; } }

        internal SettingsForm(RingSettings settings, Action<RingSettings> save, Action<RingSettings> preview,
            Action pauseForSetup = null, Action resumeAfterSetup = null, bool setupPaused = false)
        {
            settings.Validate();
            this.save = save;
            this.preview = preview;
            this.pauseForSetup = pauseForSetup;
            this.resumeAfterSetup = resumeAfterSetup;
            savedTrigger = settings.Trigger;
            SuspendLayout();
            Text = "MD301 Plus - Your action ring";
            Font = new Font("Segoe UI", Theme.BodyFontSize);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1240, 880);
            MinimumSize = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Name = "EditorLayout", Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(root);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.Controls.Add(new Label
            {
                Text = "MD301 Plus  /  Your action ring", Font = new Font("Segoe UI Semibold", 22),
                ForeColor = Theme.Accent, Dock = DockStyle.Fill
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "Choose your button, then drag actions into place.",
                Dock = DockStyle.Fill, ForeColor = Theme.Muted
            }, 0, 1);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            root.Controls.Add(body, 0, 1);
            FlowLayoutPanel assignment = new FlowLayoutPanel
            {
                Name = "ButtonAssignment", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true
            };
            body.Controls.Add(assignment, 0, 0);
            assignment.Controls.Add(Heading("1  Choose your button", 228));
            guide = new MouseButtonGuide { Size = new Size(228, 200), Margin = new Padding(3, 0, 3, 6) };
            assignment.Controls.Add(guide);
            FlowLayoutPanel buttonPicker = new FlowLayoutPanel { Name = "TriggerSource", Width = 228, Height = 148 };
            sourceChoices = new RadioButton[6];
            string[] buttonLabels =
            {
                "Not assigned",
                "DPI switch (below scroll)",
                "Back",
                "Forward",
                "Wheel click",
                "Keyboard"
            };
            foreach (int index in new[] { 1, 2, 3, 4, 5, 0 })
            {
                EditorButton choice = (EditorButton)index;
                RadioButton button = ChoiceButton("Button" + choice, buttonLabels[index],
                    index == 0 || index == 1 ? 224 : 110, index == 1 ? 38 : 32);
                button.CheckedChanged += delegate
                {
                    if (!initializing && button.Checked)
                    {
                        selectedButton = choice;
                        UpdateAssignment();
                        MarkDirty("Button assignment changed. Save to apply it.");
                    }
                };
                sourceChoices[index] = button;
                buttonPicker.Controls.Add(button);
            }
            assignment.Controls.Add(buttonPicker);
            keyRow = new FlowLayoutPanel { Width = 228, Height = 46, WrapContents = false };
            keyRow.Controls.Add(new Label { Text = "Mapped key", AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
            dpiKey = new TextBox
            {
                Name = "DpiKey", Text = "F8", BackColor = Theme.Surface,
                ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "DPI mapped function key"
            };
            keyboard = new TextBox
            {
                Name = "Trigger", BackColor = Theme.Surface, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, Text = "F8", AccessibleName = "Gear Link keyboard mapping"
            };
            dpiField = new RoundedField(dpiKey) { Width = 88, Height = 40, Margin = new Padding(0, 0, 0, 0) };
            keyboardField = new RoundedField(keyboard) { Width = 132, Height = 40, Margin = new Padding(0, 0, 0, 0) };
            keyRow.Controls.Add(dpiField);
            keyRow.Controls.Add(keyboardField);
            assignment.Controls.Add(keyRow);
            pausedNotice = new Label
            {
                Name = "PausedSetupNotice", AutoSize = true, MaximumSize = new Size(228, 0), ForeColor = Theme.Accent,
                Text = "RING PAUSED FOR SETUP\r\nSave to apply, or close to resume the last saved ring.", Visible = false
            };
            assignment.Controls.Add(pausedNotice);
            instructions = new Label
            {
                Name = "ButtonInstructions", AutoSize = true, MaximumSize = new Size(228, 0),
                ForeColor = Theme.Muted, Margin = new Padding(3, 10, 3, 8)
            };
            assignment.Controls.Add(instructions);
            setup = Button("Open Gear Link...", false);
            setup.Width = 228;
            setup.Click += delegate { OpenGearLink(); };
            assignment.Controls.Add(setup);
            capture = Button("Detect side / wheel button", false);
            capture.Width = 228;
            capture.Click += delegate { CaptureButton(); };
            assignment.Controls.Add(capture);

            TableLayoutPanel center = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 0, 12, 0)
            };
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            center.Controls.Add(Heading("2  Arrange your ring", 300), 0, 0);
            ring = new RingEditorControl(drag, settings.Actions) { Dock = DockStyle.Fill };
            center.Controls.Add(ring, 0, 1);
            TableLayoutPanel selected = new RoundedTableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Surface, Padding = new Padding(14)
            };
            selected.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selected.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            selected.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selection = new Label { Name = "SelectedAction", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            selected.Controls.Add(selection, 0, 0);
            Button edit = Button("Edit action", false);
            edit.Click += delegate { EditSelected(); };
            selected.Controls.Add(edit, 1, 0);
            center.Controls.Add(selected, 0, 2);
            body.Controls.Add(center, 1, 0);

            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(12, 0, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            actions.Controls.Add(Heading("3  Drag a shortcut", 270), 0, 0);
            FlowLayoutPanel categoryPicker = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            categories = new RadioButton[3];
            string[] categoryNames = { "Productivity", "Media", "Windows" };
            string[] categoryLabels = { "Everyday", "Media", "Windows" };
            for (int i = 0; i < categoryNames.Length; i++)
            {
                string name = categoryNames[i];
                RadioButton button = ChoiceButton("Category" + name, categoryLabels[i], 94, 36);
                button.CheckedChanged += delegate
                {
                    if (!initializing && button.Checked)
                    {
                        currentCategory = name;
                        palette.ShowCategory(name);
                    }
                };
                categories[i] = button;
                categoryPicker.Controls.Add(button);
            }
            categories[0].Checked = true;
            actions.Controls.Add(categoryPicker, 0, 1);
            actions.Controls.Add(new Label
            {
                Text = "Drag an action onto a circle.\r\nDrag circles to swap positions.",
                Dock = DockStyle.Fill, ForeColor = Theme.Muted
            }, 0, 2);
            palette = new ActionPalette(drag) { Dock = DockStyle.Fill };
            palette.ShowCategory("Productivity");
            actions.Controls.Add(palette, 0, 3);
            Button custom = Button("+  Custom shortcut or app", false);
            custom.AutoSize = false;
            custom.Dock = DockStyle.Fill;
            custom.Click += delegate { AddCustom(); };
            actions.Controls.Add(custom, 0, 4);
            Button fill = Button("Fill ring with this set", false);
            fill.AutoSize = false;
            fill.Dock = DockStyle.Fill;
            fill.Click += delegate { FillRing(); };
            actions.Controls.Add(fill, 0, 5);
            body.Controls.Add(actions, 2, 0);

            status = new Label { Name = "EditorStatus", Dock = DockStyle.Fill, ForeColor = Theme.Accent, AutoEllipsis = true };
            root.Controls.Add(status, 0, 2);
            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
            };
            Button apply = Button("Save changes", true);
            apply.Click += delegate { Save(); };
            Button close = Button("Close", false);
            close.Click += delegate { Close(); };
            Button test = Button("Preview ring", false);
            test.Click += delegate { Preview(); };
            footer.Controls.Add(apply);
            footer.Controls.Add(close);
            footer.Controls.Add(test);
            root.Controls.Add(footer, 0, 3);
            AcceptButton = apply;

            TriggerBinding binding = TriggerBinding.Parse(settings.Trigger);
            if (binding.Keyboard != null)
            {
                keyboard.Text = binding.Keyboard.Display;
                if (settings.Button == "dpi") dpiKey.Text = binding.Keyboard.Display;
            }
            EditorButton initial = !binding.IsAssigned ? EditorButton.Unassigned : settings.Button == "dpi" ? EditorButton.Dpi :
                binding.Keyboard != null ? EditorButton.Keyboard : binding.MouseButton == MouseButtonTrigger.Back ? EditorButton.Back :
                binding.MouseButton == MouseButtonTrigger.Forward ? EditorButton.Forward : EditorButton.Middle;
            selectedButton = initial;
            sourceChoices[(int)initial].Checked = true;
            dpiKey.TextChanged += delegate { UpdateAssignment(); MarkDirty("Gear Link key changed. Update the mouse mapping, then save."); };
            keyboard.TextChanged += delegate { MarkDirty("Keyboard mapping changed. Save to apply it."); };
            guide.ButtonChosen += ChooseButton;
            ring.Changed += delegate { MarkDirty("Ring updated. Save changes to apply your layout."); };
            ring.SelectionChanged += UpdateSelection;
            ring.EditRequested += EditSelected;
            palette.AssignRequested += delegate(RingAction action) { ring.Assign(ring.SelectedSlot, action); };
            initializing = false;
            UpdateAssignment();
            UpdateSelection();
            status.Text = "Drag an action onto a ring position to get started.";
            SetSetupPaused(setupPaused);
            ResumeLayout(false);
            PerformLayout();
        }

        private static Label Heading(string text, int width)
        {
            return new Label { Text = text, Width = width, Height = 30, Font = new Font("Segoe UI Semibold", 11.5f) };
        }

        private static Button Button(string text, bool primary)
        {
            return new RoundedButton
            {
                Text = text, AutoSize = true, MinimumSize = new Size(112, 40), Height = 40,
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Theme.Accent : Theme.Surface,
                ForeColor = primary ? Theme.Background : Theme.Text, Cursor = Cursors.Hand,
                Margin = new Padding(4), UseVisualStyleBackColor = false
            };
        }

        private static RadioButton ChoiceButton(string name, string text, int width, int height)
        {
            RadioButton result = new ChoiceRadioButton
            {
                Name = name, Text = text, Width = width, Height = height, Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(1),
                BackColor = Theme.Surface, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat
            };
            result.CheckedChanged += delegate
            {
                result.BackColor = result.Checked ? Theme.Accent : Theme.Surface;
                result.ForeColor = result.Checked ? Theme.Background : Theme.Text;
            };
            return result;
        }

        private EditorButton CurrentButton { get { return selectedButton; } }

        internal void ChooseButton(EditorButton button)
        {
            if ((int)button < 0 || (int)button >= sourceChoices.Length) throw new ArgumentOutOfRangeException("button");
            sourceChoices[(int)button].Checked = true;
        }

        internal void SelectCategory(string name)
        {
            string[] names = { "Productivity", "Media", "Windows" };
            int index = Array.IndexOf(names, name);
            if (index < 0) throw new SettingsException("Unknown action category: " + name);
            categories[index].Checked = true;
        }

        private void UpdateAssignment()
        {
            EditorButton button = CurrentButton;
            guide.SelectedButton = button;
            keyRow.Visible = button == EditorButton.Dpi || button == EditorButton.Keyboard;
            dpiKey.Visible = button == EditorButton.Dpi;
            keyboard.Visible = button == EditorButton.Keyboard;
            dpiField.Visible = button == EditorButton.Dpi;
            keyboardField.Visible = button == EditorButton.Keyboard;
            setup.Visible = button == EditorButton.Dpi || button == EditorButton.Keyboard;
            capture.Visible = button != EditorButton.Dpi && button != EditorButton.Keyboard;
            if (button == EditorButton.Dpi)
            {
                instructions.Text = "DPI switch - below the wheel.\r\n\r\nIn Gear Link: DPI > Keyboard > " +
                    dpiKey.Text.Trim() + ".\r\nConnect by USB / receiver.\r\nThen return here and save.";
            }
            else if (button == EditorButton.Keyboard)
            {
                instructions.Text = "Map the desired button to this key in Gear Link, then save here.\r\nA single function key needs no ASUS companion; combinations may need one.";
            }
            else if (button == EditorButton.Unassigned)
            {
                instructions.Text = "Choose a button above, or click it on the mouse guide.\r\nNothing is intercepted until you save an assignment.";
            }
            else
            {
                instructions.Text = "This button opens and closes your ring while MD301 Plus runs.\r\nIts normal function is replaced on all connected mice.";
            }
        }

        private void UpdateSelection()
        {
            RingAction action = ring.ActionAt(ring.SelectedSlot);
            selection.Text = "Position " + (ring.SelectedSlot + 1) + "  -  " + action.Label + "\r\n" + action.Command;
        }

        private void MarkDirty(string message)
        {
            if (initializing) return;
            dirty = true;
            status.Text = message;
        }

        internal void SelectMouseButton(MouseButtonTrigger button)
        {
            switch (button)
            {
                case MouseButtonTrigger.Back: ChooseButton(EditorButton.Back); break;
                case MouseButtonTrigger.Forward: ChooseButton(EditorButton.Forward); break;
                case MouseButtonTrigger.Middle: ChooseButton(EditorButton.Middle); break;
                default: throw new ArgumentOutOfRangeException("button");
            }
        }

        private void CaptureButton()
        {
            IsCapturingButton = true;
            try
            {
                using (ButtonCaptureForm captureForm = new ButtonCaptureForm())
                {
                    captureForm.StartCapture();
                    if (captureForm.ShowDialog(this) == DialogResult.OK) SelectMouseButton(captureForm.SelectedButton);
                }
            }
            catch (Win32Exception ex) { AppLog.ShowError(this, "Cannot detect button", ex); }
            finally { IsCapturingButton = false; }
        }

        private void EditSelected()
        {
            using (ActionEditorForm editor = new ActionEditorForm(ring.ActionAt(ring.SelectedSlot)))
                if (editor.ShowDialog(this) == DialogResult.OK) ring.Assign(ring.SelectedSlot, editor.ResultAction);
        }

        private void AddCustom()
        {
            using (ActionEditorForm editor = new ActionEditorForm(new RingAction
            {
                Label = "My shortcut", Kind = "shortcut", Command = "Ctrl+Shift+S", Icon = "shortcut"
            }))
                if (editor.ShowDialog(this) == DialogResult.OK) ring.Assign(ring.SelectedSlot, editor.ResultAction);
        }

        internal void LoadPreset(string name)
        {
            ring.SetActions(RingSettings.Preset(name));
            UpdateSelection();
            MarkDirty(name + " set loaded. Save changes to apply it.");
        }

        private void FillRing()
        {
            if (MessageBox.Show(this, "Replace all eight positions with the " + currentCategory + " set?",
                "Fill action ring", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                LoadPreset(currentCategory);
        }

        internal RingSettings ReadSettings()
        {
            RingSettings result = new RingSettings { Version = 1, Actions = ring.CopyActions() };
            switch (CurrentButton)
            {
                case EditorButton.Unassigned: result.Trigger = "None"; break;
                case EditorButton.Dpi: result.Trigger = dpiKey.Text.Trim(); result.Button = "dpi"; break;
                case EditorButton.Back: result.Trigger = "Mouse4"; break;
                case EditorButton.Forward: result.Trigger = "Mouse5"; break;
                case EditorButton.Middle: result.Trigger = "MiddleMouse"; break;
                case EditorButton.Keyboard: result.Trigger = keyboard.Text.Trim(); result.Button = "keyboard"; break;
                default: throw new SettingsException("Choose the button that opens your ring.");
            }
            result.Validate();
            result.Trigger = TriggerBinding.Parse(result.Trigger).Display;
            return result;
        }

        private void Save()
        {
            try
            {
                RingSettings next = ReadSettings();
                save(next);
                savedTrigger = next.Trigger;
                SetSetupPaused(false);
                dirty = false;
                status.Text = next.Button == "dpi" ?
                    "Saved. Map DPI to " + next.Trigger + " in Gear Link, then press the button below the scroll wheel." :
                    "Saved. Press your assigned mouse button to open the ring.";
            }
            catch (SettingsException ex) { status.Text = ex.Message + (IsSetupPaused ? " Ring remains paused; fix and save, or close to resume." : ""); }
            catch (Win32Exception ex) { SaveFailed(ex); }
            catch (IOException ex) { SaveFailed(ex); }
            catch (UnauthorizedAccessException ex) { SaveFailed(ex); }
        }

        private void SaveFailed(Exception error)
        {
            if (IsSetupPaused) status.Text = "Still paused for setup. Fix the problem and save, or close to resume the saved ring.";
            AppLog.ShowError(this, "Cannot save changes", error);
        }

        internal void SetSetupPaused(bool paused)
        {
            IsSetupPaused = paused;
            pausedNotice.Visible = paused;
            if (paused) status.Text = "Ring paused for Gear Link setup. Save changes to apply, or close to resume the saved ring.";
        }

        internal void NotifyTriggerReceived()
        {
            status.Text = "Saved trigger received (" + savedTrigger + "). Showing a safe preview of your ring.";
        }

        private void Preview()
        {
            try { preview(ReadSettings()); }
            catch (SettingsException ex) { status.Text = ex.Message; }
            catch (ExternalException ex) { AppLog.ShowError(this, "Cannot preview ring", ex); }
        }

        private void OpenGearLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://gearlink.asus.com/en") { UseShellExecute = true });
                if (pauseForSetup != null)
                {
                    pauseForSetup();
                    SetSetupPaused(true);
                }
                else status.Text = "Map the DPI button in Gear Link, return here, and save changes.";
            }
            catch (Win32Exception ex) { AppLog.ShowError(this, "Cannot open Gear Link", ex); }
            catch (InvalidOperationException ex) { AppLog.ShowError(this, "Cannot open Gear Link", ex); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && MessageBox.Show(this, "Discard your unsaved ring and button changes?", "Unsaved changes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                e.Cancel = true;
            if (!e.Cancel && IsSetupPaused && resumeAfterSetup != null)
            {
                try
                {
                    resumeAfterSetup();
                    SetSetupPaused(false);
                }
                catch (Win32Exception error)
                {
                    e.Cancel = true;
                    AppLog.ShowError(this, "Cannot resume the saved ring", error);
                }
            }
            base.OnFormClosing(e);
        }
    }
}
