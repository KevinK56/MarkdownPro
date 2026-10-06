using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.System;

namespace MarkdownPro.Services
{
    public static class FileAssociationService
    {
        private const string ProgId = "MarkdownPro.Document";
        private static readonly string[] Extensions = { ".md", ".markdown", ".mdown", ".mkd" };

        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        public static bool IsRegisteredAsHandler()
        {
            try
            {
                // Check if UserChoice points to MarkdownPro or if our ProgId is registered and no conflicting UserChoice overrides it
                using var userChoiceKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice");
                if (userChoiceKey != null)
                {
                    string? currentProgId = userChoiceKey.GetValue("ProgId") as string;
                    if (!string.IsNullOrEmpty(currentProgId) &&
                        currentProgId.Contains("MarkdownPro", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                using var progIdKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
                if (progIdKey != null)
                {
                    string? cmd = progIdKey.GetValue("") as string;
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(cmd) && !string.IsNullOrEmpty(exePath) &&
                        cmd.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return userChoiceKey == null;
                    }
                }
            }
            catch
            {
                // Ignore registry read issues
            }

            return false;
        }

        public static async Task<bool> RegisterFileAssociationsAsync(bool openWindowsDefaultAppsSettings = true)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    // 1. Register ProgId under HKCU\Software\Classes\MarkdownPro.Document
                    using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                    {
                        progKey?.SetValue("", "Markdown Document (Markdown Pro)");
                        progKey?.SetValue("FriendlyTypeName", "Markdown Document");

                        using (var iconKey = progKey?.CreateSubKey("DefaultIcon"))
                        {
                            iconKey?.SetValue("", $"\"{exePath}\",0");
                        }

                        using (var cmdKey = progKey?.CreateSubKey(@"shell\open\command"))
                        {
                            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                        }
                    }

                    // 2. Register Application entry under HKCU\Software\Classes\Applications\MarkdownPro.exe
                    string exeName = System.IO.Path.GetFileName(exePath);
                    using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{exeName}"))
                    {
                        appKey?.SetValue("FriendlyAppName", "Markdown Pro");
                        using (var cmdKey = appKey?.CreateSubKey(@"shell\open\command"))
                        {
                            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                        }
                        using (var typesKey = appKey?.CreateSubKey("SupportedTypes"))
                        {
                            foreach (string ext in Extensions)
                            {
                                typesKey?.SetValue(ext, "");
                            }
                        }
                    }

                    // 3. Associate each Markdown extension with MarkdownPro.Document in HKCU\Software\Classes
                    foreach (string ext in Extensions)
                    {
                        using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
                        extKey?.SetValue("", ProgId);
                        extKey?.SetValue("Content Type", "text/markdown");
                        extKey?.SetValue("PerceivedType", "text");

                        using var openWithKey = extKey?.CreateSubKey("OpenWithProgids");
                        openWithKey?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
                    }

                    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                }

                if (openWindowsDefaultAppsSettings)
                {
                    await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

