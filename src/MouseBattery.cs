using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace MD301Plus
{
    internal enum BatteryRead { Ok, Asleep, Missing }

    internal delegate BatteryRead BatterySource(out int percent);

    // Reads the battery level through the ASUS vendor HID collection that Armoury Crate and Gear Link use.
    // Only the read-only "get battery" query (0x0712) is sent, and only to collections that name the MD301.
    internal static class MouseBatteryReader
    {
        internal const int ReportLength = 65;
        private const ushort AsusVendorId = 0x0B05;
        private const ushort VendorUsagePage = 0xFF01;
        private const int TimeoutMs = 1000;
        private static string cachedPath;

        internal static byte[] BuildRequest()
        {
            byte[] request = new byte[ReportLength];
            request[1] = 0x12;
            request[2] = 0x07;
            return request;
        }

        internal static bool IsBatteryEcho(byte[] report)
        {
            return report != null && report.Length >= 7 && report[1] == 0x12 && report[2] == 0x07;
        }

        // ASUS answers 0xAAFF when the receiver cannot reach a sleeping or switched-off mouse.
        internal static bool IsUnavailable(byte[] report)
        {
            return report != null && report.Length >= 3 && report[1] == 0xFF && report[2] == 0xAA;
        }

        internal static int ParseResponse(byte[] report)
        {
            if (!IsBatteryEcho(report)) return MouseBattery.Unknown;
            int percent = report[5];
            if (percent > 100) return MouseBattery.Unknown;
            for (int i = 5; i < report.Length; i++)
                if (report[i] != 0) return percent;
            return MouseBattery.Unknown;
        }

        internal static BatteryRead Read(out int percent)
        {
            percent = MouseBattery.Unknown;
            string path = cachedPath;
            if (path != null)
            {
                BatteryRead cached = Query(path, out percent);
                if (cached != BatteryRead.Missing) return cached;
                cachedPath = null;
            }
            BatteryRead result = BatteryRead.Missing;
            foreach (string candidate in FindCollections())
            {
                int value;
                BatteryRead read = Query(candidate, out value);
                if (read == BatteryRead.Missing) continue;
                cachedPath = candidate;
                result = read;
                percent = value;
                if (read == BatteryRead.Ok) break;
            }
            return result;
        }

        internal static List<string> FindCollections()
        {
            List<string> result = new List<string>();
            Guid hidClass;
            HidD_GetHidGuid(out hidClass);
            IntPtr set = SetupDiGetClassDevs(ref hidClass, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) return result;
            try
            {
                for (int index = 0; ; index++)
                {
                    DeviceInterfaceData item = new DeviceInterfaceData(Marshal.SizeOf(typeof(DeviceInterfaceData)));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidClass, index, ref item)) break;
                    string path = InterfacePath(set, ref item);
                    if (path != null && path.IndexOf("0b05", StringComparison.OrdinalIgnoreCase) >= 0 && IsMd301Protocol(path))
                        result.Add(path);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        private static string InterfacePath(IntPtr set, ref DeviceInterfaceData item)
        {
            int required;
            SetupDiGetDeviceInterfaceDetail(set, ref item, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required <= 4) return null;
            IntPtr detail = Marshal.AllocHGlobal(required);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit.
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref item, detail, required, out required, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
            }
            finally { Marshal.FreeHGlobal(detail); }
        }

        private static bool IsMd301Protocol(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return false;
                HidAttributes attributes = new HidAttributes(Marshal.SizeOf(typeof(HidAttributes)));
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != AsusVendorId) return false;
                IntPtr parsed;
                if (!HidD_GetPreparsedData(handle, out parsed)) return false;
                HidCaps caps = new HidCaps();
                try
                {
                    if (HidP_GetCaps(parsed, ref caps) != 0x00110000) return false;
                }
                finally { HidD_FreePreparsedData(parsed); }
                if (caps.UsagePage != VendorUsagePage || caps.Usage != 1 ||
                    caps.InputReportByteLength != ReportLength || caps.OutputReportByteLength != ReportLength) return false;
                byte[] name = new byte[256];
                if (!HidD_GetProductString(handle, name, name.Length)) return false;
                string product = System.Text.Encoding.Unicode.GetString(name).Split('\0')[0];
                return product.IndexOf("MD301", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static BatteryRead Query(string path, out int percent)
        {
            percent = MouseBattery.Unknown;
            using (SafeFileHandle handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return BatteryRead.Missing;
                // The vendor collection only answers on a synchronous handle, so a watchdog cancels blocked I/O.
                using (System.Threading.Timer watchdog = new System.Threading.Timer(delegate
                {
                    try { CancelIoEx(handle, IntPtr.Zero); }
                    catch (ObjectDisposedException) { }
                }, null, Timeout.Infinite, Timeout.Infinite))
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    byte[] request = BuildRequest();
                    if (!Transfer(true, handle, request, watchdog, clock)) return BatteryRead.Asleep;
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        byte[] report = new byte[ReportLength];
                        if (!Transfer(false, handle, report, watchdog, clock)) return BatteryRead.Asleep;
                        if (IsUnavailable(report)) return BatteryRead.Asleep;
                        if (!IsBatteryEcho(report)) continue;
                        percent = ParseResponse(report);
                        return percent == MouseBattery.Unknown ? BatteryRead.Asleep : BatteryRead.Ok;
                    }
                    return BatteryRead.Asleep;
                }
            }
        }

        private static bool Transfer(bool write, SafeFileHandle handle, byte[] buffer,
            System.Threading.Timer watchdog, Stopwatch clock)
        {
            long remaining = TimeoutMs - clock.ElapsedMilliseconds;
            if (remaining <= 0) return false;
            watchdog.Change(remaining, Timeout.Infinite);
            try
            {
                int count;
                bool ok = write ? WriteFile(handle, buffer, buffer.Length, out count, IntPtr.Zero) :
                    ReadFile(handle, buffer, buffer.Length, out count, IntPtr.Zero);
                return ok && count == buffer.Length;
            }
            finally { watchdog.Change(Timeout.Infinite, Timeout.Infinite); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClass;
            public int Flags;
            public IntPtr Reserved;
            public DeviceInterfaceData(int size) { Size = size; InterfaceClass = Guid.Empty; Flags = 0; Reserved = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort Version;
            public HidAttributes(int size) { Size = size; VendorId = 0; ProductId = 0; Version = 0; }
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct HidCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidClass);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HidAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr parsed);

        [DllImport("hid.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_FreePreparsedData(IntPtr parsed);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr parsed, ref HidCaps caps);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, int length);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid classGuid, int index,
            ref DeviceInterfaceData item);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData item,
            IntPtr detail, int size, out int required, IntPtr device);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int length, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int length, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    }

    // Keeps the last known battery level and refreshes it in the background so the ring never waits on the device.
    internal sealed class MouseBattery : IDisposable
    {
        internal const int Unknown = -1;
        private const int PollMs = 5 * 60 * 1000;
        private const int RefreshThrottleMs = 5000;
        private readonly BatterySource source;
        private readonly SynchronizationContext context;
        private readonly System.Threading.Timer timer;
        private volatile int percent = Unknown;
        private volatile bool disposed;
        private int busy;
        private int lastRefresh;
        private bool loggedFailure;
        internal event Action Changed;

        internal MouseBattery() : this(MouseBatteryReader.Read, true)
        {
        }

        internal MouseBattery(BatterySource source, bool poll)
        {
            this.source = source;
            context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            lastRefresh = Environment.TickCount - RefreshThrottleMs;
            timer = new System.Threading.Timer(delegate { Poll(); }, null,
                poll ? 0 : Timeout.Infinite, poll ? PollMs : Timeout.Infinite);
        }

        internal int Percent { get { return percent; } }

        internal void Refresh()
        {
            if (disposed || unchecked(Environment.TickCount - lastRefresh) < RefreshThrottleMs) return;
            lastRefresh = Environment.TickCount;
            ThreadPool.QueueUserWorkItem(delegate { Poll(); });
        }

        internal void Poll()
        {
            if (disposed || Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                int value;
                BatteryRead result;
                // This runs on a thread-pool thread: an escaped exception would end the whole app.
                try { result = source(out value); }
                catch (Exception error)
                {
                    if (!loggedFailure) AppLog.Error("Battery read failed", error);
                    loggedFailure = true;
                    result = BatteryRead.Missing;
                    value = Unknown;
                }
                // A sleeping mouse keeps its last reading; an unplugged receiver clears it.
                int next = result == BatteryRead.Ok ? value : result == BatteryRead.Missing ? Unknown : percent;
                if (next == percent) return;
                percent = next;
                if (disposed) return;
                try { context.Post(delegate { RaiseChanged(); }, null); }
                catch (InvalidOperationException) { }
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (!disposed && handler != null) handler();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Dispose();
        }
    }
}
