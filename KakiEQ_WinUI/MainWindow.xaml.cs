using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        public MainViewModel ViewModel => MainViewModel.Instance ?? new MainViewModel();

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            // Setup Custom TitleBar with Native Windows Caption Buttons
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Configure Window Size and Position
            var appWindow = GetAppWindow();
            if (appWindow != null)
            {
                appWindow.Resize(new SizeInt32(1150, 760));

                // Customize titlebar buttons colors to match dark Material design
                var titleBar = appWindow.TitleBar;
                if (titleBar != null)
                {
                    titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(128, 255, 255, 255);
                }
            }

            // Check if initial music folder needs to be selected
            _ = CheckInitialMusicFolderAsync();
        }

        private AppWindow? GetAppWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        private async Task CheckInitialMusicFolderAsync()
        {
            await Task.Delay(500); // Give UI time to render

            var settings = ViewModel.SettingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.MusicFolderPath) || !Directory.Exists(settings.MusicFolderPath))
            {
                // Try default Windows Music folder first
                string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (Directory.Exists(myMusic) && Directory.GetFiles(myMusic, "*.*", SearchOption.AllDirectories).Length > 0)
                {
                    await ViewModel.ScanLibraryFolderAsync(myMusic);
                }
            }
            else
            {
                // Scan saved music folder in background
                _ = ViewModel.ScanLibraryFolderAsync(settings.MusicFolderPath);
            }
        }

        private async void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                await ViewModel.ScanLibraryFolderAsync(folder.Path);
            }
        }

        private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider && Math.Abs(e.NewValue - ViewModel.CurrentPositionSeconds) > 1.8)
            {
                ViewModel.SeekTo(e.NewValue);
            }
        }

        private void QueueItem_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Track track)
            {
                _ = ViewModel.PlayTrackAsync(track);
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            if (Content is FrameworkElement fe)
            {
                fe.RequestedTheme = fe.RequestedTheme == ElementTheme.Light 
                    ? ElementTheme.Dark 
                    : ElementTheme.Light;
            }
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = "Kaki EQ v2.0 - WinUI 3 Edition",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 16,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Reproductor de audio de alta fidelidad con ecualización gráfica paramétrica DSP de 10 bandas y estética Material Design 3 (Itztli / Yolpaki).",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 230, 230))
            });
            stack.Children.Add(new TextBlock
            {
                Text = "• Motor de audio: ManagedBass (BASS DirectX)\n• Plataforma: Windows App SDK / WinUI 3 / .NET 8\n• Frecuencias: 32 Hz a 16 kHz (-15 dB a +15 dB)\n• Diseñado para Windows 11 con Mica Backdrop",
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180))
            });

            var aboutDialog = new ContentDialog
            {
                Title = "Acerca de Kaki EQ",
                Content = stack,
                CloseButtonText = "Aceptar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            await aboutDialog.ShowAsync();
        }
    }
}

