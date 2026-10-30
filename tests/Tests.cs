using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MD301Plus.Tests
{
    internal sealed class TestFailure : Exception
    {
        internal TestFailure(string message) : base(message) { }
    }

    internal sealed class BackgroundSettingsPreview : SettingsForm
    {
        internal BackgroundSettingsPreview(RingSettings settings, Action resume = null, bool paused = false)
            : base(settings, delegate { }, delegate { }, null, resume, paused) { }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080;
                return parameters;
            }
        }
    }

    internal sealed class BackgroundActionPreview : ActionEditorForm
    {
        internal BackgroundActionPreview(RingAction action) : base(action) { }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080;
                return parameters;
            }
        }
    }

    internal static class TestRunner
    {
        private static int assertions;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--media-probe")
            {
                using (MediaProbeForm probe = new MediaProbeForm()) Application.Run(probe);
                return 0;
            }
            if (args.Length != 0)
            {
                Console.Error.WriteLine("Unknown test-runner arguments.");
                return 2;
            }
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                TestShortcuts();
                TestApplicationIcon();
                TestSettings(directory);
                TestMouseBindings();
                TestMediaKeys();
                TestGeometry();
                TestInputLayout();
                TestVisualDesign();
                TestNativeMediaDelivery();
                TestRingDragDrop();
                TestEditorAndRendering();
                TestActionDialogRendering();
                TestNativeWindows();
                TestShellLauncher(directory);
                TestApplicationSettings(directory);
                Console.WriteLine("PASS: " + assertions + " assertions, including native Windows hotkey and overlay smoke tests.");
                return 0;
            }
            catch (TestFailure ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("FAIL: unexpected I/O error: " + ex);
                return 1;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new TestFailure(message);
        }

        private static void TestApplicationIcon()
        {
            using (Icon icon = RingApplication.CreateIcon())
            using (Bitmap bitmap = icon.ToBitmap())
            {
                Check(icon.Size == SystemInformation.SmallIconSize, "Tray icon uses the system small-icon size.");
                Check(bitmap.GetPixel(0, 0).A == 0, "Mouse icon has a transparent background.");
                Check(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A > 0, "Mouse icon contains visible artwork.");
            }
            using (Icon executable = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            using (Bitmap bitmap = executable.ToBitmap())
                Check(bitmap.GetPixel(0, 0).A == 0, "Executable embeds the transparent mouse icon.");
        }

        private static void Throws<T>(Action action, string message) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T) { return; }
            throw new TestFailure(message);
        }

        private static void TestShortcuts()
        {
            KeyboardShortcut chord = KeyboardShortcut.Parse(" alt + Control + f8 ", true);
            Check(chord.Display == "Ctrl+Alt+F8", "Shortcut normalization");
            Check(chord.Key == 0x77 && chord.Modifiers == 3, "Native F8 and modifier values");
            Check(chord.SameAs(KeyboardShortcut.Parse("F8+Ctrl+Alt", true)), "Equivalent chord comparison");
            Check(KeyboardShortcut.Parse("F8", true).Modifiers == 0, "Gear Link trigger needs only one device-side key");
            Check(KeyboardShortcut.Parse("F24", true).Key == 0x87, "F24 trigger support");
            Check(KeyboardShortcut.Parse("Ctrl+Plus", false).Key == 0xBB, "Named Plus support");
            Check(KeyboardShortcut.Parse("Escape", false).Key == 0x1B, "Named key support");
            Check(KeyboardShortcut.Parse("9", false).Key == 0x39, "Digit support");
            foreach (string invalid in new[] { "", "Ctrl", "Ctrl+Ctrl+C", "Ctrl++", "Ctrl+C+V", "F25", "Ctrl+Unknown", "Ctrl+Alt+Delete" })
            {
                string value = invalid;
                Throws<SettingsException>(delegate { KeyboardShortcut.Parse(value, false); }, "Reject invalid chord: " + value);
            }
            Throws<SettingsException>(delegate { KeyboardShortcut.Parse("A", true); }, "Do not globally steal a plain letter");
            Throws<SettingsException>(delegate { KeyboardShortcut.Parse("Escape", true); }, "Do not globally steal Escape");
            Throws<SettingsException>(delegate { KeyboardShortcut.Parse(null, false); }, "Reject null shortcut");

            List<KeyStroke> strokes = KeyboardShortcut.Parse("Ctrl+Alt+Shift+Win+F24", false).GetKeyStrokes();
            ushort[] order = { 0x11, 0x12, 0x10, 0x5B, 0x87, 0x87, 0x5B, 0x10, 0x12, 0x11 };
            Check(strokes.Count == order.Length, "Every shortcut has balanced down/up events");
            for (int i = 0; i < order.Length; i++)
                Check(strokes[i].Key == order[i] && strokes[i].IsUp == (i >= 5), "Modifier ordering at event " + i);
            Check(strokes[3].IsExtended && strokes[6].IsExtended, "Windows key is extended in both directions");
            Check(KeyboardShortcut.Parse("Ctrl+Left", false).GetKeyStrokes()[1].IsExtended, "Arrow key is extended");
            KeyboardShortcut numericTrigger = KeyboardShortcut.Parse("Ctrl+1", true);
            Check(MenuInputCapture.IsTriggerEvent(numericTrigger, 0x31, KeyboardShortcut.Control),
                "A number-key trigger must toggle the menu rather than selecting its slot");
            Check(!MenuInputCapture.IsTriggerEvent(numericTrigger, 0x31, 0),
                "The unmodified number remains available for selecting a slot");
            Check(!MenuInputCapture.IsTriggerEvent(chord, chord.Key, 0),
                "A bare trigger key is an unrelated key when its modifiers are absent");
            Check(!MenuInputCapture.IsTriggerEvent(null, 0x31, 0), "Direct mouse mode does not hide number-key selections");
            Console.WriteLine("PASS: shortcut parser and balanced key sequences");
        }

        private static void TestSettings(string directory)
        {
            RingSettings defaults = RingSettings.CreateDefault();
            defaults.Validate();
            Check(defaults.Actions.Count == 8, "Exactly eight default actions");
            Check(defaults.Trigger == "None", "Do not take over a mouse button before explicit assignment");
            RingSettings copy = defaults.Copy();
            copy.Actions[0].Label = "Changed";
            Check(defaults.Actions[0].Label == "Copy", "Deep settings copy");

            foreach (int count in new[] { 0, 7, 9 })
            {
                RingSettings invalid = defaults.Copy();
                invalid.Actions.Clear();
                for (int i = 0; i < count; i++) invalid.Actions.Add(defaults.Actions[0].Copy());
                Throws<SettingsException>(invalid.Validate, "Reject " + count + " actions");
            }
            RingSettings bad = defaults.Copy();
            bad.Actions[0] = null;
            Throws<SettingsException>(bad.Validate, "Reject missing action");
            bad = defaults.Copy();
            bad.Trigger = "F8";
            bad.Actions[0].Command = "f8";
            Throws<SettingsException>(bad.Validate, "Prevent self-trigger loops");
            bad = defaults.Copy();
            bad.Actions[0].Kind = "shell";
            Throws<SettingsException>(bad.Validate, "Reject unknown action kinds");
            bad = defaults.Copy();
            bad.Actions[0].Icon = "unknown";
            Throws<SettingsException>(bad.Validate, "Reject unknown icons");
            bad = defaults.Copy();
            bad.Actions[0].Label = new String('x', 25);
            Throws<SettingsException>(bad.Validate, "Reject overflowing labels");
            bad = defaults.Copy();
            bad.Version = 2;
            Throws<SettingsException>(bad.Validate, "Reject unsupported settings versions");

            foreach (string valid in new[] { @"C:\Windows\notepad.exe", @"C:\Program Files\Creative App\app.exe",
                @"\\server\share\file.txt", "https://example.com/page?q=hello", "http://localhost:8000/" })
                RingAction.ValidateLaunchTarget(valid);
            foreach (string invalid in new[] { "", "notepad.exe", @"C:relative.exe", @"\relative.exe",
                "cmd.exe /c echo test", "javascript:alert(1)", "file:///C:/Windows/notepad.exe" })
            {
                string value = invalid;
                Throws<SettingsException>(delegate { RingAction.ValidateLaunchTarget(value); }, "Reject unsafe/relative launch target: " + value);
            }

            string path = Path.Combine(directory, "settings.json");
            SettingsStore store = new SettingsStore(path);
            Check(store.Load().Actions.Count == 8 && !File.Exists(path), "Missing settings return defaults without writing");
            store.Save(defaults);
            RingSettings loaded = store.Load();
            Check(loaded.Actions.Count == 8 && loaded.Actions[7].Command == "Win+Shift+S", "JSON round trip");
            loaded.Trigger = "Ctrl+Shift+F9";
            loaded.Actions[1] = new RingAction { Label = "Website", Kind = "launch", Command = "https://example.com", Icon = "launch" };
            store.Save(loaded);
            Check(store.Load().Actions[1].Kind == "launch", "Atomic replacement stores launcher");
            RingSettings dpi = defaults.Copy();
            dpi.Trigger = "F8";
            dpi.Button = "dpi";
            dpi.Validate();
            Check(dpi.Copy().Button == "dpi", "Copy retains the physical DPI-button choice");
            store.Save(dpi);
            Check(store.Load().Button == "dpi" && store.Load().Trigger == "F8", "Persist explicit DPI assignment metadata");
            RingSettings invalidDpi = dpi.Copy();
            invalidDpi.Trigger = "Ctrl+Alt+F8";
            Throws<SettingsException>(invalidDpi.Validate, "DPI setup uses one firmware-side function key");
            invalidDpi.Trigger = "Mouse4";
            Throws<SettingsException>(invalidDpi.Validate, "DPI metadata cannot pretend to be a native side button");
            invalidDpi.Trigger = "F12";
            Throws<SettingsException>(invalidDpi.Validate, "Do not suggest the debugger-reserved F12 for DPI setup");
            string before = File.ReadAllText(path);
            loaded.Actions.RemoveAt(0);
            Throws<SettingsException>(delegate { store.Save(loaded); }, "Validate before saving");
            Check(File.ReadAllText(path) == before, "Failed validation preserves the original settings");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "No temporary save files remain");
            File.WriteAllText(path, "{broken");
            Throws<SettingsException>(delegate { store.Load(); }, "Malformed JSON is not silently reset");
            Check(File.ReadAllText(path) == "{broken", "Malformed JSON is preserved");
            File.WriteAllText(path, "null");
            Throws<SettingsException>(delegate { store.Load(); }, "Reject null settings object");
            File.WriteAllText(path, new String(' ', 131073));
            Throws<SettingsException>(delegate { store.Load(); }, "Reject oversized settings");
            Console.WriteLine("PASS: eight-slot validation, launch validation and persistent settings");
        }

        private static void TestMouseBindings()
        {
            Check(!TriggerBinding.Parse("None").IsAssigned, "Explicit disabled state owns no input");
            Check(TriggerBinding.Parse(" mouse4 ").MouseButton == MouseButtonTrigger.Back, "Parse Back binding");
            Check(TriggerBinding.Parse("Mouse5").MouseButton == MouseButtonTrigger.Forward, "Parse Forward binding");
            Check(TriggerBinding.Parse("MiddleMouse").MouseButton == MouseButtonTrigger.Middle, "Parse middle binding");
            Check(TriggerBinding.Parse("alt+ctrl+f8").Display == "Ctrl+Alt+F8", "Existing keyboard configurations remain compatible");
            Check(TriggerBinding.Parse("Mouse4").SameAs(TriggerBinding.Mouse(MouseButtonTrigger.Back)), "Canonical mouse binding comparison");
            Check(!TriggerBinding.Parse("Mouse4").SameAs(TriggerBinding.Parse("Mouse5")), "Distinct mouse bindings");
            Throws<SettingsException>(delegate { TriggerBinding.Parse("Mouse1"); }, "Do not replace primary click");
            Throws<SettingsException>(delegate { TriggerBinding.Parse("Mouse2"); }, "Do not replace secondary click");
            Throws<ArgumentOutOfRangeException>(delegate { TriggerBinding.Mouse(MouseButtonTrigger.None); }, "Reject an invalid mouse assignment");
            Check(Native.TriggerButton(0x020B, 1u << 16) == MouseButtonTrigger.Back, "Decode Windows XBUTTON1");
            Check(Native.TriggerButton(0x020C, 2u << 16) == MouseButtonTrigger.Forward, "Decode Windows XBUTTON2");
            Check(Native.TriggerButton(0x0207, 0) == MouseButtonTrigger.Middle, "Decode middle click");
            Check(Native.TriggerButton(0x0201, 0) == MouseButtonTrigger.None, "Primary click is not assignable");

            foreach (MouseButtonTrigger button in new[] { MouseButtonTrigger.Back, MouseButtonTrigger.Forward, MouseButtonTrigger.Middle })
            {
                int count = 0;
                using (MouseButtonListener listener = new MouseButtonListener(button, delegate { count++; }))
                {
                    listener.Start();
                    int down = button == MouseButtonTrigger.Middle ? 0x0207 : 0x020B;
                    int up = button == MouseButtonTrigger.Middle ? 0x0208 : 0x020C;
                    uint data = button == MouseButtonTrigger.Middle ? 0 : (button == MouseButtonTrigger.Back ? 1u : 2u) << 16;
                    Check(!listener.ProcessMouseEvent(0x0201, 0) && !listener.ProcessMouseEvent(0x0204, 0),
                        "Direct assignment leaves primary and secondary clicks untouched");
                    uint other = button == MouseButtonTrigger.Back ? 2u << 16 : 1u << 16;
                    Check(!listener.ProcessMouseEvent(0x020B, other), "Direct assignment passes unrelated mouse buttons through");
                    listener.ProcessMouseEvent(up, data);
                    Application.DoEvents();
                    Check(count == 0, "An orphan button release never opens the ring");
                    Check(listener.ProcessMouseEvent(down, data), "Consume assigned button down");
                    Check(count == 0, "Open on a completed click, not button down");
                    Check(listener.ProcessMouseEvent(up, data), "Consume assigned button up");
                    PumpUntil(delegate { return count == 1; }, "Mouse click dispatches once");
                    listener.ProcessMouseEvent(up, data);
                    Application.DoEvents();
                    Check(count == 1, "Duplicate button up does not toggle the ring");
                    listener.ProcessMouseEvent(down, data);
                    listener.ProcessMouseEvent(up, data);
                    listener.Dispose();
                    Application.DoEvents();
                    Check(count == 1, "Disposing a binding drops queued trigger messages");
                }
            }
            List<MouseButtonTrigger> captured = new List<MouseButtonTrigger>();
            using (MouseButtonListener capture = new MouseButtonListener(MouseButtonTrigger.None, delegate(MouseButtonTrigger button) { captured.Add(button); }))
            {
                capture.Start();
                capture.ProcessMouseEvent(0x020B, 1u << 16);
                capture.ProcessMouseEvent(0x020C, 1u << 16);
                capture.ProcessMouseEvent(0x020B, 2u << 16);
                capture.ProcessMouseEvent(0x020C, 2u << 16);
                capture.ProcessMouseEvent(0x0207, 0);
                capture.ProcessMouseEvent(0x0208, 0);
                PumpUntil(delegate { return captured.Count == 3; }, "Capture accepts all three assignable mouse buttons");
                Check(captured[0] == MouseButtonTrigger.Back && captured[1] == MouseButtonTrigger.Forward &&
                    captured[2] == MouseButtonTrigger.Middle, "Capture reports the actual logical button");
                Check(!capture.ProcessMouseEvent(0x0201, 0), "Capture never consumes primary click");
            }
            Console.WriteLine("PASS: direct Back/Forward/middle assignment, capture, consumption, pass-through and cleanup");
        }

        private static void TestMediaKeys()
        {
            Dictionary<string, ushort> keys = new Dictionary<string, ushort>
            {
                { "VolumeMute", 0xAD }, { "VolumeDown", 0xAE }, { "VolumeUp", 0xAF },
                { "MediaNextTrack", 0xB0 }, { "MediaPreviousTrack", 0xB1 },
                { "MediaStop", 0xB2 }, { "MediaPlayPause", 0xB3 }, { "MediaSelect", 0xB5 },
                { "BrowserBack", 0xA6 }, { "BrowserForward", 0xA7 }, { "BrowserRefresh", 0xA8 },
                { "LaunchMail", 0xB4 }
            };
            foreach (KeyValuePair<string, ushort> key in keys)
            {
                KeyboardShortcut shortcut = KeyboardShortcut.Parse(key.Key, false);
                Check(shortcut.Key == key.Value && shortcut.Modifiers == 0, "Native Windows VK code: " + key.Key);
                Native.Input[] inputs = ShortcutSender.BuildInputs(shortcut);
                Check(inputs.Length == 2 && inputs[0].Data.Keyboard.VirtualKey == key.Value &&
                    inputs[1].Data.Keyboard.VirtualKey == key.Value, "One native key press/release: " + key.Key);
                Check(inputs[0].Data.Keyboard.Flags == Native.ExtendedKey &&
                    inputs[1].Data.Keyboard.Flags == (Native.ExtendedKey | Native.KeyUp), "Extended media/browser key flags: " + key.Key);
            }
            Check(KeyboardShortcut.Parse("PlayPause", false).Display == "MediaPlayPause", "Play/pause alias");
            Check(KeyboardShortcut.Parse("Mute", false).Key == 0xAD, "Mute alias");
            Check(KeyboardShortcut.Parse("NextTrack", false).Key == 0xB0, "Next-track alias");
            Check(KeyboardShortcut.Parse("PreviousTrack", false).Key == 0xB1, "Previous-track alias");
            Check(KeyboardShortcut.Parse("PrintScreen", false).Key == 0x2C, "Native Print Screen key");
            Check(ActionIcons.Glyph("volume-up") != ActionIcons.Glyph("volume-down"), "Volume directions have distinct icons");
            foreach (string name in new[] { "Productivity", "Media", "Windows" })
            {
                RingSettings settings = RingSettings.CreateDefault();
                settings.Actions = RingSettings.Preset(name);
                settings.Validate();
                Check(settings.Actions.Count == 8, "Exactly eight actions in " + name + " preset");
            }
            Throws<SettingsException>(delegate { RingSettings.Preset("unknown"); }, "Unknown presets are rejected");
            Console.WriteLine("PASS: native Windows/media VK codes, aliases, balanced events and three eight-slot presets");
        }

        private static void TestGeometry()
        {
            Rectangle[] areas =
            {
                new Rectangle(0, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1080),
                new Rectangle(0, -1440, 2560, 1440), new Rectangle(10, 20, 180, 120)
            };
            foreach (Rectangle area in areas)
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f, 3f })
            foreach (Point pointer in new[] { area.Location, new Point(area.Right - 1, area.Bottom - 1),
                new Point(area.Left + area.Width / 2, area.Top + area.Height / 2) })
            {
                RingGeometry geometry = new RingGeometry(pointer, area, scale);
                Check(area.Contains(geometry.Bounds), "Ring remains inside the monitor work area");
                Check(geometry.HitTest(Point.Round(new PointF(
                    geometry.Bounds.Left + geometry.Center.X, geometry.Bounds.Top + geometry.Center.Y))) == -1, "Center cancels, never activates");
                for (int i = 0; i < 8; i++)
                {
                    RectangleF bubble = geometry.Bubble(i);
                    Point center = Point.Round(new PointF(geometry.Bounds.Left + bubble.X + bubble.Width / 2,
                        geometry.Bounds.Top + bubble.Y + bubble.Height / 2));
                    Check(geometry.HitTest(center) == i, "Correct clockwise action at DPI " + scale + ", slot " + i);
                    Check(bubble.Left >= 0 && bubble.Top >= 0 &&
                        bubble.Right <= geometry.Bounds.Width && bubble.Bottom <= geometry.Bounds.Height, "Bubble stays visible");
                    RectangleF next = geometry.Bubble((i + 1) % 8);
                    double dx = bubble.X - next.X, dy = bubble.Y - next.Y;
                    Check(Math.Sqrt(dx * dx + dy * dy) > bubble.Width, "Adjacent bubbles do not overlap");
                }
                Check(geometry.HitTest(new Point(area.Right + 100, area.Bottom + 100)) == -1, "Outside click is not an action");
            }
            Throws<ArgumentOutOfRangeException>(delegate { new RingGeometry(Point.Empty, Rectangle.Empty, 1); }, "Reject empty monitor");
            Throws<ArgumentOutOfRangeException>(delegate { new RingGeometry(Point.Empty, areas[0], Single.NaN); }, "Reject invalid DPI");
            RingGeometry sample = new RingGeometry(new Point(960, 540), areas[0], 1);
            Throws<ArgumentOutOfRangeException>(delegate { sample.Bubble(8); }, "Reject ninth action");
            Check(sample.Bubble(0).Y < sample.Center.Y && sample.Bubble(2).X > sample.Center.X, "Start at top and advance clockwise");
            Console.WriteLine("PASS: eight hit targets, screen edges, negative monitor origins and 100-300% DPI");
        }

        private static void TestInputLayout()
        {
            Check(Marshal.SizeOf(typeof(Native.Input)) == (IntPtr.Size == 8 ? 40 : 28), "INPUT matches the Windows native ABI");
            Check(Marshal.OffsetOf(typeof(Native.Input), "Data").ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "INPUT union alignment");
            Native.Input[] inputs = ShortcutSender.BuildInputs(KeyboardShortcut.Parse("Win+Shift+S", false));
            Check(inputs.Length == 6, "Screenshot sends exactly six key events");
            Check(inputs[0].Data.Keyboard.VirtualKey == 0x10 && inputs[1].Data.Keyboard.VirtualKey == 0x5B,
                "Stable modifier press ordering");
            Check(inputs[2].Data.Keyboard.VirtualKey == 'S' && inputs[2].Data.Keyboard.Flags == 0, "Main key down");
            Check(inputs[3].Data.Keyboard.Flags == Native.KeyUp, "Main key up");
            Check(inputs[1].Data.Keyboard.Flags == Native.ExtendedKey &&
                inputs[4].Data.Keyboard.Flags == (Native.ExtendedKey | Native.KeyUp), "Extended Windows key flags");
            foreach (Native.Input input in inputs)
                Check(input.Type == 1 && input.Data.Keyboard.ExtraInfo == UIntPtr.Zero, "Native keyboard input fields");
            Console.WriteLine("PASS: native input layout and SendInput event construction");
        }

        private static void TestEditorAndRendering()
        {
            RingSettings settings = RingSettings.CreateDefault();
            using (BackgroundSettingsPreview form = new BackgroundSettingsPreview(settings))
            {
                IntPtr foreground = Native.GetForegroundWindow();
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                Check(Native.GetForegroundWindow() == foreground, "Editor rendering is non-activating");
                Check(form.Font.SizeInPoints >= 11, "The editor uses an easier-to-read base text size");
                TableLayoutPanel layout = (TableLayoutPanel)form.Controls[0];
                TableLayoutPanel header = (TableLayoutPanel)layout.GetControlFromPosition(0, 0);
                Label heading = (Label)header.GetControlFromPosition(0, 0);
                Check(heading.Height >= heading.Font.Height, "The settings heading fits at the current display DPI");
                foreach (Control control in layout.Controls)
                {
                    Label text = control as Label;
                    if (text != null && text.Text.Length > 0)
                    {
                        int preferredHeight = text.GetPreferredSize(new Size(text.Width, Int32.MaxValue)).Height;
                        Check(text.Height >= preferredHeight, "Settings text fits: " + text.Text.Split('\r')[0] +
                            " (height " + text.Height + ", preferred " + preferredHeight + ")");
                    }
                }
                RingEditorControl ring = form.RingEditor;
                Check(ring.CopyActions().Count == 8 && ring.AllowDrop, "The ring is an eight-position drop target");
                Check(ring.Visible && ring.Width > 300 && ring.Height > 300, "The ring is the primary visible editing surface");
                Check(!ContainsGrid(form), "The confusing settings table has been removed");
                Check(form.ReadSettings().Trigger == "None", "Editor starts without taking over a button");
                form.SelectMouseButton(MouseButtonTrigger.Back);
                Check(form.ReadSettings().Trigger == "Mouse4", "Captured Back button persists as a direct binding");
                form.SelectMouseButton(MouseButtonTrigger.Forward);
                Check(form.ReadSettings().Trigger == "Mouse5", "Captured Forward button persists as a direct binding");
                form.SelectMouseButton(MouseButtonTrigger.Middle);
                Check(form.ReadSettings().Trigger == "MiddleMouse", "Captured middle button persists as a direct binding");
                form.ChooseButton(EditorButton.Unassigned);
                Check(form.ReadSettings().Trigger == "None", "The assignment can be disabled without losing the actions");
                RingAction undo = ring.ActionAt(3);
                undo.Label = "Custom undo";
                ring.Assign(3, undo);
                form.ChooseButton(EditorButton.Keyboard);
                ((TextBox)form.Controls.Find("Trigger", true)[0]).Text = "alt+ctrl+f9";
                RingSettings edited = form.ReadSettings();
                Check(edited.Trigger == "Ctrl+Alt+F9" && edited.Actions[3].Label == "Custom undo", "Real editor controls produce the edited settings");
                Check(settings.Actions[3].Label == "Undo", "Unsaved editor changes do not mutate live settings");
                form.LoadPreset("Media");
                Check(form.ReadSettings().Actions[0].Command == "MediaPlayPause", "Editor loads Media preset");
                Check(settings.Actions[0].Command == "Ctrl+C", "Preset changes are not saved implicitly");
                form.LoadPreset("Windows");
                Check(form.ReadSettings().Actions[2].Command == "Win+E", "Editor loads Windows preset");
                form.LoadPreset("Productivity");
                form.ChooseButton(EditorButton.Dpi);
                ((TextBox)form.Controls.Find("DpiKey", true)[0]).Text = "F9";
                Check(form.ReadSettings().Button == "dpi" && form.ReadSettings().Trigger == "F9",
                    "The below-scroll DPI button is an explicit, persistent assignment");
                Label instructions = (Label)form.Controls.Find("ButtonInstructions", true)[0];
                Check(instructions.Text.Contains("below") && instructions.Text.Contains("Gear Link") && instructions.Text.Contains("F9"),
                    "DPI setup explains its location and the exact device-side mapping");
                ((TextBox)form.Controls.Find("DpiKey", true)[0]).Text = "F8";
                form.LoadPreset("Media");
                form.SelectCategory("Productivity");
                form.Controls.Find("CategoryMedia", true)[0].AccessibilityObject.DoDefaultAction();
                Application.DoEvents();
                ActionPalette palette = (ActionPalette)form.Controls.Find("ActionPalette", true)[0];
                Check(palette.Items[0].ToString() == "Play / pause", "Palette items expose human-readable accessible names");
                form.Controls.Find("ButtonBack", true)[0].AccessibilityObject.DoDefaultAction();
                Application.DoEvents();
                Check(form.ReadSettings().Trigger == "Mouse4", "Accessible button choices update the actual saved assignment");
                form.Controls.Find("ButtonDpi", true)[0].AccessibilityObject.DoDefaultAction();
                Application.DoEvents();
                Check(form.ReadSettings().Button == "dpi", "Accessible DPI choice updates its managed workflow");
                Check(palette.Width <= palette.Parent.ClientSize.Width, "Palette stays within its column");
                Label selected = (Label)form.Controls.Find("SelectedAction", true)[0];
                Check(selected.Height <= selected.Parent.ClientSize.Height, "Selected-action summary stays within its row");
                using (Bitmap image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.Controls[0].DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-preview.png"), ImageFormat.Png);
                }
                float displayScale = form.DeviceDpi / 96f;
                form.Size = new Size((int)(1040 * displayScale), (int)(720 * displayScale));
                Application.DoEvents();
                Rectangle editorBounds = ring.RectangleToScreen(ring.ClientRectangle);
                for (int i = 0; i < 9; i++)
                    Check(editorBounds.Contains(ring.AccessibilityObject.GetChild(i).Bounds),
                        "A compact window keeps ring target " + (i + 1) + " visible");
                foreach (string name in new[] { "CategoryProductivity", "CategoryMedia", "CategoryWindows", "ButtonDpi", "ButtonBack", "ButtonForward" })
                {
                    Control choice = form.Controls.Find(name, true)[0];
                    Check(choice.Parent.ClientRectangle.Contains(choice.Bounds), "The compact layout keeps " + name + " inside its group");
                }
                using (Bitmap image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.Controls[0].DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-compact-preview.png"), ImageFormat.Png);
                }
                form.Hide();
            }
            int resumed = 0;
            RingSettings dpiSettings = settings.Copy();
            dpiSettings.Trigger = "F8";
            dpiSettings.Button = "dpi";
            using (BackgroundSettingsPreview paused = new BackgroundSettingsPreview(dpiSettings, delegate { resumed++; }, true))
            {
                paused.ShowInTaskbar = false;
                paused.Show();
                Application.DoEvents();
                Check(paused.IsSetupPaused, "A reopened editor clearly retains its paused setup state");
                Check(paused.Controls.Find("PausedSetupNotice", true)[0].Visible, "The paused setup banner is visible");
                FlowLayoutPanel assignment = (FlowLayoutPanel)paused.Controls.Find("ButtonAssignment", true)[0];
                Check(!assignment.HorizontalScroll.Visible, "The paused DPI setup does not force horizontal scrolling");
                paused.Close();
                Check(resumed == 1, "Closing a clean setup editor resumes the saved ring");
            }
            RingGeometry geometry = new RingGeometry(new Point(220, 220), new Rectangle(0, 0, 440, 440), 1);
            using (Bitmap image = new Bitmap(440, 440))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                    RingPainter.Draw(graphics, geometry, settings.Actions, 2, false);
                for (int i = 0; i < 8; i++)
                {
                    RectangleF bubble = geometry.Bubble(i);
                    Color pixel = image.GetPixel((int)(bubble.X + bubble.Width / 2 + 38), (int)(bubble.Y + 45));
                    Check(pixel.ToArgb() == (i == 2 ? Theme.Accent : Theme.Surface).ToArgb(), "Visible bubble " + (i + 1));
                }
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ring-preview.png"), ImageFormat.Png);
                using (Graphics graphics = Graphics.FromImage(image))
                    RingPainter.Draw(graphics, geometry, RingSettings.Preset("Media"), 3, false);
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "media-ring-preview.png"), ImageFormat.Png);
            }
            Console.WriteLine("PASS: editable settings and eight-bubble rendering (previews saved to bin)");
        }

        private static void TestNativeMediaDelivery()
        {
            using (MediaProbeForm monitor = new MediaProbeForm())
            {
                monitor.StartMonitoring();
                foreach (string command in new[]
                {
                    "VolumeUp", "VolumeDown", "VolumeMute", "MediaPlayPause",
                    "MediaNextTrack", "MediaPreviousTrack", "MediaStop", "MediaSelect"
                })
                {
                    int before = monitor.EventCount;
                    ShortcutSender.Send(KeyboardShortcut.Parse(command, false));
                    PumpUntil(delegate { return monitor.EventCount == before + 2; }, "Windows receives the native " + command + " down/up pair");
                    Check(monitor.LastEvent.EndsWith(" UP", StringComparison.Ordinal), "Release the native " + command + " key");
                }

            }
            Console.WriteLine("PASS: real SendInput media delivery captured before changing system volume or playback");
        }

        private static void TestActionDialogRendering()
        {
            using (BackgroundActionPreview dialog = new BackgroundActionPreview(RingSettings.Preset("Media")[0]))
            {
                IntPtr foreground = Native.GetForegroundWindow();
                dialog.ShowInTaskbar = false;
                dialog.Show();
                Application.DoEvents();
                Check(Native.GetForegroundWindow() == foreground, "Dialog rendering does not interrupt the active application");
                Check(dialog.Font.SizeInPoints >= 11, "The action dialog uses the same readable text size");
                CheckDialogFields(dialog);
                using (Bitmap image = new Bitmap(dialog.ClientSize.Width, dialog.ClientSize.Height))
                {
                    dialog.Controls[0].DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "action-dialog-preview.png"), ImageFormat.Png);
                }
                dialog.Close();
            }
            Console.WriteLine("PASS: refined action dialog layout and native editable fields");
        }

        private static void CheckDialogFields(Control root)
        {
            foreach (Control child in root.Controls)
            {
                TextBox input = child as TextBox;
                if (input != null)
                {
                    Check(input.Parent is RoundedField, "Editable action fields use a rounded surface");
                    Check(input.Parent.ClientRectangle.Contains(input.Bounds), "Text remains inside the rounded action field");
                    Check(input.Height >= input.PreferredHeight, "The editable text height is not clipped");
                }
                CheckDialogFields(child);
            }
        }

        private static bool ContainsGrid(Control root)
        {
            if (root is DataGridView) return true;
            foreach (Control child in root.Controls) if (ContainsGrid(child)) return true;
            return false;
        }

        private static void TestRingDragDrop()
        {
            RingSettings original = RingSettings.CreateDefault();
            ActionDragSession drag = new ActionDragSession();
            using (RingEditorControl editor = new RingEditorControl(drag, original.Actions))
            {
                editor.Size = new Size(440, 440);
                RingAction media = RingSettings.Preset("Media")[0];
                DataObject payload = drag.Begin(media, -1);
                Check(editor.DropOnSlot(2, payload), "Drop an action from the palette onto a chosen ring position");
                Check(editor.ActionAt(2).Command == "MediaPlayPause" && editor.CopyActions().Count == 8,
                    "A drop replaces only the target and preserves eight positions");
                Check(original.Actions[2].Command == "Ctrl+X", "Editing does not mutate live settings");
                drag.End();
                Check(!editor.DropOnSlot(1, payload), "Expired drag tokens cannot change the ring");
                DataObject external = new DataObject();
                external.SetData(DataFormats.UnicodeText, "untrusted external text");
                Check(!editor.DropOnSlot(1, external), "Only this editor's action drags are accepted");
                payload = drag.Begin(editor.ActionAt(0), 0);
                Check(editor.DropOnSlot(4, payload), "Rearrange ring positions by dragging");
                Check(editor.ActionAt(4).Command == "Ctrl+C" && editor.ActionAt(0).Command == "Ctrl+Y",
                    "Rearrangement swaps positions rather than dropping an action");
                drag.End();
                Throws<ArgumentOutOfRangeException>(delegate { editor.DropOnSlot(8, payload); }, "Reject a ninth drop target");
                RingAction invalid = media.Copy();
                invalid.Label = "";
                Throws<SettingsException>(delegate { editor.Assign(0, invalid); }, "Validate custom actions before replacing a slot");
                Check(editor.ActionAt(0).Command == "Ctrl+Y", "An invalid action leaves the ring unchanged");
                List<RingAction> incomplete = original.Copy().Actions;
                incomplete[3] = null;
                Throws<SettingsException>(delegate { editor.SetActions(incomplete); }, "Reject incomplete action sets");
                Check(editor.CopyActions().Count == 8, "An invalid replacement does not damage the editor");
                editor.SelectSlot(0);
                string moved = editor.ActionAt(0).Command;
                editor.MoveSelected(1);
                Check(editor.SelectedSlot == 1 && editor.ActionAt(1).Command == moved, "Keyboard reordering moves a selected action clockwise");
                editor.MoveSelected(-1);
                Check(editor.SelectedSlot == 0 && editor.ActionAt(0).Command == moved, "Keyboard reordering can move it back");
                Check(editor.AccessibilityObject.GetChildCount() == 9, "Expose eight ring positions and the edit hub to accessibility");
                int edits = 0;
                editor.EditRequested += delegate { edits++; };
                editor.AccessibilityObject.GetChild(8).DoDefaultAction();
                Application.DoEvents();
                Check(edits == 1, "The center edit affordance performs the advertised action");
                editor.AccessibilityObject.GetChild(4).DoDefaultAction();
                Application.DoEvents();
                Check(editor.SelectedSlot == 4, "Accessible ring positions are selectable without pointer injection");
            }
            using (MouseButtonGuide guide = new MouseButtonGuide())
            {
                foreach (Size size in new[] { new Size(224, 266), new Size(228, 200), new Size(448, 532), new Size(160, 220), new Size(360, 240) })
                {
                    guide.Size = size;
                    foreach (EditorButton button in new[] { EditorButton.Dpi, EditorButton.Middle, EditorButton.Back, EditorButton.Forward })
                    {
                        Rectangle target = guide.ButtonBounds(button);
                        Check(guide.ClientRectangle.Contains(target), "The mouse button hotspot remains inside the illustration");
                        Check(guide.ButtonAt(new Point(target.Left + target.Width / 2, target.Top + target.Height / 2)) == button,
                            "The illustrated " + button + " button matches its hit target at " + size);
                    }
                    Check(guide.ButtonBounds(EditorButton.Dpi).Top > guide.ButtonBounds(EditorButton.Middle).Bottom,
                        "The DPI switch remains below, and separate from, the main wheel");
                    Check(guide.ButtonBounds(EditorButton.Back).Top > guide.ButtonBounds(EditorButton.Forward).Bottom,
                        "The two side buttons retain their physical order");
                }
            }
            Console.WriteLine("PASS: ring-first drag/drop, position swaps, isolated drafts and physical button guide");
        }

        private static void TestVisualDesign()
        {
            Check(Contrast(Theme.Text, Theme.Surface) >= 7, "Primary text has strong contrast on action cards");
            Check(Contrast(Theme.Muted, Theme.Surface) >= 4.5, "Secondary text meets normal-text contrast guidance");
            Check(Contrast(Theme.Background, Theme.Accent) >= 7, "Selected controls have readable dark-on-gold text");
            using (System.Drawing.Drawing2D.GraphicsPath shape = Theme.RoundedRectangle(new RectangleF(0, 0, 100, 40), 10))
            {
                Check(!shape.IsVisible(1, 1) && shape.IsVisible(50, 20), "Rounded surfaces soften corners without losing their center");
            }
            using (Bitmap bitmap = new Bitmap(400, 400))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                using (Font font = new Font("Segoe UI Semibold", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat format = new StringFormat { FormatFlags = StringFormatFlags.LineLimit })
                {
                    foreach (string set in new[] { "Productivity", "Media", "Windows" })
                    foreach (RingAction action in RingSettings.Preset(set))
                    {
                        int characters, lines;
                        graphics.MeasureString(action.Label, font, new SizeF(96 * scale, 38 * scale), format, out characters, out lines);
                        Check(characters == action.Label.Length && lines <= 2,
                            "Larger ring text displays the full " + action.Label + " label at scale " + scale);
                    }
                }
            }
            using (Panel parent = new Panel { BackColor = Theme.Background })
            using (RoundedButton button = new RoundedButton
            {
                Size = new Size(200, 80), Text = "Save changes", BackColor = Theme.Accent, Font = new Font("Segoe UI", 11)
            })
            {
                parent.Controls.Add(button);
                using (Bitmap image = new Bitmap(button.Width, button.Height))
                {
                    button.DrawToBitmap(image, button.ClientRectangle);
                    Check(image.GetPixel(0, 0).ToArgb() == Theme.Background.ToArgb(), "A rounded button has no square filled corner");
                    Check(image.GetPixel(button.Width / 2, 8).ToArgb() == Theme.Accent.ToArgb(), "The primary button keeps its gold action surface");
                }
                bool clicked = false;
                button.Click += delegate { clicked = true; };
                button.PerformClick();
                Check(clicked, "Rounded styling preserves normal button activation");
            }
            using (TextBox input = new TextBox { Text = "F8", Font = new Font("Segoe UI", 11) })
            using (RoundedField field = new RoundedField(input) { Size = new Size(200, 60) })
            {
                field.CreateControl();
                field.PerformLayout();
                Check(field.ClientRectangle.Contains(input.Bounds), "The rounded field keeps its real editable text inside its bounds");
                Check(input.BorderStyle == BorderStyle.None && input.Text == "F8", "Rounded input retains a standard editable text provider");
            }
            using (MouseButtonGuide guide = new MouseButtonGuide { Size = new Size(448, 532), SelectedButton = EditorButton.Dpi })
            using (Bitmap image = new Bitmap(448, 532))
            {
                guide.DrawToBitmap(image, guide.ClientRectangle);
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mouse-guide-preview.png"), ImageFormat.Png);
            }
            Console.WriteLine("PASS: readable contrast, larger type, rounded controls and MD301-shaped guide");
        }

        private static double Contrast(Color a, Color b)
        {
            double left = Luminance(a), right = Luminance(b);
            return (Math.Max(left, right) + 0.05) / (Math.Min(left, right) + 0.05);
        }

        private static double Luminance(Color color)
        {
            double[] channels = { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
            for (int i = 0; i < channels.Length; i++)
                channels[i] = channels[i] <= 0.04045 ? channels[i] / 12.92 : Math.Pow((channels[i] + 0.055) / 1.055, 2.4);
            return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
        }

        private static void TestNativeWindows()
        {
            KeyboardShortcut shortcut = KeyboardShortcut.Parse("Ctrl+Alt+Shift+F24", true);
            using (HotkeyWindow first = new HotkeyWindow())
            using (HotkeyWindow second = new HotkeyWindow())
            {
                int firstId = first.Register(shortcut);
                Throws<Win32Exception>(delegate { second.Register(shortcut); }, "Report real global hotkey conflicts");
                first.Unregister(firstId);
                int secondId = second.Register(shortcut);
                bool fired = false;
                second.Triggered += delegate { fired = true; };
                Check(Native.PostMessage(second.Handle, Native.HotkeyMessage, new IntPtr(secondId), IntPtr.Zero), "Post native hotkey message");
                PumpUntil(delegate { return fired; }, "Dispatch Windows hotkey messages");
            }
            using (HotkeyWindow afterDispose = new HotkeyWindow())
                afterDispose.Register(shortcut);

            using (RingForm ring = new RingForm())
            {
                IntPtr foreground = Native.GetForegroundWindow();
                RingAction chosen = null;
                IntPtr destination = IntPtr.Zero;
                bool wasPreview = false;
                ring.ActionChosen += delegate(RingAction action, IntPtr target, bool preview)
                {
                    Check(!ring.Visible, "Hide the overlay before dispatching an action");
                    chosen = action; destination = target; wasPreview = preview;
                };
                ring.ShowMenu(RingSettings.CreateDefault(), foreground, true);
                Check(ring.Visible, "Native overlay opens");
                Check(Native.GetForegroundWindow() == foreground, "Opening the ring does not steal focus");
                Check(!ring.Region.IsVisible(0, 0), "Overlay has a circular window region");
                ring.CompleteSelection(7);
                PumpUntil(delegate { return chosen != null; }, "Dispatch the eighth action");
                Check(chosen.Label == "Screenshot" && destination == foreground && wasPreview, "Preserve action, destination and inert preview mode");
                Check(Native.GetForegroundWindow() == foreground, "Closing the ring does not steal focus");
                chosen = null;
                ring.ShowMenu(RingSettings.CreateDefault(), foreground, true);
                ring.CompleteSelection(-1);
                PumpUntil(delegate { return !ring.Visible; }, "Cancel native overlay");
                Check(chosen == null, "Cancel never executes an action");
                ring.ShowMenu(RingSettings.CreateDefault(), foreground, true);
                ring.CompleteSelection(0);
                ring.CloseMenu();
                ring.ShowMenu(RingSettings.CreateDefault(), foreground, true);
                Application.DoEvents();
                Check(chosen == null && ring.Visible, "A queued selection from a closed ring cannot affect a newly opened ring");
                ring.CloseMenu();
            }
            Console.WriteLine("PASS: native hotkeys, conflicts, cleanup, non-activating overlay, selection and cancellation");
        }

        private static void TestApplicationSettings(string directory)
        {
            SettingsStore store = new SettingsStore(Path.Combine(directory, "application.json"));
            RingSettings original = RingSettings.CreateDefault();
            original.Trigger = "Ctrl+Alt+Shift+F24";
            store.Save(original);
            KeyboardShortcut oldTrigger = KeyboardShortcut.Parse(original.Trigger, true);
            KeyboardShortcut nextTrigger = KeyboardShortcut.Parse("Ctrl+Alt+Shift+F22", true);
            KeyboardShortcut failedTrigger = KeyboardShortcut.Parse("Ctrl+Alt+Shift+F21", true);
            string requestName = "MD301Plus.SettingsRequest.Test." + Guid.NewGuid().ToString("N");
            using (RingApplication application = new RingApplication(false, false, store, requestName))
            using (HotkeyWindow probe = new HotkeyWindow())
            {
                Application.DoEvents();
                Check(!application.IsSettingsOpen, "A normal tray start keeps Settings closed");
                Throws<Win32Exception>(delegate { probe.Register(oldTrigger); }, "Tray application owns its configured trigger");
                application.PauseTriggerForSetup();
                Check(application.ActiveBinding == null, "Pause the trigger while recording the DPI key in Gear Link");
                int pausedId = probe.Register(oldTrigger);
                probe.Unregister(pausedId);
                application.ResumeSavedTrigger();
                Throws<Win32Exception>(delegate { probe.Register(oldTrigger); }, "Resume the saved trigger after Gear Link setup");
                int conflictId = probe.Register(KeyboardShortcut.Parse("Ctrl+Alt+Shift+F23", true));
                RingSettings draft = original.Copy();
                draft.Trigger = "Ctrl+Alt+Shift+F23";
                string before = File.ReadAllText(store.FilePath);
                Throws<Win32Exception>(delegate { application.ApplySettings(draft); }, "Conflicting trigger is reported before saving");
                Check(File.ReadAllText(store.FilePath) == before, "Hotkey conflict preserves persisted settings");
                Throws<Win32Exception>(delegate { probe.Register(oldTrigger); }, "Hotkey conflict preserves the previous binding");
                probe.Unregister(conflictId);

                draft.Trigger = nextTrigger.Display;
                application.ApplySettings(draft);
                Check(store.Load().Trigger == nextTrigger.Display, "Live settings persist the new trigger");
                int oldId = probe.Register(oldTrigger);
                probe.Unregister(oldId);
                Throws<Win32Exception>(delegate { probe.Register(nextTrigger); }, "New live trigger is registered");

                draft.Trigger = failedTrigger.Display;
                before = File.ReadAllText(store.FilePath);
                using (FileStream locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws<IOException>(delegate { application.ApplySettings(draft); }, "A failed disk save is reported");
                Check(File.ReadAllText(store.FilePath) == before, "Failed disk save preserves persisted settings");
                Throws<Win32Exception>(delegate { probe.Register(nextTrigger); }, "Failed disk save preserves the current binding");
                int releasedId = probe.Register(failedTrigger);
                probe.Unregister(releasedId);
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Failed atomic save cleans up its temporary file");

                draft.Trigger = "Mouse5";
                application.ApplySettings(draft);
                Check(store.Load().Trigger == "Mouse5" && application.ActiveBinding.MouseButton == MouseButtonTrigger.Forward,
                    "Saving switches from a keyboard trigger to a live native mouse binding");
                int freedKeyboard = probe.Register(nextTrigger);
                probe.Unregister(freedKeyboard);
                draft.Trigger = failedTrigger.Display;
                before = File.ReadAllText(store.FilePath);
                using (FileStream locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws<IOException>(delegate { application.ApplySettings(draft); }, "Cross-type assignment save failure is reported");
                Check(application.ActiveBinding.MouseButton == MouseButtonTrigger.Forward &&
                    File.ReadAllText(store.FilePath) == before, "Failed save preserves the active mouse binding and settings");
                int failedKeyboard = probe.Register(failedTrigger);
                probe.Unregister(failedKeyboard);

                draft.Trigger = "None";
                application.ApplySettings(draft);
                Check(store.Load().Trigger == "None" && !application.ActiveBinding.IsAssigned,
                    "Disabling assignment releases the mouse binding and persists the disabled state");
                Check(SettingsRequestWindow.Send(requestName), "Starting the app again reaches the running instance");
                PumpUntil(delegate { return application.IsSettingsOpen; },
                    "Starting the app again opens Settings in the running instance");
                application.Dispose();
                Check(!SettingsRequestWindow.Send(requestName), "Shutdown removes the running-instance request window");
                application.Dispose();
            }
            using (HotkeyWindow afterDispose = new HotkeyWindow())
                afterDispose.Register(nextTrigger);
            using (RingApplication unassigned = new RingApplication(false, false, store, requestName))
                Check(!unassigned.ActiveBinding.IsAssigned, "Starting without an assignment claims no button or hotkey");
            Console.WriteLine("PASS: tray lifecycle, second-launch Settings handoff, keyboard/mouse/disabled transitions, binding rollback and idempotent shutdown");
        }

        private static void TestShellLauncher(string directory)
        {
            Check(!ShellLauncher.IsPackaged, "Unit tests run without Store package identity");
            string script = Path.Combine(directory, "desktop launch.cmd");
            string marker = Path.Combine(directory, "desktop-launch.txt");
            File.WriteAllText(script, "@echo launched> \"%~dp0desktop-launch.txt\"\r\n");
            ShellLauncher.OpenThroughDesktop(script, 0);
            Stopwatch timeout = Stopwatch.StartNew();
            string text = null;
            while (text == null && timeout.ElapsedMilliseconds < 15000)
            {
                Thread.Sleep(50);
                try { if (File.Exists(marker)) text = File.ReadAllText(marker).Trim(); }
                catch (IOException) { }
            }
            Check(text == "launched", "Explorer opens a launch target for the Store version");
            Console.WriteLine("PASS: Explorer opens launch targets outside the app");
        }

        private static void PumpUntil(Func<bool> done, string message)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!done() && timeout.ElapsedMilliseconds < 1500)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Check(done(), message);
        }
    }
}
