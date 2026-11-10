using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class KronatlView : Page
    {
        public KronatlViewModel? ViewModel => (DataContext as KronatlViewModel) ?? MainViewModel.Instance?.KronatlVm;

        public KronatlView()
        {
            InitializeComponent();
            if (MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.KronatlVm;
            }
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is KronatlViewModel vm && DataContext != vm)
            {
                DataContext = vm;
                Bindings?.Update();
            }
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void OpenCodex_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is KronatlSeason season)
            {
                MainWindow.Instance?.OpenKronatlCodex(season);
            }
        }

        private void PlayTop30_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is KronatlSeason season && ViewModel != null)
            {
                ViewModel.PlaySeasonTop30Command.Execute(season);
            }
        }
    }
}
