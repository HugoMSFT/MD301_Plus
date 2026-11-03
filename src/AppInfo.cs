using System;
using System.Reflection;

namespace MD301Plus
{
    internal static class AppInfo
    {
        internal const string Name = "MD301 Plus";
        // NotifyIcon.Text throws for longer text on .NET Framework.
        internal const int MaxTrayTextLength = 63;
        internal static readonly string Version = ReadVersion();

        internal static string DisplayVersion { get { return FormatVersion(ShellLauncher.IsPackaged); } }

        // Partner Center and Installed apps show the Store package as this version plus ".0".
        internal static string FormatVersion(bool packaged)
        {
            return "Version " + Version + (packaged ? " (Microsoft Store)" : string.Empty);
        }

        internal static string TrayText(string status)
        {
            string text = Name + " " + Version + " - " + status;
            return text.Length <= MaxTrayTextLength ? text : text.Substring(0, MaxTrayTextLength - 3) + "...";
        }

        private static string ReadVersion()
        {
            AssemblyInformationalVersionAttribute version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                typeof(AppInfo).Assembly, typeof(AssemblyInformationalVersionAttribute));
            return version != null ? version.InformationalVersion : typeof(AppInfo).Assembly.GetName().Version.ToString(3);
        }
    }
}
