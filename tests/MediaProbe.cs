using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MD301Plus.Tests
{
    internal sealed class MediaProbeForm : Form
    {
        private readonly Native.HookCallback callback;
        private readonly Label received;
        private readonly Label count;
        private IntPtr hook;
        private int events;
        internal int EventCount { get { return events; } }
        internal string LastEvent { get { return received.Text; } }

        internal MediaProbeForm()
        {
            Text = "MD301 Plus - Media UX test monitor";
            Font = new Font("Segoe UI", 11);
            ClientSize = new Size(620, 280);
            StartPosition = FormStartPosition.CenterScreen;
            Label help = new Label
            {
                Dock = DockStyle.Top, Height = 90, Padding = new Padding(16),
                Text = "Live test monitor: injected media commands are captured here before Windows changes volume or playback.\r\nPhysical media keys are not intercepted."
            };
            received = new Label { Name = "ReceivedMediaKey", Dock = DockStyle.Top, Height = 75, Padding = new Padding(16), Text = "Received: none" };
            count = new Label { Name = "MediaEventCount", Dock = DockStyle.Top, Height = 40, Padding = new Padding(16, 0, 0, 0), Text = "Events: 0" };
            Controls.Add(count);
            Controls.Add(received);
            Controls.Add(help);
            callback = KeyboardCallback;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartMonitoring();
        }

        internal void StartMonitoring()
        {
            if (hook != IntPtr.Zero) return;
            hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                Native.KeyboardHookData input = (Native.KeyboardHookData)Marshal.PtrToStructure(data, typeof(Native.KeyboardHookData));
                bool media = (input.VirtualKey >= 0xAD && input.VirtualKey <= 0xB3) || input.VirtualKey == 0xB5;
                if ((input.Flags & 0x10) != 0 && media)
                {
                    string key = ((Keys)input.VirtualKey).ToString();
                    bool up = (input.Flags & 0x80) != 0;
                    events++;
                    received.Text = "Received: " + key + (up ? " UP" : " DOWN");
                    count.Text = "Events: " + events;
                    return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        protected override void Dispose(bool disposing)
        {
            if (hook != IntPtr.Zero)
            {
                if (!Native.UnhookWindowsHookEx(hook))
                    Console.Error.WriteLine("Could not remove the media test hook: " + Marshal.GetLastWin32Error());
                hook = IntPtr.Zero;
            }
            GC.KeepAlive(callback);
            base.Dispose(disposing);
        }
    }
}
