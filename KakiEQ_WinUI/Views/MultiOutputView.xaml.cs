using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class MultiOutputView : UserControl
    {
        public MultiOutputViewModel? ViewModel => DataContext as MultiOutputViewModel;

        public MultiOutputView()
        {
            InitializeComponent();
        }

        private void DeviceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                if (device.IsEnabled != toggle.IsOn)
                {
                    ViewModel.SetDeviceEnabled(device, toggle.IsOn);
                }
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider && slider.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                ViewModel.UpdateDeviceVolume(device, (float)(e.NewValue / 100.0));
            }
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                ViewModel.SetDeviceMuted(device, !device.IsMuted);
            }
        }

        private void SystemRoutingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && ViewModel != null)
            {
                if (ViewModel.IsSystemRoutingActive != toggle.IsOn)
                {
                    ViewModel.IsSystemRoutingActive = toggle.IsOn;
                }
            }
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                ViewModel.TestDeviceCommand.Execute(device);
            }
        }
    }
}
