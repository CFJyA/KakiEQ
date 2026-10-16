using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace KakiEQ_WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[KakiEQ UnhandledException]: {e.Message}\n{e.Exception}");
                e.Handled = true;
            };
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
                        string ext = System.IO.Path.GetExtension(candidate).ToLowerInvariant();
                        if (ext is ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".wma" or ".opus")
                        {
                            audioFilePath = candidate;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(audioFilePath))
            {
                _window = new Views.MiniPlayerWindow(audioFilePath);
            }
            else
            {
                _window = new MainWindow();
            }

            _window.Activate();
        }
    }
}
