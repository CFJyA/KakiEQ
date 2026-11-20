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
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class SettingsWindow : Window
    {
        private bool _isInitializing = true;
        private AppWindow? _appWindow;
        public bool AllowClose { get; set; } = false;

        public SettingsWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarBorder);

            ConfigureWindowSize();
        }

        public void ShowWindow()
        {
            LoadSettingsData();
            _appWindow?.Show();
            Activate();
        }

        private void ConfigureWindowSize()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow != null)
            {
                _appWindow.Resize(new SizeInt32(780, 680));
                _appWindow.Closing += (s, args) =>
                {
                    if (!AllowClose)
                    {
                        args.Cancel = true;
                        _appWindow.Hide();
                    }
                };

                // Set window icon
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Kaki_Oficial_Blanco.ico");
                if (File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }

                var titleBar = _appWindow.TitleBar;
                if (titleBar != null)
                {
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                }
            }
        }

        private void LoadSettingsData()
        {
            _isInitializing = true;
            var vm = MainViewModel.Instance;
            var settings = vm?.SettingsService.LoadSettings() ?? new Services.Data.AppSettings();

            MusicFolderPathText.Text = string.IsNullOrEmpty(settings.MusicFolderPath)
                ? "(No se ha configurado ninguna carpeta aún)"
                : settings.MusicFolderPath;

            DarkModeToggle.IsOn = settings.IsDarkMode;
            CrossfadeToggle.IsOn = settings.CrossfadeEnabled;
            CrossfadeSlider.Value = Math.Clamp(settings.CrossfadeDurationSeconds, 1, 10);
            CrossfadeValueLabel.Text = $"{(int)CrossfadeSlider.Value} s";

            CrossfadeSliderContainer.Opacity = settings.CrossfadeEnabled ? 1.0 : 0.45;
            CrossfadeSliderContainer.IsHitTestVisible = settings.CrossfadeEnabled;

            StemMorphCrossfadeToggle.IsOn = settings.StemMorphCrossfadeEnabled;
            VolumeNormalizationToggle.IsOn = settings.VolumeNormalizationEnabled;
            SpectrumAnalyzerToggle.IsOn = settings.SpectrumAnalyzerEnabled;
            WasapiExclusiveToggle.IsOn = settings.WasapiExclusiveEnabled;
            AudioTechInfoToggle.IsOn = settings.ShowAudioTechnicalInfo;

            // Set current theme to window
            if (Content is FrameworkElement fe)
            {
                fe.RequestedTheme = settings.IsDarkMode ? ElementTheme.Dark : ElementTheme.Light;
            }

            _isInitializing = false;

            // Query registry off the UI thread so window activation never stalls
            _ = Task.Run(() =>
            {
                bool startupEnabled = StartupHelper.IsStartupEnabled();
                DispatcherQueue.TryEnqueue(() =>
                {
                    _isInitializing = true;
                    StartupToggle.IsOn = startupEnabled;
                    _isInitializing = false;
                });
            });
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
                MusicFolderPathText.Text = folder.Path;

                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    await vm.ScanLibraryFolderAsync(folder.Path);
                }
            }
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            var vm = MainViewModel.Instance;
            var settings = vm?.SettingsService.LoadSettings();
            if (settings != null && !string.IsNullOrEmpty(settings.MusicFolderPath) && Directory.Exists(settings.MusicFolderPath))
            {
                if (vm != null)
                {
                    await vm.ScanLibraryFolderAsync(settings.MusicFolderPath);
                }
            }
        }

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool isDark = DarkModeToggle.IsOn;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.IsLightTheme = !isDark;

                var settings = vm.SettingsService.LoadSettings();
                settings.IsDarkMode = isDark;
                vm.SettingsService.SaveSettings(settings);
            }

            // Update this window's theme
            if (Content is FrameworkElement fe)
            {
                fe.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            }

            // Update main window theme
            if (MainWindow.Instance?.Content is FrameworkElement mainFe)
            {
                mainFe.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            }
        }

        private void CrossfadeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enabled = CrossfadeToggle.IsOn;
            CrossfadeSliderContainer.Opacity = enabled ? 1.0 : 0.45;
            CrossfadeSliderContainer.IsHitTestVisible = enabled;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.AudioEngine.CrossfadeEnabled = enabled;

                var settings = vm.SettingsService.LoadSettings();
                settings.CrossfadeEnabled = enabled;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void CrossfadeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            int val = (int)Math.Round(e.NewValue);
            CrossfadeValueLabel.Text = $"{val} s";

            if (_isInitializing) return;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.AudioEngine.CrossfadeDurationSeconds = val;

                var settings = vm.SettingsService.LoadSettings();
                settings.CrossfadeDurationSeconds = val;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void VolumeNormalizationToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enabled = VolumeNormalizationToggle.IsOn;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.AudioEngine.VolumeNormalizationEnabled = enabled;

                var settings = vm.SettingsService.LoadSettings();
                settings.VolumeNormalizationEnabled = enabled;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enable = StartupToggle.IsOn;
            StartupHelper.SetStartup(enable);

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                var settings = vm.SettingsService.LoadSettings();
                settings.StartWithWindows = enable;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void AudioTechInfoToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool isEnabled = AudioTechInfoToggle.IsOn;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.ShowAudioTechnicalInfo = isEnabled;

                var settings = vm.SettingsService.LoadSettings();
                settings.ShowAudioTechnicalInfo = isEnabled;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void StemMorphCrossfadeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enabled = StemMorphCrossfadeToggle.IsOn;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.AudioEngine.StemMorphCrossfadeEnabled = enabled;

                var settings = vm.SettingsService.LoadSettings();
                settings.StemMorphCrossfadeEnabled = enabled;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void SpectrumAnalyzerToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enabled = SpectrumAnalyzerToggle.IsOn;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.IsSpectrumAnalyzerEnabled = enabled;

                var settings = vm.SettingsService.LoadSettings();
                settings.SpectrumAnalyzerEnabled = enabled;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void WasapiExclusiveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enabled = WasapiExclusiveToggle.IsOn;

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                vm.AudioEngine.WasapiExclusiveEnabled = enabled;

                var settings = vm.SettingsService.LoadSettings();
                settings.WasapiExclusiveEnabled = enabled;
                vm.SettingsService.SaveSettings(settings);
            }
        }

        private void SettingsRootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            if (w <= 0) return;

            bool isNarrow = w < 560;
            if (SettingsScrollViewer != null)
            {
                SettingsScrollViewer.Padding = isNarrow
                    ? new Thickness(14, 14, 14, 24)
                    : new Thickness(28, 20, 28, 32);
            }

            if (CrossfadeSliderContainer != null)
            {
                CrossfadeSliderContainer.Margin = isNarrow
                    ? new Thickness(0, 4, 0, 0)
                    : new Thickness(56, 0, 0, 0);
            }

            if (PickFolderButton != null)
            {
                if (isNarrow)
                {
                    Grid.SetRow(PickFolderButton, 1);
                    Grid.SetColumn(PickFolderButton, 0);
                    Grid.SetColumnSpan(PickFolderButton, 3);
                    PickFolderButton.Margin = new Thickness(0, 10, 0, 0);
                    PickFolderButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else
                {
                    Grid.SetRow(PickFolderButton, 0);
                    Grid.SetColumn(PickFolderButton, 2);
                    Grid.SetColumnSpan(PickFolderButton, 1);
                    PickFolderButton.Margin = new Thickness(0);
                    PickFolderButton.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }
        }
    }
}
