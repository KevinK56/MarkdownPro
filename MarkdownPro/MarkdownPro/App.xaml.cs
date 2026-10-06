using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace MarkdownPro
{
    public partial class App : Application
    {
        private MainWindow? _window;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public App()
        {
            InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var currentInstance = AppInstance.GetCurrent();
            var activatedArgs = currentInstance.GetActivatedEventArgs();

            var mainInstance = AppInstance.FindOrRegisterForKey("MarkdownPro.PrimaryInstance");
            if (!mainInstance.IsCurrent)
            {
                await mainInstance.RedirectActivationToAsync(activatedArgs);
                Environment.Exit(0);
                return;
            }

            mainInstance.Activated += OnAppInstanceActivated;

            var initialFiles = ExtractFilePaths(activatedArgs);
            foreach (var cmdFile in ExtractCommandLineFilePaths())
            {
                if (!initialFiles.Contains(cmdFile, StringComparer.OrdinalIgnoreCase))
                {
                    initialFiles.Add(cmdFile);
                }
            }

            _window = new MainWindow(initialFiles);
            _window.Activate();
        }

        private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
        {
            var files = ExtractFilePaths(e);
            if (_window == null)
            {
                return;
            }

            _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (files.Count > 0)
                {
                    _window.OpenFilesFromExternalActivation(files);
                }

                _window.Activate();
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
            });
        }

        private static List<string> ExtractFilePaths(AppActivationArguments? activatedArgs)
        {
            var result = new List<string>();
            if (activatedArgs == null)
            {
                return result;
            }

            try
            {
                if (activatedArgs.Kind == ExtendedActivationKind.File &&
                    activatedArgs.Data is IFileActivatedEventArgs fileArgs)
                {
                    foreach (var item in fileArgs.Files)
                    {
                        if (item is IStorageFile storageFile && !string.IsNullOrWhiteSpace(storageFile.Path))
                        {
                            result.Add(storageFile.Path);
                        }
                    }
                }
                else if (activatedArgs.Kind == ExtendedActivationKind.Launch &&
                         activatedArgs.Data is ILaunchActivatedEventArgs launchArgs &&
                         !string.IsNullOrWhiteSpace(launchArgs.Arguments))
                {
                    string candidate = launchArgs.Arguments.Trim('"', ' ');
                    if (File.Exists(candidate))
                    {
                        result.Add(Path.GetFullPath(candidate));
                    }
                }
            }
            catch
            {
                // Ignore activation extraction errors
            }

            return result;
        }

        private static List<string> ExtractCommandLineFilePaths()
        {
            var result = new List<string>();
            try
            {
                string[] cmdArgs = Environment.GetCommandLineArgs();
                for (int i = 1; i < cmdArgs.Length; i++)
                {
                    string arg = cmdArgs[i].Trim('"', ' ');
                    if (!string.IsNullOrWhiteSpace(arg) && !arg.StartsWith('-') && File.Exists(arg))
                    {
                        result.Add(Path.GetFullPath(arg));
                    }
                }
            }
            catch
            {
                // Ignore command line parse issues
            }

            return result;
        }
    }
}

