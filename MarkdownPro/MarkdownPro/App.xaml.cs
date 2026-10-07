using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace MarkdownPro
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private static Mutex? _singleInstanceMutex;
        private CancellationTokenSource? _pipeServerCts;

        private const string SingleInstanceMutexName = @"Local\MarkdownPro_SingleInstance_Mutex_v1";
        private const string SingleInstancePipeName = "MarkdownPro_SingleInstance_Pipe_v1";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private static readonly string CrashLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownPro",
            "crash.log");

        public static void LogCrash(string context, Exception? ex)
        {
            try
            {
                string dir = Path.GetDirectoryName(CrashLogPath)!;
                Directory.CreateDirectory(dir);
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex}\r\n--------------------------------------------------\r\n";
                File.AppendAllText(CrashLogPath, entry);
            }
            catch
            {
            }
        }

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            UnhandledException += (_, e) =>
            {
                LogCrash($"WinUI.UnhandledException ({e.Message})", e.Exception);
                e.Handled = true;
            };

            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                var initialFiles = new List<string>();

                // Gather files from command-line arguments (Explorer Open With / double-click)
                foreach (var cmdFile in ExtractCommandLineFilePaths())
                {
                    if (!initialFiles.Contains(cmdFile, StringComparer.OrdinalIgnoreCase))
                    {
                        initialFiles.Add(cmdFile);
                    }
                }

                // Also gather any files from Windows AppInstance activation
                try
                {
                    var currentInstance = AppInstance.GetCurrent();
                    var activatedArgs = currentInstance.GetActivatedEventArgs();
                    foreach (var actFile in ExtractFilePaths(activatedArgs))
                    {
                        if (!initialFiles.Contains(actFile, StringComparer.OrdinalIgnoreCase))
                        {
                            initialFiles.Add(actFile);
                        }
                    }
                }
                catch
                {
                }

                // Check if a primary instance of Markdown Pro is already running
                bool createdNew;
                _singleInstanceMutex = new Mutex( initiallyOwned: true, SingleInstanceMutexName, out createdNew);

                if (!createdNew)
                {
                    // Another instance is already open -> forward the file paths over the Named Pipe and exit immediately
                    ForwardFilesToRunningInstance(initialFiles);
                    Environment.Exit(0);
                    return;
                }

                // We are the primary instance -> start the Named Pipe listener to receive files from future instances
                _pipeServerCts = new CancellationTokenSource();
                _ = RunSingleInstancePipeServerAsync(_pipeServerCts.Token);

                _window = new MainWindow(initialFiles);
                _window.Closed += (_, _) =>
                {
                    _pipeServerCts?.Cancel();
                    try
                    {
                        _singleInstanceMutex?.ReleaseMutex();
                        _singleInstanceMutex?.Dispose();
                    }
                    catch
                    {
                    }
                };
                _window.Activate();
            }
            catch (Exception ex)
            {
                LogCrash("OnLaunched", ex);
            }
        }

        private static void ForwardFilesToRunningInstance(List<string> files)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out);
                client.Connect(timeout: 2500);

                // Send one path per line (or "__ACTIVATE__" if launched with no file arguments)
                string payload = files.Count > 0
                    ? string.Join("\n", files)
                    : "__ACTIVATE__";

                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
            }
            catch (Exception ex)
            {
                LogCrash("ForwardFilesToRunningInstance", ex);
            }
        }

        private async Task RunSingleInstancePipeServerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        SingleInstancePipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string payload = await reader.ReadToEndAsync(ct);

                    var receivedFiles = new List<string>();
                    if (!string.IsNullOrWhiteSpace(payload) && payload.Trim() != "__ACTIVATE__")
                    {
                        foreach (var line in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string candidate = line.Trim('"', ' ');
                            if (IsValidMarkdownOrTextFilePath(candidate))
                            {
                                receivedFiles.Add(Path.GetFullPath(candidate));
                            }
                        }
                    }

                    if (_window != null)
                    {
                        _window.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (receivedFiles.Count > 0)
                            {
                                _window.OpenFilesFromExternalActivation(receivedFiles);
                            }

                            BringMainWindowToFront();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogCrash("SingleInstancePipeServer", ex);
                    await Task.Delay(250, ct);
                }
            }
        }

        private void BringMainWindowToFront()
        {
            if (_window == null)
            {
                return;
            }

            try
            {
                _window.Activate();
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                if (hwnd != IntPtr.Zero)
                {
                    if (IsIconic(hwnd))
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                    }
                    else
                    {
                        ShowWindow(hwnd, SW_SHOW);
                    }
                    SetForegroundWindow(hwnd);
                }
            }
            catch
            {
            }
        }

        private static bool IsValidMarkdownOrTextFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            string ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return false;
            }

            // Never open the executable itself or binary libraries as a Markdown document
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".pri", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".xbf", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".mdown", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".mkd", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
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
                        if (item is IStorageFile storageFile &&
                            !string.IsNullOrWhiteSpace(storageFile.Path) &&
                            IsValidMarkdownOrTextFilePath(storageFile.Path))
                        {
                            result.Add(storageFile.Path);
                        }
                    }
                }
                else if (activatedArgs.Kind == ExtendedActivationKind.Launch &&
                         activatedArgs.Data is ILaunchActivatedEventArgs launchArgs &&
                         !string.IsNullOrWhiteSpace(launchArgs.Arguments))
                {
                    foreach (var path in ParseArgumentsString(launchArgs.Arguments))
                    {
                        if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(path);
                        }
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
                    if (!string.IsNullOrWhiteSpace(arg) &&
                        !arg.StartsWith('-') &&
                        IsValidMarkdownOrTextFilePath(arg))
                    {
                        string fullPath = Path.GetFullPath(arg);
                        if (!result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(fullPath);
                        }
                    }
                }
            }
            catch
            {
                // Ignore command line parse issues
            }

            return result;
        }

        private static List<string> ParseArgumentsString(string rawArguments)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(rawArguments))
            {
                return result;
            }

            string trimmed = rawArguments.Trim('"', ' ');
            if (IsValidMarkdownOrTextFilePath(trimmed))
            {
                result.Add(Path.GetFullPath(trimmed));
            }

            return result;
        }
    }
}

