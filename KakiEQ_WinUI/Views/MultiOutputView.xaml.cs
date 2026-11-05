using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class MultiOutputView : Page
    {
        public MultiOutputViewModel? ViewModel => (DataContext as MultiOutputViewModel) ?? MainViewModel.Instance?.MultiOutputVm;

        public MultiOutputView()
        {
            InitializeComponent();
            if (MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.MultiOutputVm;
            }
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MultiOutputViewModel vm && DataContext != vm)
            {
                DataContext = vm;
                Bindings?.Update();
            }
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void DeviceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                ViewModel.SetDeviceEnabled(device, toggle.IsOn);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider && slider.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                ViewModel.UpdateDeviceVolume(device, (float)(e.NewValue / 100.0));
            }
        }

        private void LatencySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider && slider.DataContext is AudioOutputDevice device && ViewModel != null)
            {
                ViewModel.UpdateDeviceLatency(device, (int)Math.Round(e.NewValue));
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
