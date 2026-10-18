using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using KakiEQ_WinUI.Views;

namespace KakiEQ_WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static DispatcherQueue? AppDispatcherQueue { get; private set; }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            UnhandledException += (s, e) =>
            {
                try
                {
                    string logPath = @"C:\Users\W11\source\repos\KakiEQ_WinUI\debug_crash.txt";
                    File.AppendAllText(logPath, $"[CRASH]: {e.Message}\nException: {e.Exception}\nStackTrace: {e.Exception?.StackTrace}\nInner: {e.Exception?.InnerException}\n");
                }
                catch { }
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string logPath = @"C:\Users\W11\source\repos\KakiEQ_WinUI\debug_crash.txt";
                    File.AppendAllText(logPath, $"[APPDOMAIN CRASH]: {e.ExceptionObject}\n");
                }
                catch { }
            };
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            string? audioFilePath = null;
            try
            {
                var cmdArgs = Environment.GetCommandLineArgs();
                for (int i = 1; i < cmdArgs.Length; i++)
                {
                    string candidate = cmdArgs[i].Trim('"', ' ');
                    if (File.Exists(candidate))
                    {
                        string ext = Path.GetExtension(candidate).ToLowerInvariant();
                        if (ext is ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".wma" or ".opus")
                        {
                            audioFilePath = candidate;
                            break;
                        }
                    }
                }
            }
            catch { }

            // If an audio file was passed, attempt to pass it to an already running instance
            if (!string.IsNullOrEmpty(audioFilePath))
            {
                if (TrySendToExistingInstance(audioFilePath))
                {
                    // Existing instance was notified, terminate this secondary process cleanly
                    Environment.Exit(0);
                    return;
                }
            }

            // Primary instance initialization
            AppDispatcherQueue = DispatcherQueue.GetForCurrentThread();
            StartPipeServer();

            if (!string.IsNullOrEmpty(audioFilePath))
            {
                _window = new MiniPlayerWindow(audioFilePath);
            }
            else
            {
                _window = new MainWindow();
            }

            _window.Activate();
        }

        private static bool TrySendToExistingInstance(string filePath)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "KakiEQ_Playback_Pipe", PipeDirection.Out);
                client.Connect(300); // 300ms connection timeout
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(filePath);
                try
                {
                    AllowSetForegroundWindow(ASFW_ANY);
                }
                catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void StartPipeServer()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            "KakiEQ_Playback_Pipe",
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync();

                        using var reader = new StreamReader(server);
                        string? filePath = await reader.ReadLineAsync();

                        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                        {
                            AppDispatcherQueue?.TryEnqueue(() =>
                            {
                                HandleIncomingAudioFile(filePath);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IPC] Server loop error: {ex.Message}");
                        await Task.Delay(300);
                    }
                }
            });
        }

        public static void HandleIncomingAudioFile(string filePath)
        {
            try
            {
                if (MiniPlayerWindow.CurrentInstance != null)
                {
                    MiniPlayerWindow.CurrentInstance.BringToForeground();
                    _ = MiniPlayerWindow.CurrentInstance.LoadAndPlayAudioFileAsync(filePath);
                }
                else
                {
                    var mini = new MiniPlayerWindow(filePath);
                    mini.Activate();
                    mini.BringToForeground();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HandleIncomingAudioFile] Error: {ex.Message}");
            }
        }
    }
}
