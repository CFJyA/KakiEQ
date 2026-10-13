using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class EqualizerView : UserControl
    {
        public EqualizerViewModel? ViewModel => DataContext as EqualizerViewModel;

        public EqualizerView()
        {
            InitializeComponent();
        }
    }
}

