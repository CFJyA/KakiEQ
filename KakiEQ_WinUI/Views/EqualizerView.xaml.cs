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

        private Line? _zeroLine;
        private Polyline? _curvePolyline;
        private readonly List<Ellipse> _bandDots = new();
        private bool _isSubscribed;

        private readonly SolidColorBrush _zeroLineBrush = new(Windows.UI.Color.FromArgb(50, 255, 255, 255));
        private readonly SolidColorBrush _curveStrokeBrush = new(Windows.UI.Color.FromArgb(255, 187, 134, 252)); // #BB86FC
        private readonly SolidColorBrush _dotFillBrush = new(Windows.UI.Color.FromArgb(255, 3, 218, 198));       // #03DAC6

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
            };
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
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
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

        private void SlidersRackGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (BandsItemsControl?.ItemsPanelRoot is StackPanel panel && e.NewSize.Width > 300)
                {
                    double availForBands = e.NewSize.Width - 120; // Subtract Preamp + Separator
                    double extraSpace = availForBands - (10 * 82);
                    double newSpacing = Math.Round(Math.Clamp(extraSpace / 9.0, 2.0, 48.0));
                    if (Math.Abs(panel.Spacing - newSpacing) > 1.5)
                    {
                        panel.Spacing = newSpacing;
                    }
                }
            }
            catch { }
        }

        private void EnsureCanvasPrimitives(int bandCount)
        {
            if (CurveCanvas == null) return;

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
