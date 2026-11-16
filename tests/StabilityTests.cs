using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MD301Plus.Tests
{
    internal sealed class BackgroundRecoveryPreview : SettingsRecoveryForm
    {
        internal BackgroundRecoveryPreview(SettingsStore store, SettingsException failure) : base(store, failure) { }
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

    internal static partial class TestRunner
    {
        private static void TestStability(string directory)
        {
            TestConcurrentLogging();
            TestSettingsRecovery(directory);
            TestPaletteEnter();
            TestHookThread();
            TestMenuThreadDispatch();
            TestLaunchWorker(directory);
            TestGearLinkLaunchState();
            TestStoreLaunchPolicy();
            TestPartialInputDelivery();
            TestBatteryFreshness();
        }

        private static void TestConcurrentLogging()
        {
            const int workers = 8, entries = 32;
            string oversized = new string('x', 600 * 1024);
            File.WriteAllText(AppLog.FilePath, oversized);
            using (ManualResetEvent start = new ManualResetEvent(false))
            {
                Thread[] threads = new Thread[workers];
                for (int i = 0; i < threads.Length; i++)
                {
                    int writer = i;
                    threads[i] = new Thread(delegate()
                    {
                        start.WaitOne();
                        for (int entry = 0; entry < entries; entry++)
                            AppLog.Note("parallel-log-" + writer + "-" + entry);
                    });
                    threads[i].Start();
                }
                start.Set();
                foreach (Thread thread in threads)
                    Check(thread.Join(10000), "Concurrent diagnostic writers finish");
            }
            string[] lines = File.ReadAllLines(AppLog.FilePath);
            Check(lines.Length == workers * entries, "Concurrent diagnostic writes lose no entries");
            Check(File.ReadAllText(AppLog.FilePath + ".old") == oversized, "Concurrent writers rotate the old log exactly once");
            for (int writer = 0; writer < workers; writer++)
                for (int entry = 0; entry < entries; entry++)
                    Check(Array.Exists(lines, delegate(string line) { return line.EndsWith("parallel-log-" + writer + "-" + entry); }),
                        "Keep diagnostic entry " + writer + "-" + entry);
            File.Delete(AppLog.FilePath);
            File.Delete(AppLog.FilePath + ".old");
            Console.WriteLine("PASS: serialized concurrent diagnostic writes");
        }

        private static void TestSettingsRecovery(string directory)
        {
            SettingsStore store = new SettingsStore(Path.Combine(directory, "recovery.json"));
            Throws<FileNotFoundException>(delegate { store.LoadBackup(); }, "A missing backup must not look like a default configuration");
            RingSettings previous = RingSettings.CreateDefault();
            previous.Trigger = "F8";
            previous.Button = "dpi";
            previous.Actions[0].Label = "Previous copy";
            store.Save(previous);
            RingSettings next = previous.Copy();
            next.Actions[0].Label = "Latest copy";
            store.Save(next);
            Check(store.LoadBackup().Actions[0].Label == "Previous copy", "Saving keeps the previous validated settings");
            string backupBytes = File.ReadAllText(store.BackupPath);
            string currentBytes = File.ReadAllText(store.FilePath);
            using (FileStream locked = new FileStream(store.BackupPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Throws<IOException>(delegate { store.Save(previous); }, "A locked backup reports the failed save");
            Check(File.ReadAllText(store.FilePath) == currentBytes && File.ReadAllText(store.BackupPath) == backupBytes,
                "A failed backup replacement preserves both the current settings and backup");

            File.WriteAllText(store.FilePath, "{damaged settings");
            using (BackgroundRecoveryPreview recovery = new BackgroundRecoveryPreview(store, new SettingsException("The saved settings are unreadable.")))
            {
                IntPtr foreground = Native.GetForegroundWindow();
                recovery.ShowInTaskbar = false;
                recovery.Show();
                Application.DoEvents();
                Check(recovery.Controls.Find("RestoreSettingsBackup", true)[0].Enabled, "Recovery offers a valid backup");
                Check(recovery.AcceptButton == recovery.CancelButton, "Enter does not select a destructive recovery action by default");
                Check(Native.GetForegroundWindow() == foreground, "Recovery rendering does not interrupt the active application");
                using (Bitmap image = new Bitmap(recovery.ClientSize.Width, recovery.ClientSize.Height))
                {
                    recovery.Controls[0].DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-recovery-preview.png"), ImageFormat.Png);
                }
                recovery.Hide();
            }
            Check(File.ReadAllText(store.FilePath) == "{damaged settings", "Opening or canceling recovery never changes the original file");
            using (FileStream locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                Throws<IOException>(delegate { store.Recover(previous); }, "A locked original prevents recovery from overwriting it");
            Check(File.ReadAllText(store.FilePath) == "{damaged settings", "Failed recovery leaves the original intact");
            string preserved = store.Recover(store.LoadBackup());
            Check(preserved != null && File.ReadAllText(preserved) == "{damaged settings", "Recovery preserves the exact damaged bytes");
            RingSettings restored = store.Load();
            Check(restored.Trigger == "None" && restored.Button == null && restored.Actions[0].Label == "Previous copy",
                "Recovery restores actions but claims no mouse button or keyboard trigger");
            Check(previous.Trigger == "F8" && previous.Button == "dpi", "Recovery never mutates its input configuration");
            Check(File.ReadAllText(store.BackupPath) == backupBytes, "Recovery does not replace the good backup with damaged settings");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Settings failures clean up temporary files");

            File.WriteAllText(store.BackupPath, "{damaged backup");
            using (SettingsRecoveryForm recovery = new SettingsRecoveryForm(store, new SettingsException("Bad backup probe")))
                Check(!recovery.Controls.Find("RestoreSettingsBackup", true)[0].Enabled, "An invalid backup cannot be restored");
            RingSettings invalid = restored.Copy();
            invalid.Actions.RemoveAt(0);
            currentBytes = File.ReadAllText(store.FilePath);
            Throws<SettingsException>(delegate { store.Recover(invalid); }, "Recovery validates actions before changing any file");
            Check(File.ReadAllText(store.FilePath) == currentBytes, "Invalid recovery input leaves the current settings untouched");
            Console.WriteLine("PASS: settings backup, explicit recovery, original preservation and disabled recovered triggers");
        }

        private static void TestPaletteEnter()
        {
            int saves = 0;
            using (BackgroundSettingsPreview form = new BackgroundSettingsPreview(RingSettings.CreateDefault(), null, false,
                delegate { saves++; }))
            {
                IntPtr foreground = Native.GetForegroundWindow();
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                form.SelectCategory("Media");
                ActionPalette palette = (ActionPalette)form.Controls.Find("ActionPalette", true)[0];
                palette.SelectedIndex = 0;
                Message key = Message.Create(palette.Handle, 0x0100, new IntPtr((int)Keys.Enter), new IntPtr(0x001C0001));
                Check(palette.PreProcessMessage(ref key), "The shortcut palette consumes Enter before the default form button");
                Check(saves == 0 && form.RingEditor.ActionAt(0).Command == "MediaPlayPause",
                    "Enter assigns the highlighted action and never saves the draft");
                Check(Native.GetForegroundWindow() == foreground, "Keyboard regression coverage does not change the active application");
                form.Hide();
            }
            Console.WriteLine("PASS: palette Enter assigns without saving");
        }

        private static void TestHookThread()
        {
            int owner = Thread.CurrentThread.ManagedThreadId;
            int installed = 0, released = 0, callbackThread = 0, callbacks = 0;
            IntPtr hook = IntPtr.Zero;
            UIntPtr marker = new UIntPtr(0x4D443301);
            Native.HookCallback callback = delegate(int code, IntPtr message, IntPtr data)
            {
                if (code >= 0)
                {
                    Native.MouseHookData input = (Native.MouseHookData)Marshal.PtrToStructure(data, typeof(Native.MouseHookData));
                    if (input.ExtraInfo == marker)
                    {
                        callbackThread = Thread.CurrentThread.ManagedThreadId;
                        Interlocked.Increment(ref callbacks);
                    }
                }
                return Native.CallNextHookEx(hook, code, message, data);
            };
            using (HookThread worker = new HookThread(delegate
            {
                installed = Thread.CurrentThread.ManagedThreadId;
                hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            }, delegate
            {
                released = Thread.CurrentThread.ManagedThreadId;
                if (hook != IntPtr.Zero && !Native.UnhookWindowsHookEx(hook))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }))
            {
                Check(installed != owner && worker.IsRunning, "Native hooks run on a separate message-loop thread");
                uint accepted = 0;
                Point pointer = Cursor.Position;
                int direction = pointer.X > Screen.FromPoint(pointer).Bounds.Left ? -1 : 1;
                Thread input = new Thread(delegate()
                {
                    Thread.Sleep(50);
                    Native.Input[] moves = { new Native.Input() };
                    moves[0].Data.Mouse.X = direction;
                    moves[0].Data.Mouse.Flags = 0x0001 | 0x2000;
                    moves[0].Data.Mouse.ExtraInfo = marker;
                    accepted = Native.SendInput(1, moves, Marshal.SizeOf(typeof(Native.Input)));
                    Thread.Sleep(200);
                    moves[0].Data.Mouse.X = -direction;
                    accepted += Native.SendInput(1, moves, Marshal.SizeOf(typeof(Native.Input)));
                });
                input.Start();
                // Paired one-pixel moves generate real packets without clicking or scrolling.
                Thread.Sleep(1400);
                bool finished = input.Join(1000);
                Check(finished && accepted == 2 && callbacks >= 2,
                    "Native input is processed while the UI is blocked for longer than the Windows hook timeout" +
                    " (sender finished=" + finished + ", accepted=" + accepted + ", callbacks=" + callbacks + ")");
                Check(callbackThread == installed, "Hook callbacks stay on their installing thread");
                worker.Dispose();
                Check(!worker.IsRunning && released == installed, "Disposal releases hooks on their own thread and joins it");
            }
            GC.KeepAlive(callback);
            bool cleaned = false;
            Throws<Win32Exception>(delegate
            {
                new HookThread(delegate { throw new Win32Exception(5, "Hook installation probe"); }, delegate { cleaned = true; });
            }, "Input-thread startup errors reach the UI caller");
            Check(cleaned, "A failed hook installation still releases partially installed resources");
            bool reentered = false;
            // Disposing this context would destroy WinForms' shared UI marshalling control.
            WindowsFormsSynchronizationContext ui = new WindowsFormsSynchronizationContext();
            ui.Post(delegate { reentered = true; }, null);
            using (HookThread worker = new HookThread(delegate { Thread.Sleep(50); }, delegate { Thread.Sleep(50); }))
            {
                Check(!reentered, "Starting an input thread does not pump queued UI actions");
                worker.Dispose();
                Check(!reentered, "Stopping an input thread cannot reenter ring creation or disposal");
            }
            PumpUntil(delegate { return reentered; }, "Queued UI work resumes after input-thread cleanup");
            Console.WriteLine("PASS: input-thread isolation through a UI stall, native callbacks and startup/shutdown cleanup");
        }

        private static void TestMenuThreadDispatch()
        {
            int owner = Thread.CurrentThread.ManagedThreadId, hovered = -1, selected = -1, hoverThread = 0, selectionThread = 0;
            int produced = 0;
            bool swallowed = false;
            SynchronizationContext previous = SynchronizationContext.Current;
            MenuInputCapture created;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                created = new MenuInputCapture(delegate { return 4; },
                    delegate(int index) { hovered = index; hoverThread = Thread.CurrentThread.ManagedThreadId; },
                    delegate(int index) { selected = index; selectionThread = Thread.CurrentThread.ManagedThreadId; },
                    TriggerBinding.Parse("F22"));
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            using (MenuInputCapture capture = created)
            {
                Thread producer = new Thread(delegate()
                {
                    capture.Mouse(0x0200, Point.Empty, 0, true);
                    swallowed = capture.Mouse(0x0201, Point.Empty, 0, true) &&
                        capture.Mouse(0x0202, Point.Empty, 0, true);
                    Interlocked.Exchange(ref produced, 1);
                });
                producer.Start();
                WaitWithoutPumping(delegate { return Thread.VolatileRead(ref produced) == 1; }, "Menu callbacks do not wait for the UI thread");
                Check(swallowed && hovered == -1 && selected == -1, "Input decisions stay immediate while UI callbacks are queued");
                PumpUntil(delegate { return hovered == 4 && selected == 4; }, "Menu hover and selection arrive on the UI");
                Check(hoverThread == owner && selectionThread == owner, "Menu callbacks never access controls from the hook thread");
                Check(producer.Join(1500), "The menu input producer exits");
            }
            int stale = 0;
            produced = 0;
            using (MenuInputCapture capture = new MenuInputCapture(delegate { return 0; }, delegate { stale++; },
                delegate { stale++; }, TriggerBinding.Parse("F22")))
            {
                Thread producer = new Thread(delegate()
                {
                    capture.Mouse(0x0200, Point.Empty, 0, true);
                    Interlocked.Exchange(ref produced, 1);
                });
                producer.Start();
                WaitWithoutPumping(delegate { return Thread.VolatileRead(ref produced) == 1; }, "A queued callback producer finishes");
                capture.Dispose();
                Check(producer.Join(1500), "The closed-menu producer exits");
                Pump(20);
                Check(stale == 0, "Closing a menu drops queued callbacks from that capture");
            }
            Console.WriteLine("PASS: thread-safe menu decisions, UI dispatch and late-callback cancellation");
        }

        private static void TestLaunchWorker(string directory)
        {
            int owner = Thread.CurrentThread.ManagedThreadId, launched = 0, completions = 0, callbackThread = 0;
            ApartmentState apartment = ApartmentState.Unknown;
            Exception failure = null;
            using (ManualResetEvent started = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ActionLauncher launcher = new ActionLauncher(delegate
            {
                apartment = Thread.CurrentThread.GetApartmentState();
                launched++;
                started.Set();
                if (!release.WaitOne(5000)) throw new TimeoutException("Launch probe was not released.");
            }, delegate(Exception error)
            {
                completions++;
                callbackThread = Thread.CurrentThread.ManagedThreadId;
                failure = error;
            }))
            {
                launcher.Open("https://example.com/");
                try
                {
                    Check(started.WaitOne(1500) && launcher.IsBusy, "A slow launch runs independently of the UI");
                    Throws<InvalidOperationException>(delegate { launcher.Open("https://example.com/again"); },
                        "Slow launches cannot build an unbounded queue of delayed actions");
                    bool responsive = false;
                    WindowsFormsSynchronizationContext ui = new WindowsFormsSynchronizationContext();
                    ui.Post(delegate { responsive = Thread.CurrentThread.ManagedThreadId == owner; }, null);
                    PumpUntil(delegate { return responsive; }, "The UI keeps processing work during a slow launch");
                    Check(apartment == ApartmentState.STA && completions == 0, "Shell work uses an STA worker without premature success");
                }
                finally { release.Set(); }
                PumpUntil(delegate { return completions == 1; }, "Launch completion reaches the UI");
                Check(launched == 1 && failure == null && callbackThread == owner && !launcher.IsBusy,
                    "One successful launch reports completion on the UI thread");
                launcher.Open(Path.Combine(directory, "missing-launch-target.exe"));
                PumpUntil(delegate { return completions == 2; }, "Missing launch targets report a failure");
                Check(failure is FileNotFoundException && launched == 1, "An absent file is never passed to the shell");
                launcher.Open("cmd.exe /c invalid");
                PumpUntil(delegate { return completions == 3; }, "Invalid targets report a failure");
                Check(failure is SettingsException && launched == 1, "Invalid targets cannot reach the shell worker");
            }
            int late = 0;
            using (ManualResetEvent started = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ActionLauncher launcher = new ActionLauncher(delegate
            {
                started.Set();
                if (!release.WaitOne(5000)) throw new TimeoutException("Shutdown launch probe was not released.");
                throw new IOException("Launch failed after shutdown");
            }, delegate { late++; }))
            {
                launcher.Open("https://example.com/");
                try { Check(started.WaitOne(1500), "A shutdown probe starts"); }
                finally
                {
                    launcher.Dispose();
                    release.Set();
                }
                PumpUntil(delegate { return !launcher.IsBusy; }, "Shutdown does not block on a slow launch worker");
                Pump(20);
                Check(late == 0, "A disposed launcher never calls disposed UI controls");
                Throws<ObjectDisposedException>(delegate { launcher.Open("https://example.com/"); }, "A stopped launcher rejects new work");
            }
            int afterClose = 0;
            using (ManualResetEvent lookup = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ActionLauncher launcher = new ActionLauncher(delegate { afterClose++; }, delegate { afterClose++; },
                delegate
                {
                    lookup.Set();
                    if (!release.WaitOne(5000)) throw new TimeoutException("Path lookup probe was not released.");
                    return true;
                }))
            {
                launcher.Open(Path.Combine(directory, "slow-network-like-target.exe"));
                try { Check(lookup.WaitOne(1500), "A slow path lookup runs off the UI"); }
                finally
                {
                    launcher.Dispose();
                    release.Set();
                }
                PumpUntil(delegate { return !launcher.IsBusy; }, "A canceled lookup completes in the background");
                Pump(20);
                Check(afterClose == 0, "Finishing a path lookup after shutdown cannot open a delayed application");
            }
            Console.WriteLine("PASS: asynchronous STA launches, bounded work, explicit failures and shutdown-safe callbacks");
        }

        private static void TestStoreLaunchPolicy()
        {
            bool direct = false, desktop = false;
            ShellLauncher.Open("https://example.com/", true, delegate(string target, int show)
            {
                desktop = target == "https://example.com/" && show == 1;
            }, delegate { direct = true; });
            Check(desktop && !direct, "Store launches go through Explorer only");
            foreach (Exception failure in new Exception[]
            {
                new COMException("Explorer unavailable"), new InvalidCastException("Desktop object unavailable"),
                new TargetInvocationException(new COMException("ShellExecute failed")), new MissingMemberException("Shell missing"),
                new UnauthorizedAccessException("Desktop denied"), new ArgumentException("Desktop argument rejected")
            })
            {
                Exception cause = failure;
                Throws<InvalidOperationException>(delegate
                {
                    ShellLauncher.Open("https://example.com/", true, delegate { throw cause; }, delegate { direct = true; });
                }, "Store launch failures are reported without changing the app-data environment");
                Check(!direct, "An Explorer failure never falls back to launching inside the Store environment");
            }
            desktop = false;
            ShellLauncher.Open("https://example.com/", false, delegate { desktop = true; }, delegate { direct = true; });
            Check(direct && !desktop, "Unpackaged launches preserve normal shell behavior");
            Console.WriteLine("PASS: Store launch isolation and explicit Explorer failure handling");
        }

        private static void TestGearLinkLaunchState()
        {
            RingSettings settings = RingSettings.CreateDefault();
            settings.Trigger = "F8";
            settings.Button = "dpi";
            int pauses = 0, resumes = 0, saves = 0;
            using (ManualResetEvent opened = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (BackgroundSettingsPreview form = new BackgroundSettingsPreview(settings,
                resume: delegate { resumes++; }, save: delegate { saves++; }, pause: delegate { pauses++; },
                openLink: delegate
                {
                    opened.Set();
                    if (!release.WaitOne(5000)) throw new TimeoutException("Gear Link probe was not released.");
                }))
            {
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                Button button = (Button)form.Controls.Find("OpenGearLink", true)[0];
                button.PerformClick();
                try
                {
                    Check(opened.WaitOne(1500) && pauses == 1 && form.IsSetupPaused && !button.Enabled,
                        "Gear Link setup pauses before opening the browser and cannot launch twice");
                    ((Button)form.AcceptButton).PerformClick();
                    Check(saves == 1 && !form.IsSetupPaused, "A new saved assignment can resume the ring while the browser is opening");
                }
                finally { release.Set(); }
                PumpUntil(delegate { return button.Enabled; }, "Gear Link launch completion reaches the editor");
                Check(pauses == 1 && resumes == 0 && !form.IsSetupPaused,
                    "A late browser launch completion never pauses a newly saved assignment");
                form.Hide();
            }
            Console.WriteLine("PASS: Gear Link launch pause timing and protection against late setup-state changes");
        }

        private static void TestPartialInputDelivery()
        {
            int calls = 0;
            Native.Input[] cleanup = null;
            Throws<Win32Exception>(delegate
            {
                ShortcutSender.Send(KeyboardShortcut.Parse("Ctrl+Alt+F8", false),
                    delegate(uint count, Native.Input[] inputs, int size)
                    {
                        calls++;
                        if (calls == 1) return 2;
                        cleanup = inputs;
                        return count;
                    });
            }, "Partial SendInput delivery reports failure after cleanup");
            Check(calls == 2 && cleanup.Length == 2 &&
                cleanup[0].Data.Keyboard.VirtualKey == 0x12 && cleanup[1].Data.Keyboard.VirtualKey == 0x11 &&
                cleanup[0].Data.Keyboard.Flags == Native.KeyUp && cleanup[1].Data.Keyboard.Flags == Native.KeyUp,
                "Partial input releases only the accepted key-down events, in reverse order");
            calls = 0;
            try
            {
                ShortcutSender.Send(KeyboardShortcut.Parse("Ctrl+C", false), delegate
                {
                    calls++;
                    return calls == 1 ? 1u : 0u;
                });
                Check(false, "Cleanup rejection must report failure");
            }
            catch (Win32Exception error)
            {
                Check(error.Message.Contains("release Ctrl, Alt, Shift and Win manually"),
                    "Blocked modifier cleanup explicitly tells the user how to recover");
            }
            calls = 0;
            Throws<Win32Exception>(delegate
            {
                ShortcutSender.Send(KeyboardShortcut.Parse("Ctrl+C", false), delegate { calls++; return 0; });
            }, "Completely blocked input reports failure");
            Check(calls == 1, "Rejected input does not inject unnecessary release events");
            Console.WriteLine("PASS: partial and blocked input delivery, modifier cleanup and explicit failure messages");
        }

        private static void TestBatteryFreshness()
        {
            DateTime now = new DateTime(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc);
            int level = 64;
            BatteryRead state = BatteryRead.Ok;
            bool fail = false;
            using (MouseBattery battery = new MouseBattery(delegate(out int value)
            {
                if (fail) throw new IOException("battery-failure-probe");
                value = level;
                return state;
            }, false, delegate { return now; }))
            {
                Check(battery.Reading.State == BatteryRead.Pending && battery.Reading.Readout == "--",
                    "An initial battery query is pending, not a made-up percentage");
                battery.Poll();
                Check(battery.Reading.IsCurrent && battery.Reading.UpdatedUtc == now && battery.Reading.Readout == "64%",
                    "A valid reading records its successful update time");
                DateTime successful = now;
                now = now.AddMinutes(10);
                Check(!battery.Reading.IsCurrent && battery.Reading.Readout == "64%*" &&
                    battery.Reading.Description.Contains("last known"), "A ten-minute-old reading is visibly stale even if a device read is stuck");
                state = BatteryRead.Asleep;
                battery.Poll();
                Check(battery.Percent == 64 && battery.Reading.UpdatedUtc == successful &&
                    battery.Reading.Description.Contains("asleep or not responding"), "Sleeping devices keep an honestly labeled last-known reading");
                fail = true;
                battery.Poll();
                battery.Poll();
                Check(battery.Percent == 64 && battery.Reading.State == BatteryRead.Failed &&
                    battery.Reading.Description.Contains("read failed"), "Read failures are distinct from a missing receiver and retain only stale data");
                Check(RegexCount(File.ReadAllText(AppLog.FilePath), "battery-failure-probe") == 1,
                    "Consecutive battery exceptions are logged once");
                fail = false;
                state = BatteryRead.Ok;
                level = 80;
                now = now.AddMinutes(1);
                battery.Poll();
                Check(battery.Reading.IsCurrent && battery.Percent == 80 && battery.Reading.UpdatedUtc == now,
                    "A reconnect or resumed device refreshes its reading and timestamp");
                fail = true;
                battery.Poll();
                Check(RegexCount(File.ReadAllText(AppLog.FilePath), "battery-failure-probe") == 2,
                    "A new failure after recovery is logged again");
                fail = false;
                state = BatteryRead.Missing;
                battery.Poll();
                Check(battery.Reading.UpdatedUtc == null && battery.Reading.Readout == "--" &&
                    battery.Reading.Description.Contains("No compatible MD301 USB connection"),
                    "Unplugging the receiver clears the percentage and explains the supported connection");
                state = BatteryRead.Ok;
                level = 101;
                battery.Poll();
                Check(battery.Reading.State == BatteryRead.Failed && battery.Percent == MouseBattery.Unknown,
                    "Impossible device values never become displayed percentages");
            }
            BatteryReading stale = new BatteryReading(50, BatteryRead.Asleep, now, delegate { return now; });
            RingGeometry geometry = new RingGeometry(new Point(240, 240), new Rectangle(0, 0, 480, 480), 1);
            using (Bitmap image = new Bitmap(480, 480))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                    RingPainter.Draw(graphics, geometry, RingSettings.CreateDefault().Actions, -1, false, false, 50, stale);
                Check(GaugePixel(image, geometry, -90 + RingPainter.BatteryGapDegrees + 20).ToArgb() == Theme.Muted.ToArgb(),
                    "A cached battery gauge is muted rather than claiming a fresh green reading");
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "battery-stale-preview.png"), ImageFormat.Png);
            }
            using (RingEditorControl editor = new RingEditorControl(new ActionDragSession(), RingSettings.CreateDefault().Actions))
            using (RingForm ring = new RingForm())
            {
                editor.BatteryStatus = stale;
                ring.BatteryStatus = stale;
                Check(editor.AccessibleDescription.Contains("last known") && ring.AccessibleDescription.Contains("last known"),
                    "Both ring surfaces expose battery freshness to accessibility");
                BatteryReading fresh = new BatteryReading(50, BatteryRead.Ok, now, delegate { return now; });
                editor.BatteryStatus = fresh;
                ring.BatteryStatus = fresh;
                now = now.AddMinutes(10);
                editor.RefreshBatteryStatus();
                ring.RefreshBatteryStatus();
                Check(editor.AccessibleDescription.Contains("last known") && ring.AccessibleDescription.Contains("last known"),
                    "Visible battery surfaces become stale without waiting for a stuck device query to return");
            }
            Pump(20);
            Console.WriteLine("PASS: battery freshness threshold, asleep/missing/failed states, reconnects and accessible stale rendering");
        }

        private static int RegexCount(string text, string value)
        {
            return System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(value)).Count;
        }

        private static void WaitWithoutPumping(Func<bool> done, string message)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!done() && timeout.ElapsedMilliseconds < 1500) Thread.Sleep(1);
            Check(done(), message);
        }
    }
}
