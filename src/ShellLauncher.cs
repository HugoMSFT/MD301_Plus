using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MD301Plus
{
    internal static class ShellLauncher
    {
        private const int ShowNormal = 1;
        private static readonly Lazy<bool> packaged = new Lazy<bool>(DetectPackage);

        internal static bool IsPackaged { get { return packaged.Value; } }

        internal static void Open(string target)
        {
            if (IsPackaged)
            {
                // Processes started by a Store (MSIX) app inherit its private AppData and registry redirection,
                // so ask Explorer to open the target exactly as if it was opened from Start.
                try
                {
                    OpenThroughDesktop(target, ShowNormal);
                    return;
                }
                catch (Exception ex)
                {
                    if (!IsDesktopLaunchFailure(ex)) throw;
                    AppLog.Error("Explorer could not open " + target + "; MD301 Plus opened it directly", ex);
                }
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

        internal static void OpenThroughDesktop(string target, int show)
        {
            object windows = null, desktop = null, browser = null, view = null, folderView = null, shell = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true));
                object location = 0; // CSIDL_DESKTOP
                object root = null;
                int window;
                desktop = ((IShellWindows)windows).FindWindowSW(ref location, ref root, 8, out window, 1); // SWC_DESKTOP, SWFO_NEEDDISPATCH
                if (desktop == null) throw new COMException("The Windows desktop is not running.");

                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
                Guid browserInterface = typeof(IShellBrowser).GUID;
                Marshal.ThrowExceptionForHR(((IComServiceProvider)desktop).QueryService(ref service, ref browserInterface, out browser));
                view = ((IShellBrowser)browser).QueryActiveShellView();
                Guid dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                folderView = ((IShellView)view).GetItemObject(0, ref dispatch); // SVGIO_BACKGROUND
                shell = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null);

                uint explorer;
                if (Native.GetWindowThreadProcessId(new IntPtr(window), out explorer) != 0)
                    Native.AllowSetForegroundWindow(explorer);
                shell.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, shell,
                    new object[] { target, string.Empty, string.Empty, string.Empty, show });
            }
            finally
            {
                foreach (object item in new[] { shell, folderView, view, browser, desktop, windows })
                    if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            }
        }

        private static bool IsDesktopLaunchFailure(Exception error)
        {
            return error is ExternalException || error is InvalidCastException || error is TargetInvocationException ||
                error is MissingMemberException || error is UnauthorizedAccessException || error is ArgumentException;
        }

        private static bool DetectPackage()
        {
            try
            {
                uint length = 0;
                return GetCurrentPackageFullName(ref length, null) == 122; // ERROR_INSUFFICIENT_BUFFER: has a package name
            }
            catch (EntryPointNotFoundException) { return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder name);

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            void Count(); void Item(); void NewEnum(); void Register();
            void RegisterPending(); void Revoke(); void OnNavigate(); void OnActivated();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(ref object location, ref object root, int windowClass, out int window, int options);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IComServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid service, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB();
            void RemoveMenusSB(); void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB();
            void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellView
        {
            void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless();
            void UIActivate(); void Refresh(); void CreateViewWindow(); void DestroyViewWindow();
            void GetCurrentInfo(); void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object GetItemObject(uint item, ref Guid interfaceId);
        }
    }
}
