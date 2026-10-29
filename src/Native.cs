using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MD301Plus
{
    internal static class Native
    {
        internal const int HotkeyMessage = 0x0312;
        internal const uint NoRepeat = 0x4000;
        internal const uint KeyUp = 0x0002;
        internal const uint ExtendedKey = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint
        {
            public int X;
            public int Y;
            public NativePoint(Point point) { X = point.X; Y = point.Y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseHookData
        {
            public NativePoint Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardHookData
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        internal delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int type, HookCallback callback, IntPtr module, uint thread);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, [In] Input[] inputs, int inputSize);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("shcore.dll")]
        internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllowSetForegroundWindow(uint processId);

        internal static float DpiScale(Point point)
        {
            uint x, y;
            int result = GetDpiForMonitor(MonitorFromPoint(new NativePoint(point), 2), 0, out x, out y);
            if (result != 0 || x == 0)
                throw new ExternalException("Windows could not report the monitor's DPI.", result);
            return x / 96f;
        }

        internal static bool IsDown(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        internal static bool InputIsHeld(TriggerBinding trigger)
        {
            return IsDown(0x10) || IsDown(0x11) || IsDown(0x12) ||
                IsDown(0x5B) || IsDown(0x5C) || IsDown(0x01) ||
                IsDown(0x02) || IsDown(0x04) || IsDown(0x05) || IsDown(0x06) ||
                (trigger.Keyboard != null && IsDown(trigger.Keyboard.Key));
        }

        internal static uint CurrentModifiers()
        {
            uint result = 0;
            if (IsDown(0x11)) result |= KeyboardShortcut.Control;
            if (IsDown(0x12)) result |= KeyboardShortcut.Alt;
            if (IsDown(0x10)) result |= KeyboardShortcut.Shift;
            if (IsDown(0x5B) || IsDown(0x5C)) result |= KeyboardShortcut.Windows;
            return result;
        }

        internal static MouseButtonTrigger TriggerButton(int message, uint mouseData)
        {
            if (message == 0x0207 || message == 0x0208) return MouseButtonTrigger.Middle;
            if (message != 0x020B && message != 0x020C) return MouseButtonTrigger.None;
            uint xbutton = mouseData >> 16;
            return xbutton == 1 ? MouseButtonTrigger.Back :
                (xbutton == 2 ? MouseButtonTrigger.Forward : MouseButtonTrigger.None);
        }

        internal static bool IsTriggerButtonDown(int message)
        {
            return message == 0x0207 || message == 0x020B;
        }
    }

    internal sealed class MouseButtonListener : NativeWindow, IDisposable
    {
        private const int ClickMessage = 0x8001;
        private readonly MouseButtonTrigger button;
        private readonly Action<MouseButtonTrigger> triggered;
        private readonly Native.HookCallback callback;
        private IntPtr hook;
        private MouseButtonTrigger pressed;

        internal MouseButtonListener(MouseButtonTrigger button, Action<MouseButtonTrigger> triggered)
        {
            this.button = button;
            this.triggered = triggered;
            callback = MouseCallback;
            CreateHandle(new CreateParams { Caption = "MD301Plus.MouseButton", Parent = new IntPtr(-3) });
        }

        internal void Start()
        {
            if (Handle == IntPtr.Zero) throw new ObjectDisposedException("MouseButtonListener");
            if (hook != IntPtr.Zero) throw new InvalidOperationException("The mouse-button listener is already running.");
            hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot listen for the assigned mouse button.");
        }

        private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code >= 0 && (kind == 0x0207 || kind == 0x0208 || kind == 0x020B || kind == 0x020C))
            {
                Native.MouseHookData input = (Native.MouseHookData)Marshal.PtrToStructure(data, typeof(Native.MouseHookData));
                if (ProcessMouseEvent(kind, input.MouseData)) return new IntPtr(1);
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        internal bool ProcessMouseEvent(int message, uint mouseData)
        {
            MouseButtonTrigger current = Native.TriggerButton(message, mouseData);
            if (current == MouseButtonTrigger.None || (button != MouseButtonTrigger.None && current != button))
                return false;
            if (Native.IsTriggerButtonDown(message))
            {
                pressed = current;
            }
            else if (pressed == current)
            {
                pressed = MouseButtonTrigger.None;
                if (!Native.PostMessage(Handle, ClickMessage, new IntPtr((int)current), IntPtr.Zero))
                    AppLog.Error("Dispatching a mouse-button trigger", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ClickMessage)
            {
                triggered((MouseButtonTrigger)message.WParam.ToInt32());
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero && !Native.UnhookWindowsHookEx(hook))
                AppLog.Error("Releasing the assigned mouse button", new Win32Exception(Marshal.GetLastWin32Error()));
            hook = IntPtr.Zero;
            if (Handle != IntPtr.Zero) DestroyHandle();
            GC.KeepAlive(callback);
        }
    }

    internal sealed class TriggerRegistration : IDisposable
    {
        private HotkeyWindow keyboard;
        private MouseButtonListener mouse;
        internal TriggerBinding Binding { get; private set; }

        internal TriggerRegistration(TriggerBinding binding, Action triggered)
        {
            Binding = binding;
            if (!binding.IsAssigned) return;
            try
            {
                if (binding.Keyboard != null)
                {
                    keyboard = new HotkeyWindow();
                    keyboard.Register(binding.Keyboard);
                    keyboard.Triggered += triggered;
                }
                else
                {
                    mouse = new MouseButtonListener(binding.MouseButton, delegate { triggered(); });
                    mouse.Start();
                }
            }
            catch (Win32Exception)
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (keyboard != null) { keyboard.Dispose(); keyboard = null; }
            if (mouse != null) { mouse.Dispose(); mouse = null; }
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly HashSet<int> registrations = new HashSet<int>();
        private int nextId = 1;
        internal event Action Triggered;

        internal HotkeyWindow()
        {
            CreateHandle(new CreateParams { Caption = "MD301Plus.Hotkeys", Parent = new IntPtr(-3) });
        }

        internal int Register(KeyboardShortcut shortcut)
        {
            int id = nextId++;
            if (!Native.RegisterHotKey(Handle, id, shortcut.Modifiers | Native.NoRepeat, shortcut.Key))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot register " + shortcut.Display + ". It may be reserved or in use. Choose another trigger.");
            registrations.Add(id);
            return id;
        }

        internal void Unregister(int id)
        {
            if (!registrations.Contains(id))
                return;
            if (!Native.UnregisterHotKey(Handle, id))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not release the previous keyboard trigger.");
            registrations.Remove(id);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.HotkeyMessage && registrations.Contains(message.WParam.ToInt32()))
            {
                Action handler = Triggered;
                if (handler != null) handler();
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            foreach (int id in registrations)
                if (!Native.UnregisterHotKey(Handle, id))
                    AppLog.Error("Releasing a keyboard trigger", new Win32Exception(Marshal.GetLastWin32Error()));
            registrations.Clear();
            DestroyHandle();
        }
    }

    // Lets a second launch (for example from the Start menu) open Settings in the running instance.
    internal sealed class SettingsRequestWindow : NativeWindow, IDisposable
    {
        internal const string DefaultName = "MD301Plus.SettingsRequest";
        private const int RequestMessage = 0x8001;
        private readonly Action requested;

        internal SettingsRequestWindow(string name, Action requested)
        {
            this.requested = requested;
            CreateHandle(new CreateParams { Caption = name, Parent = new IntPtr(-3) });
        }

        internal static bool Send(string name)
        {
            IntPtr window = Native.FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, name);
            if (window == IntPtr.Zero) return false;
            uint process;
            // The launching process may pass its foreground permission so Settings appears in front.
            if (Native.GetWindowThreadProcessId(window, out process) != 0) Native.AllowSetForegroundWindow(process);
            return Native.PostMessage(window, RequestMessage, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == RequestMessage)
            {
                requested();
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    internal sealed class MenuInputCapture : IDisposable
    {
        private readonly Native.HookCallback mouseCallback;
        private readonly Native.HookCallback keyboardCallback;
        private readonly Func<Point, int> hitTest;
        private readonly Action<int> hover;
        private readonly Action<int> complete;
        private readonly TriggerBinding trigger;
        private IntPtr mouseHook;
        private IntPtr keyboardHook;
        private int pendingButton;
        private int pendingAction = -1;
        private int pendingKey;
        private bool completed;

        internal MenuInputCapture(Func<Point, int> hitTest, Action<int> hover, Action<int> complete, TriggerBinding trigger)
        {
            this.hitTest = hitTest;
            this.hover = hover;
            this.complete = complete;
            this.trigger = trigger;
            mouseCallback = MouseCallback;
            keyboardCallback = KeyboardCallback;
        }

        internal void Start()
        {
            IntPtr module = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
            if (mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot listen for menu clicks.");
            keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, module, 0);
            if (keyboardHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot listen for Escape and action numbers.");
        }

        private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || completed)
                return Native.CallNextHookEx(mouseHook, code, message, data);
            int kind = message.ToInt32();
            Native.MouseHookData input = (Native.MouseHookData)Marshal.PtrToStructure(data, typeof(Native.MouseHookData));
            int index = hitTest(new Point(input.Point.X, input.Point.Y));
            if (kind == 0x0200)
            {
                hover(index);
            }
            else if (kind == 0x0201 || kind == 0x0204)
            {
                pendingButton = kind == 0x0201 ? 1 : 2;
                pendingAction = pendingButton == 1 ? index : -1;
                return new IntPtr(1);
            }
            else if ((kind == 0x0202 && pendingButton == 1) || (kind == 0x0205 && pendingButton == 2))
            {
                int selected = pendingAction >= 0 && pendingAction == index ? index : -1;
                pendingButton = 0;
                Finish(selected);
                return new IntPtr(1);
            }
            else if (Native.TriggerButton(kind, input.MouseData) != MouseButtonTrigger.None &&
                Native.TriggerButton(kind, input.MouseData) != trigger.MouseButton && Native.IsTriggerButtonDown(kind))
            {
                Finish(-1);
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }

        private IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || completed)
                return Native.CallNextHookEx(keyboardHook, code, message, data);
            Native.KeyboardHookData input = (Native.KeyboardHookData)Marshal.PtrToStructure(data, typeof(Native.KeyboardHookData));
            int key = (int)input.VirtualKey;
            int kind = message.ToInt32();
            bool down = kind == 0x0100 || kind == 0x0104;
            bool up = kind == 0x0101 || kind == 0x0105;
            if (IsTriggerEvent(trigger.Keyboard, key, Native.CurrentModifiers()))
                return Native.CallNextHookEx(keyboardHook, code, message, data);
            int index = key >= 0x31 && key <= 0x38 ? key - 0x31 :
                (key >= 0x61 && key <= 0x68 ? key - 0x61 : -1);
            if (index >= 0 || key == 0x1B)
            {
                if (down && pendingKey == 0)
                    pendingKey = key;
                if (up && pendingKey == key)
                    Finish(index);
                return new IntPtr(1);
            }
            if (down && !IsModifier(key))
                Finish(-1);
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }

        private static bool IsModifier(int key)
        {
            return key == 0x10 || key == 0x11 || key == 0x12 ||
                key == 0x5B || key == 0x5C || (key >= 0xA0 && key <= 0xA5);
        }

        internal static bool IsTriggerEvent(KeyboardShortcut trigger, int key, uint modifiers)
        {
            return trigger != null && key == trigger.Key && modifiers == trigger.Modifiers;
        }

        private void Finish(int index)
        {
            if (completed) return;
            completed = true;
            complete(index);
        }

        public void Dispose()
        {
            if (mouseHook != IntPtr.Zero && !Native.UnhookWindowsHookEx(mouseHook))
                AppLog.Error("Releasing the menu mouse listener", new Win32Exception(Marshal.GetLastWin32Error()));
            if (keyboardHook != IntPtr.Zero && !Native.UnhookWindowsHookEx(keyboardHook))
                AppLog.Error("Releasing the menu keyboard listener", new Win32Exception(Marshal.GetLastWin32Error()));
            mouseHook = keyboardHook = IntPtr.Zero;
            GC.KeepAlive(mouseCallback);
            GC.KeepAlive(keyboardCallback);
        }
    }

    internal static class ShortcutSender
    {
        internal static Native.Input[] BuildInputs(KeyboardShortcut shortcut)
        {
            List<KeyStroke> strokes = shortcut.GetKeyStrokes();
            Native.Input[] inputs = new Native.Input[strokes.Count];
            for (int i = 0; i < strokes.Count; i++)
            {
                inputs[i].Type = 1;
                inputs[i].Data.Keyboard.VirtualKey = strokes[i].Key;
                inputs[i].Data.Keyboard.Flags = (strokes[i].IsUp ? Native.KeyUp : 0) |
                    (strokes[i].IsExtended ? Native.ExtendedKey : 0);
            }
            return inputs;
        }

        internal static void Send(KeyboardShortcut shortcut)
        {
            Native.Input[] inputs = BuildInputs(shortcut);
            uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.Input)));
            if (sent == inputs.Length) return;
            int error = Marshal.GetLastWin32Error();
            List<Native.Input> releases = new List<Native.Input>();
            for (int i = (int)sent - 1; i >= 0; i--)
            {
                if ((inputs[i].Data.Keyboard.Flags & Native.KeyUp) == 0)
                {
                    Native.Input release = inputs[i];
                    release.Data.Keyboard.Flags |= Native.KeyUp;
                    releases.Add(release);
                }
            }
            string cleanup = "";
            if (releases.Count > 0 &&
                Native.SendInput((uint)releases.Count, releases.ToArray(), Marshal.SizeOf(typeof(Native.Input))) != releases.Count)
                cleanup = " Windows also blocked modifier cleanup; release Ctrl, Alt, Shift and Win manually.";
            throw new Win32Exception(error, "Windows accepted " + sent + " of " + inputs.Length +
                " shortcut events. Elevated applications and secure desktops can block input." + cleanup);
        }
    }
}
