using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Helpers
{
    public class MainViewTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? HomeTemplate { get; set; }
        public DataTemplate? LibraryTemplate { get; set; }
        public DataTemplate? ArtistsTemplate { get; set; }
        public DataTemplate? ArtistDetailTemplate { get; set; }
        public DataTemplate? AlbumsTemplate { get; set; }
        public DataTemplate? AlbumDetailTemplate { get; set; }
        public DataTemplate? PlaylistsTemplate { get; set; }
        public DataTemplate? PlaylistDetailTemplate { get; set; }
        public DataTemplate? EqualizerTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            return item switch
            {
                HomeViewModel => HomeTemplate,
                LibraryViewModel => LibraryTemplate,
                ArtistsViewModel => ArtistsTemplate,
                ArtistDetailViewModel => ArtistDetailTemplate,
                AlbumsViewModel => AlbumsTemplate,
                AlbumDetailViewModel => AlbumDetailTemplate,
                PlaylistsViewModel => PlaylistsTemplate,
                PlaylistDetailViewModel => PlaylistDetailTemplate,
                EqualizerViewModel => EqualizerTemplate,
                _ => base.SelectTemplateCore(item)
            };
        }

        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item) ?? base.SelectTemplateCore(item, container);
        }
    }
}

