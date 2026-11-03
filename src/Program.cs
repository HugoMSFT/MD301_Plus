using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MD301Plus
{
    internal static class AppLog
    {
        private const long MaxBytes = 512 * 1024;
        internal static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MD301Plus");
        // Tests redirect this so they never write to the user's log.
        internal static string FilePath = Path.Combine(DirectoryPath, "app.log");

        internal static string Error(string operation, Exception error)
        {
            return Write(DateTime.UtcNow.ToString("O") + " " + operation + Environment.NewLine + error + Environment.NewLine);
        }

        // Records events that otherwise leave no trace, such as a ring closed by another window or a canceled shortcut.
        internal static void Note(string message)
        {
            Write(DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }

        private static string Write(string entry)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                FileInfo existing = new FileInfo(FilePath);
                if (existing.Exists && existing.Length > MaxBytes)
                {
                    File.Delete(FilePath + ".old");
                    File.Move(FilePath, FilePath + ".old");
                }
                File.AppendAllText(FilePath, entry);
                return "Details: " + FilePath;
            }
            catch (IOException logError)
            {
                Trace.TraceError(entry + "Log write failed: " + logError);
                return "Could not write the diagnostic log: " + logError.Message;
            }
            catch (UnauthorizedAccessException logError)
            {
                Trace.TraceError(entry + "Log write failed: " + logError);
                return "Could not write the diagnostic log: " + logError.Message;
            }
        }

        internal static void ShowError(IWin32Window owner, string operation, Exception error)
        {
            MessageBox.Show(owner, error.Message + Environment.NewLine + Environment.NewLine + Error(operation, error),
                "MD301 Plus - " + operation, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal sealed class RingApplication : ApplicationContext
    {
        private readonly SettingsStore store;
        private readonly RingForm ring;
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem resumeMenu;
        private readonly System.Windows.Forms.Timer executionTimer;
        private readonly SettingsRequestWindow settingsRequests;
        private readonly LifetimeWindow lifetime;
        private readonly MouseBattery battery;
        private readonly Stopwatch wait = new Stopwatch();
        private readonly Stopwatch sinceOpened = new Stopwatch();
        internal const int RepeatTriggerMilliseconds = 250;
        private RingSettings settings;
        private SettingsForm editor;
        private TriggerRegistration registration;
        private RingAction pendingAction;
        private IntPtr pendingTarget;
        private bool disposed;
        internal TriggerBinding ActiveBinding { get { return registration == null ? null : registration.Binding; } }
        internal bool IsSettingsOpen { get { return editor != null && !editor.IsDisposed && editor.Visible; } }
        internal string TrayToolTip { get { return tray.Text; } }
        internal ContextMenuStrip TrayMenu { get { return trayMenu; } }

        internal RingApplication(bool openSettings, bool openPreview)
            : this(openSettings, openPreview, new SettingsStore(Path.Combine(AppLog.DirectoryPath, "settings.json")),
                SettingsRequestWindow.DefaultName)
        {
        }

        internal RingApplication(bool openSettings, bool openPreview, SettingsStore store, string settingsRequestName)
        {
            this.store = store;
            bool firstRun = !File.Exists(store.FilePath);
            settings = store.Load();
            if (firstRun) store.Save(settings);
            Win32Exception registrationError = null;
            try { registration = new TriggerRegistration(TriggerBinding.Parse(settings.Trigger), ToggleRing); }
            catch (Win32Exception ex) { registrationError = ex; }
            icon = CreateIcon();
            ring = new RingForm { Icon = icon };
            ring.ActionChosen += ChooseAction;
            battery = new MouseBattery();
            battery.Changed += ShowBattery;
            executionTimer = new System.Windows.Forms.Timer { Interval = 20 };
            executionTimer.Tick += ExecutePending;
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(new ToolStripMenuItem(AppInfo.DisplayVersion) { Name = "AppVersion", Enabled = false });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Settings...", null, delegate { ShowSettings(); });
            trayMenu.Items.Add("Preview ring", null, delegate { ShowPreview(settings); });
            resumeMenu = new ToolStripMenuItem("Resume saved ring") { Enabled = false };
            resumeMenu.Click += delegate
            {
                try { ResumeSavedTrigger(); }
                catch (Win32Exception ex) { ReportActionError("Cannot resume trigger", ex); }
            };
            trayMenu.Items.Add(resumeMenu);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, delegate { ExitThread(); });
            tray = new NotifyIcon
            {
                Icon = icon, Text = AppInfo.TrayText(registration == null ? "trigger unavailable" :
                    TriggerBinding.Parse(settings.Trigger).Description),
                ContextMenuStrip = trayMenu, Visible = true
            };
            tray.DoubleClick += delegate { ShowSettings(); };
            settingsRequests = new SettingsRequestWindow(settingsRequestName, ShowSettings);
            lifetime = new LifetimeWindow(settingsRequestName + ".Lifetime", ExitThread);
            if (firstRun || openSettings || registrationError != null) ShowSettings();
            if (registrationError != null) AppLog.ShowError(editor, "Trigger unavailable", registrationError);
            if (openPreview) ShowPreview(settings);
        }

        internal bool IsRingOpen { get { return ring.Visible; } }

        internal void ToggleRing()
        {
            if (editor != null && (editor.IsCapturingButton || editor.IsDragging || editor.HasModalEditor)) return;
            if (ring.Visible)
            {
                // A second trigger right after opening is a bounced or repeated press, not a request to close.
                if (sinceOpened.IsRunning && sinceOpened.ElapsedMilliseconds < RepeatTriggerMilliseconds)
                {
                    AppLog.Note("Ignored a repeated trigger " + sinceOpened.ElapsedMilliseconds + " ms after the action ring opened.");
                    return;
                }
                ring.CloseMenu();
                return;
            }
            CancelPending();
            IntPtr target = Native.GetForegroundWindow();
            if (target == IntPtr.Zero)
            {
                AppLog.Note("Trigger received while no application was active; the action ring stayed closed.");
                Notify("No active application", "Open a normal desktop application before opening the action ring.", ToolTipIcon.Warning);
                return;
            }
            bool editorFocused = editor != null && editor.ContainsFocus;
            if (editorFocused) editor.NotifyTriggerReceived();
            battery.Refresh();
            try
            {
                ring.ShowMenu(settings, target, editorFocused);
                sinceOpened.Restart();
            }
            catch (ExternalException ex) { ReportActionError("Cannot open the action ring", ex); }
            catch (InvalidOperationException ex) { ReportActionError("Cannot open the action ring", ex); }
        }

        private void ShowBattery()
        {
            ring.BatteryPercent = battery.Percent;
            if (editor != null && !editor.IsDisposed) editor.RingEditor.BatteryPercent = battery.Percent;
        }

        private void ShowPreview(RingSettings draft)
        {
            CancelPending();
            battery.Refresh();
            sinceOpened.Reset();
            try { ring.ShowMenu(draft, Native.GetForegroundWindow(), true,
                registration == null ? TriggerBinding.Parse(settings.Trigger) : registration.Binding); }
            catch (ExternalException ex) { ReportActionError("Cannot show preview", ex); }
            catch (InvalidOperationException ex) { ReportActionError("Cannot show preview", ex); }
        }

        private void ShowSettings()
        {
            CancelPending();
            ring.CloseMenu();
            if (editor == null || editor.IsDisposed)
            {
                editor = new SettingsForm(settings, ApplySettings, ShowPreview, PauseTriggerForSetup, ResumeSavedTrigger, resumeMenu.Enabled);
                editor.Icon = icon;
                editor.RingEditor.BatteryPercent = battery.Percent;
                editor.FormClosed += delegate { editor = null; ring.CloseMenu(); };
                editor.Show();
                battery.Refresh();
            }
            else
            {
                if (editor.WindowState == FormWindowState.Minimized) editor.WindowState = FormWindowState.Normal;
                editor.Activate();
            }
        }

        internal void ApplySettings(RingSettings next)
        {
            next.Validate();
            TriggerBinding newTrigger = TriggerBinding.Parse(next.Trigger);
            bool changed = registration == null || !newTrigger.SameAs(registration.Binding);
            TriggerRegistration candidate = changed ? new TriggerRegistration(newTrigger, ToggleRing) : registration;
            bool saved = false;
            try
            {
                store.Save(next);
                saved = true;
            }
            finally
            {
                if (!saved && changed) candidate.Dispose();
            }
            TriggerRegistration previous = registration;
            settings = next.Copy();
            registration = candidate;
            resumeMenu.Enabled = false;
            if (editor != null) editor.SetSetupPaused(false);
            tray.Text = AppInfo.TrayText(newTrigger.Description);
            if (changed && previous != null) previous.Dispose();
        }

        internal void PauseTriggerForSetup()
        {
            CancelPending();
            ring.CloseMenu();
            if (registration != null) registration.Dispose();
            registration = null;
            resumeMenu.Enabled = true;
            if (editor != null) editor.SetSetupPaused(true);
            tray.Text = AppInfo.TrayText("paused for Gear Link setup");
        }

        internal void ResumeSavedTrigger()
        {
            if (registration != null) return;
            registration = new TriggerRegistration(TriggerBinding.Parse(settings.Trigger), ToggleRing);
            resumeMenu.Enabled = false;
            if (editor != null) editor.SetSetupPaused(false);
            tray.Text = AppInfo.TrayText(registration.Binding.Description);
        }

        private void ChooseAction(RingAction action, IntPtr target, bool preview)
        {
            if (preview)
            {
                Notify("Preview: " + action.Label, action.Command + "\r\nNo action was executed.", ToolTipIcon.Info);
                return;
            }
            pendingAction = action;
            pendingTarget = target;
            wait.Restart();
            executionTimer.Start();
        }

        private void ExecutePending(object sender, EventArgs args)
        {
            if (pendingAction == null) { CancelPending(); return; }
            // Media keys are global, so they still work when another window took the foreground.
            IntPtr foreground = Native.GetForegroundWindow();
            if (pendingAction.Kind == "shortcut" && !KeyboardShortcut.Parse(pendingAction.Command, false).IsMediaKey &&
                (!Native.IsWindow(pendingTarget) || foreground != pendingTarget))
            {
                AppLog.Note("Canceled \"" + pendingAction.Label + "\": " + Native.Describe(foreground) + " replaced " +
                    Native.Describe(pendingTarget) + " in front.");
                CancelPending();
                Notify("Shortcut canceled", "The active application changed. Nothing was sent.", ToolTipIcon.Info);
                return;
            }
            List<string> held = Native.HeldInputs(TriggerBinding.Parse(settings.Trigger));
            if (held.Count > 0)
            {
                if (wait.ElapsedMilliseconds >= 2000)
                {
                    AppLog.Note("Canceled \"" + pendingAction.Label + "\": still held after 2 seconds: " + string.Join(", ", held.ToArray()) + ".");
                    CancelPending();
                    Notify("Shortcut canceled", "Release the mouse button and keyboard modifiers, then try again.", ToolTipIcon.Warning);
                }
                return;
            }
            RingAction action = pendingAction;
            CancelPending();
            try
            {
                if (action.Kind == "shortcut")
                {
                    ShortcutSender.Send(KeyboardShortcut.Parse(action.Command, false));
                }
                else
                {
                    RingAction.ValidateLaunchTarget(action.Command);
                    Uri url;
                    bool web = Uri.TryCreate(action.Command, UriKind.Absolute, out url) &&
                        (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
                    if (!web && !File.Exists(action.Command) && !Directory.Exists(action.Command))
                        throw new FileNotFoundException("The launch target does not exist: " + action.Command);
                    ShellLauncher.Open(action.Command);
                }
            }
            catch (Win32Exception ex) { ReportActionError("Action failed", ex); }
            catch (IOException ex) { ReportActionError("Action failed", ex); }
            catch (UnauthorizedAccessException ex) { ReportActionError("Action failed", ex); }
            catch (InvalidOperationException ex) { ReportActionError("Action failed", ex); }
            catch (SettingsException ex) { ReportActionError("Action failed", ex); }
        }

        private void CancelPending()
        {
            executionTimer.Stop();
            pendingAction = null;
            pendingTarget = IntPtr.Zero;
            wait.Reset();
        }

        private void ReportActionError(string operation, Exception error)
        {
            string diagnostic = AppLog.Error(operation, error);
            Notify(operation, error.Message + "\r\n" + diagnostic, ToolTipIcon.Error);
        }

        private void Notify(string title, string message, ToolTipIcon type)
        {
            tray.ShowBalloonTip(7000, title.Length > 60 ? title.Substring(0, 60) : title,
                message.Length > 250 ? message.Substring(0, 247) + "..." : message, type);
        }

        internal static Icon CreateIcon()
        {
            using (Stream stream = typeof(RingApplication).Assembly.GetManifestResourceStream("MD301Plus.AppIcon"))
            {
                if (stream == null) throw new IOException("The embedded application icon is missing.");
                using (Icon embedded = new Icon(stream, SystemInformation.SmallIconSize))
                    return (Icon)embedded.Clone();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                battery.Dispose();
                lifetime.Dispose();
                settingsRequests.Dispose();
                executionTimer.Dispose();
                if (editor != null) editor.Dispose();
                ring.Dispose();
                if (registration != null) registration.Dispose();
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
                icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool openSettings = false, openPreview = false, startup = false;
            foreach (string arg in args)
            {
                if (arg == "--settings") openSettings = true;
                else if (arg == "--preview") openPreview = true;
                else if (arg == "--startup") startup = true;
                else
                {
                    MessageBox.Show("Usage: MD301Plus.exe [--settings] [--preview] [--startup]", "MD301 Plus");
                    return 2;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\MD301Plus", out created))
            {
                if (!created)
                {
                    // A sign-in launch must not open Settings over an already running copy.
                    if (startup) return 0;
                    if (!RequestSettingsFromRunningInstance())
                        MessageBox.Show("MD301 Plus is already running. Right-click its mouse icon in the notification area to open Settings.",
                            "MD301 Plus");
                    return 0;
                }
                try
                {
                    using (RingApplication context = new RingApplication(openSettings, openPreview))
                        Application.Run(context);
                    return 0;
                }
                catch (SettingsException ex) { AppLog.ShowError(null, "Cannot start", ex); return 1; }
                catch (IOException ex) { AppLog.ShowError(null, "Cannot start", ex); return 1; }
                catch (UnauthorizedAccessException ex) { AppLog.ShowError(null, "Cannot start", ex); return 1; }
                catch (ExternalException ex) { AppLog.ShowError(null, "Cannot start", ex); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static bool RequestSettingsFromRunningInstance()
        {
            // The running instance may still be starting (for example at sign-in), so retry briefly.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (SettingsRequestWindow.Send(SettingsRequestWindow.DefaultName)) return true;
                Thread.Sleep(100);
            }
            return false;
        }
    }
}
