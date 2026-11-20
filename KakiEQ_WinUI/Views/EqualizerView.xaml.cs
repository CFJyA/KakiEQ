using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class EqualizerView : Page
    {
        public EqualizerViewModel? ViewModel => (DataContext as EqualizerViewModel) ?? MainViewModel.Instance?.EqualizerVm;

        private const int SpectrumBandCount = 32;
        private readonly float[] _rawSpectrum = new float[SpectrumBandCount];
        private readonly float[] _smoothSpectrum = new float[SpectrumBandCount];
        private readonly List<Rectangle> _spectrumBars = new();
        private Polygon? _spectrumPolygon;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _spectrumTimer;

        private Line? _zeroLine;
        private Polyline? _curvePolyline;
        private readonly List<Ellipse> _bandDots = new();
        private bool _isSubscribed;

        private readonly SolidColorBrush _zeroLineBrush = new(Windows.UI.Color.FromArgb(50, 255, 255, 255));
        private readonly SolidColorBrush _curveStrokeBrush = new(Windows.UI.Color.FromArgb(255, 187, 134, 252)); // #BB86FC
        private readonly SolidColorBrush _dotFillBrush = new(Windows.UI.Color.FromArgb(255, 3, 218, 198));       // #03DAC6
        private readonly SolidColorBrush _spectrumBarBrush = new(Windows.UI.Color.FromArgb(72, 3, 218, 198));
        private readonly SolidColorBrush _spectrumPolyFillBrush = new(Windows.UI.Color.FromArgb(38, 187, 134, 252));
        private readonly SolidColorBrush _spectrumPolyStrokeBrush = new(Windows.UI.Color.FromArgb(95, 187, 134, 252));

        public EqualizerView()
        {
            InitializeComponent();
            if (MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.EqualizerVm;
            }
            DataContextChanged += (s, e) =>
            {
                SubscribeViewModel();
                RedrawCurve();
            };
            Loaded += (s, e) =>
            {
                if (DataContext == null && MainViewModel.Instance != null)
                {
                    DataContext = MainViewModel.Instance.EqualizerVm;
                }
                SubscribeViewModel();
                RedrawCurve();
                StartSpectrumTimer();
            };
            Unloaded += (s, e) => StopSpectrumTimer();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is EqualizerViewModel vm && DataContext != vm)
            {
                DataContext = vm;
            }
            else if (DataContext == null && MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.EqualizerVm;
            }
            SubscribeViewModel();
            RedrawCurve();
            StartSpectrumTimer();
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void StartSpectrumTimer()
        {
            if (_spectrumTimer == null && DispatcherQueue != null)
            {
                _spectrumTimer = DispatcherQueue.CreateTimer();
                _spectrumTimer.Interval = TimeSpan.FromMilliseconds(25);
                _spectrumTimer.Tick += (_, _) => UpdateSpectrumVisuals();
            }
            _spectrumTimer?.Start();
        }

        private void StopSpectrumTimer()
        {
            _spectrumTimer?.Stop();
        }

        private void SubscribeViewModel()
        {
            if (ViewModel != null && !_isSubscribed)
            {
                ViewModel.BandsChanged += (s, e) => RedrawCurve();
                _isSubscribed = true;
            }
        }

        private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawCurve();
        }

        private void EqHeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            if (w <= 0 || EqHeaderActionsPanel == null || EqDspTargetPanel == null) return;

            if (w < 680)
            {
                Grid.SetRow(EqHeaderActionsPanel, 1);
                Grid.SetColumn(EqHeaderActionsPanel, 0);
                Grid.SetColumnSpan(EqHeaderActionsPanel, 2);
                EqHeaderActionsPanel.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetRow(EqDspTargetPanel, 2);
            }
            else
            {
                Grid.SetRow(EqHeaderActionsPanel, 0);
                Grid.SetColumn(EqHeaderActionsPanel, 1);
                Grid.SetColumnSpan(EqHeaderActionsPanel, 1);
                EqHeaderActionsPanel.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(EqDspTargetPanel, 1);
            }
        }

        private void BandsItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (SlidersRackGrid != null && SlidersRackGrid.ActualWidth > 0)
            {
                ApplyResponsiveBandSizing(SlidersRackGrid.ActualWidth);
            }
        }

        private void SlidersRackGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveBandSizing(e.NewSize.Width);
        }

        private void ApplyResponsiveBandSizing(double rackWidth)
        {
            try
            {
                if (BandsItemsControl?.ItemsPanelRoot is StackPanel panel && rackWidth > 220)
                {
                    double availForBands = Math.Max(220, rackWidth - 88); // Subtract Preamp + Separator
                    double spacing = Math.Round(Math.Clamp((availForBands - 620.0) / 14.0, 2.0, 24.0));
                    double bandWidth = Math.Clamp(Math.Floor((availForBands - 9.0 * spacing) / 10.0), 42.0, 86.0);

                    if (Math.Abs(panel.Spacing - spacing) > 0.5)
                    {
                        panel.Spacing = spacing;
                    }

                    int count = BandsItemsControl.Items?.Count ?? 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (BandsItemsControl.ContainerFromIndex(i) is FrameworkElement cont)
                        {
                            cont.Width = bandWidth;
                        }
                    }
                }
            }
            catch { }
        }

        private void EnsureCanvasPrimitives(int bandCount)
        {
            if (CurveCanvas == null) return;

            if (_spectrumPolygon == null)
            {
                _spectrumPolygon = new Polygon
                {
                    Fill = _spectrumPolyFillBrush,
                    Stroke = _spectrumPolyStrokeBrush,
                    StrokeThickness = 1.1,
                    Visibility = Visibility.Collapsed
                };
                CurveCanvas.Children.Insert(0, _spectrumPolygon);
            }

            while (_spectrumBars.Count < SpectrumBandCount)
            {
                var bar = new Rectangle
                {
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = _spectrumBarBrush,
                    Width = 4,
                    Height = 2,
                    Visibility = Visibility.Collapsed
                };
                _spectrumBars.Add(bar);
                CurveCanvas.Children.Insert(0, bar);
            }

            if (_zeroLine == null)
            {
                _zeroLine = new Line
                {
                    Stroke = _zeroLineBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                CurveCanvas.Children.Add(_zeroLine);
            }

            if (_curvePolyline == null)
            {
                _curvePolyline = new Polyline
                {
                    Stroke = _curveStrokeBrush,
                    StrokeThickness = 2.5
                };
                CurveCanvas.Children.Add(_curvePolyline);
            }

            while (_bandDots.Count < bandCount)
            {
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = _dotFillBrush
                };
                _bandDots.Add(dot);
                CurveCanvas.Children.Add(dot);
            }
        }

        private void UpdateSpectrumVisuals()
        {
            if (CurveCanvas == null || CurveCanvas.ActualWidth <= 20 || CurveCanvas.ActualHeight <= 10) return;

            var mainVm = MainViewModel.Instance;
            bool enabled = mainVm?.IsSpectrumAnalyzerEnabled == true;
            bool isActiveView = mainVm?.CurrentView is EqualizerViewModel && !mainVm.IsNowPlayingExpanded;

            if (!enabled || !isActiveView)
            {
                if (_spectrumPolygon != null && _spectrumPolygon.Visibility != Visibility.Collapsed)
                {
                    _spectrumPolygon.Visibility = Visibility.Collapsed;
                    for (int i = 0; i < _spectrumBars.Count; i++)
                    {
                        _spectrumBars[i].Visibility = Visibility.Collapsed;
                    }
                }
                return;
            }

            EnsureCanvasPrimitives(ViewModel?.Bands.Count ?? 10);

            bool hasSignal = mainVm!.AudioEngine.GetSpectrumBands(_rawSpectrum);
            double width = CurveCanvas.ActualWidth;
            double height = CurveCanvas.ActualHeight;
            double paddingX = 24.0;
            double bottomY = height - 4.0;
            double maxBarHeight = Math.Max(10.0, height - 10.0);
            double usableWidth = Math.Max(40.0, width - (2.0 * paddingX));
            double slotW = usableWidth / SpectrumBandCount;
            double barW = Math.Max(3.0, slotW * 0.56);

            if (_spectrumPolygon != null)
            {
                _spectrumPolygon.Visibility = Visibility.Visible;
                _spectrumPolygon.Points.Clear();
                _spectrumPolygon.Points.Add(new Point(paddingX, bottomY));
            }

            for (int i = 0; i < SpectrumBandCount; i++)
            {
                float target = hasSignal ? _rawSpectrum[i] : 0f;
                float coeff = target > _smoothSpectrum[i] ? 0.52f : 0.16f;
                _smoothSpectrum[i] += (target - _smoothSpectrum[i]) * coeff;

                double barH = Math.Max(2.0, _smoothSpectrum[i] * maxBarHeight);
                double left = paddingX + i * slotW + (slotW - barW) * 0.5;
                double top = bottomY - barH;

                var bar = _spectrumBars[i];
                if (bar.Visibility != Visibility.Visible) bar.Visibility = Visibility.Visible;
                bar.Width = barW;
                bar.Height = barH;
                Canvas.SetLeft(bar, left);
                Canvas.SetTop(bar, top);

                _spectrumPolygon?.Points.Add(new Point(left + barW * 0.5, top));
            }

            _spectrumPolygon?.Points.Add(new Point(paddingX + usableWidth, bottomY));
        }

        private void RedrawCurve()
        {
            if (CurveCanvas == null || CurveCanvas.ActualWidth <= 0 || CurveCanvas.ActualHeight <= 0) return;
            if (ViewModel == null || ViewModel.Bands.Count == 0) return;

            int count = ViewModel.Bands.Count;
            EnsureCanvasPrimitives(count);

            double width = CurveCanvas.ActualWidth;
            double height = CurveCanvas.ActualHeight;
            double paddingX = 24;
            double paddingY = 8;
            double usableWidth = width - (2 * paddingX);
            double usableHeight = height - (2 * paddingY);

            double zeroY = paddingY + (usableHeight / 2.0);
            if (_zeroLine != null)
            {
                _zeroLine.X1 = paddingX;
                _zeroLine.Y1 = zeroY;
                _zeroLine.X2 = width - paddingX;
                _zeroLine.Y2 = zeroY;
            }

            if (_curvePolyline != null)
            {
                _curvePolyline.Points.Clear();
                for (int i = 0; i < count; i++)
                {
                    var band = ViewModel.Bands[i];
                    double x = paddingX + ((double)i / (count - 1)) * usableWidth;

                    double clampedGain = Math.Clamp(band.Gain, -15f, 15f);
                    double normalized = (clampedGain + 15.0) / 30.0;
                    double y = paddingY + ((1.0 - normalized) * usableHeight);

                    _curvePolyline.Points.Add(new Point(x, y));

                    var dot = _bandDots[i];
                    Canvas.SetLeft(dot, x - 3);
                    Canvas.SetTop(dot, y - 3);
                }
            }
        }

        private async void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (xamlRoot == null || ViewModel == null) return;

            int customCount = ViewModel.Presets.Count(p => p.IsCustom);
            var inputTextBox = new TextBox
            {
                Header = "Nombre del Perfil de Audio",
                PlaceholderText = "Ej. Mis Bajos, Noche, Gaming...",
                Text = $"Mi Perfil {customCount + 1}",
                CornerRadius = new CornerRadius(10),
                MaxLength = 50
            };

            var dialog = new ContentDialog
            {
                Title = "Guardar Perfil de Audio",
                Content = inputTextBox,
                PrimaryButtonText = "Guardar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default
            };

            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
                {
                    ViewModel.SaveCurrentAsPreset(inputTextBox.Text.Trim());
                }
            }
            catch { }
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is EqualizerPreset preset)
            {
                ViewModel?.DeletePreset(preset);
            }
        }

        private void EnhanceBass_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            if (ViewModel.Bands.Count >= 3)
            {
                ViewModel.Bands[0].Gain = 6f;
                ViewModel.Bands[1].Gain = 5f;
                ViewModel.Bands[2].Gain = 3f;
                RedrawCurve();
            }
        }

        private void EnhanceVocal_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            if (ViewModel.Bands.Count >= 8)
            {
                ViewModel.Bands[5].Gain = 4f;
                ViewModel.Bands[6].Gain = 4.5f;
                ViewModel.Bands[7].Gain = 3f;
                RedrawCurve();
            }
        }

        private void EnhanceTreble_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            if (ViewModel.Bands.Count >= 10)
            {
                ViewModel.Bands[8].Gain = 5f;
                ViewModel.Bands[9].Gain = 5.5f;
                RedrawCurve();
            }
        }
    }
}
