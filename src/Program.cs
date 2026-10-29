using System;
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
        internal static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MD301Plus");
        internal static readonly string FilePath = Path.Combine(DirectoryPath, "app.log");

        internal static string Error(string operation, Exception error)
        {
            string entry = DateTime.UtcNow.ToString("O") + " " + operation + Environment.NewLine + error + Environment.NewLine;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
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
        private readonly Stopwatch wait = new Stopwatch();
        private RingSettings settings;
        private SettingsForm editor;
        private TriggerRegistration registration;
        private RingAction pendingAction;
        private IntPtr pendingTarget;
        private bool disposed;
        internal TriggerBinding ActiveBinding { get { return registration == null ? null : registration.Binding; } }

        internal RingApplication(bool openSettings, bool openPreview)
            : this(openSettings, openPreview, new SettingsStore(Path.Combine(AppLog.DirectoryPath, "settings.json")))
        {
        }

        internal RingApplication(bool openSettings, bool openPreview, SettingsStore store)
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
            executionTimer = new System.Windows.Forms.Timer { Interval = 20 };
            executionTimer.Tick += ExecutePending;
            trayMenu = new ContextMenuStrip();
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
                Icon = icon, Text = registration == null ? "MD301 Plus - trigger unavailable" :
                    "MD301 Plus - " + TriggerBinding.Parse(settings.Trigger).Description,
                ContextMenuStrip = trayMenu, Visible = true
            };
            tray.DoubleClick += delegate { ShowSettings(); };
            if (firstRun || openSettings || registrationError != null) ShowSettings();
            if (registrationError != null) AppLog.ShowError(editor, "Trigger unavailable", registrationError);
            if (openPreview) ShowPreview(settings);
        }

        private void ToggleRing()
        {
            if (editor != null && (editor.IsCapturingButton || editor.IsDragging || editor.HasModalEditor)) return;
            if (ring.Visible)
            {
                ring.CloseMenu();
                return;
            }
            CancelPending();
            IntPtr target = Native.GetForegroundWindow();
            if (target == IntPtr.Zero)
            {
                Notify("No active application", "Open a normal desktop application before opening the action ring.", ToolTipIcon.Warning);
                return;
            }
            bool editorFocused = editor != null && editor.ContainsFocus;
            if (editorFocused) editor.NotifyTriggerReceived();
            try { ring.ShowMenu(settings, target, editorFocused); }
            catch (ExternalException ex) { ReportActionError("Cannot open the action ring", ex); }
        }

        private void ShowPreview(RingSettings draft)
        {
            CancelPending();
            try { ring.ShowMenu(draft, Native.GetForegroundWindow(), true,
                registration == null ? TriggerBinding.Parse(settings.Trigger) : registration.Binding); }
            catch (ExternalException ex) { ReportActionError("Cannot show preview", ex); }
        }

        private void ShowSettings()
        {
            CancelPending();
            ring.CloseMenu();
            if (editor == null || editor.IsDisposed)
            {
                editor = new SettingsForm(settings, ApplySettings, ShowPreview, PauseTriggerForSetup, ResumeSavedTrigger, resumeMenu.Enabled);
                editor.Icon = icon;
                editor.FormClosed += delegate { editor = null; ring.CloseMenu(); };
                editor.Show();
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
            tray.Text = "MD301 Plus - " + newTrigger.Description;
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
            tray.Text = "MD301 Plus - paused for Gear Link setup";
        }

        internal void ResumeSavedTrigger()
        {
            if (registration != null) return;
            registration = new TriggerRegistration(TriggerBinding.Parse(settings.Trigger), ToggleRing);
            resumeMenu.Enabled = false;
            if (editor != null) editor.SetSetupPaused(false);
            tray.Text = "MD301 Plus - " + registration.Binding.Description;
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
            if (pendingAction.Kind == "shortcut" &&
                (!Native.IsWindow(pendingTarget) || Native.GetForegroundWindow() != pendingTarget))
            {
                CancelPending();
                Notify("Shortcut canceled", "The active application changed. Nothing was sent.", ToolTipIcon.Info);
                return;
            }
            if (Native.InputIsHeld(TriggerBinding.Parse(settings.Trigger)))
            {
                if (wait.ElapsedMilliseconds >= 2000)
                {
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
                    Process.Start(new ProcessStartInfo(action.Command) { UseShellExecute = true });
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
            bool openSettings = false, openPreview = false;
            foreach (string arg in args)
            {
                if (arg == "--settings") openSettings = true;
                else if (arg == "--preview") openPreview = true;
                else
                {
                    MessageBox.Show("Usage: MD301Plus.exe [--settings] [--preview]", "MD301 Plus");
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
    }
}
