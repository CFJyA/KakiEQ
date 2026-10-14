using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class EqualizerView : UserControl
    {
        public EqualizerViewModel? ViewModel => DataContext as EqualizerViewModel;

        public EqualizerView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (ViewModel != null)
                {
                    ViewModel.BandsChanged += (s2, e2) => RedrawCurve();
                    RedrawCurve();
                }
            };
            Loaded += (s, e) => RedrawCurve();
        }

        private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawCurve();
        }

        private void RedrawCurve()
        {
            if (CurveCanvas == null || CurveCanvas.ActualWidth <= 0 || CurveCanvas.ActualHeight <= 0) return;
            if (ViewModel == null || ViewModel.Bands.Count == 0) return;

            CurveCanvas.Children.Clear();

            double width = CurveCanvas.ActualWidth;
            double height = CurveCanvas.ActualHeight;
            double paddingX = 24;
            double paddingY = 8;
            double usableWidth = width - (2 * paddingX);
            double usableHeight = height - (2 * paddingY);

            // Draw center 0 dB reference line
            double zeroY = paddingY + (usableHeight / 2.0);
            var zeroLine = new Line
            {
                X1 = paddingX,
                Y1 = zeroY,
                X2 = width - paddingX,
                Y2 = zeroY,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };
            CurveCanvas.Children.Add(zeroLine);

            // Calculate points for the 10 bands
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252)), // #BB86FC
                StrokeThickness = 2.5
            };

            int count = ViewModel.Bands.Count;
            for (int i = 0; i < count; i++)
            {
                var band = ViewModel.Bands[i];
                double x = paddingX + ((double)i / (count - 1)) * usableWidth;

                // gain is -15 to +15.
                // -15 -> Y is bottom (height - paddingY)
                // +15 -> Y is top (paddingY)
                double clampedGain = Math.Clamp(band.Gain, -15f, 15f);
                double normalized = (clampedGain + 15.0) / 30.0; // 0 to 1
                double y = paddingY + ((1.0 - normalized) * usableHeight);

                polyline.Points.Add(new Point(x, y));

                // Add small glowing dot at each frequency point
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 3, 218, 198)) // Cyan #03DAC6
                };
                Canvas.SetLeft(dot, x - 3);
                Canvas.SetTop(dot, y - 3);
                CurveCanvas.Children.Add(dot);
            }

            CurveCanvas.Children.Add(polyline);
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
                ViewModel.Bands[0].Gain = 6f; // 32Hz
                ViewModel.Bands[1].Gain = 5f; // 64Hz
                ViewModel.Bands[2].Gain = 3f; // 125Hz
                RedrawCurve();
            }
        }

        private void EnhanceVocal_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            if (ViewModel.Bands.Count >= 8)
            {
                ViewModel.Bands[5].Gain = 4f; // 1kHz
                ViewModel.Bands[6].Gain = 4.5f; // 2kHz
                ViewModel.Bands[7].Gain = 3f; // 4kHz
                RedrawCurve();
            }
        }

        private void EnhanceTreble_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            if (ViewModel.Bands.Count >= 10)
            {
                ViewModel.Bands[8].Gain = 5f; // 8kHz
                ViewModel.Bands[9].Gain = 5.5f; // 16kHz
                RedrawCurve();
            }
        }
    }
}
