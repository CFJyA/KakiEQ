using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Helpers
{
    public static class PlaylistDialogHelper
    {
        /// <summary>
        /// Shows the complete dialog to create a playlist (Image picker, Name, Description).
        /// Optionally adds the specified track or multiple tracks to the playlist right after creation.
        /// </summary>
        public static async Task<Playlist?> ShowCreatePlaylistDialogAsync(
            XamlRoot xamlRoot,
            Track? initialTrackToAdd = null,
            IEnumerable<Track>? initialTracksToAdd = null,
            string? collectionName = null)
        {
            if (xamlRoot == null) return null;

            int realCount = MainViewModel.Instance?.GlobalPlaylists?.Count(p => !p.IsCreateCard) ?? 0;
            string defaultName = $"Playlist {realCount + 1}";
            string? selectedCoverPath = null;

            // --- UI Elements ---
            var rootPanel = new StackPanel { Spacing = 16, Width = 380 };

            // 1. Cover Image Picker Section
            var coverSection = new Border
            {
                Background = GetThemeBrush(xamlRoot, "AppSurfaceContainer"),
                BorderBrush = GetThemeBrush(xamlRoot, "AppBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14)
            };

            var coverGrid = new Grid();
            coverGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            coverGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Image Preview box
            var previewBorder = new Border
            {
                Width = 90,
                Height = 90,
                CornerRadius = new CornerRadius(12),
                Background = GetThemeBrush(xamlRoot, "AppAccentSurface"),
                BorderBrush = GetThemeBrush(xamlRoot, "AppBorder"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 14, 0)
            };

            var previewGrid = new Grid();
            var placeholderIcon = new FontIcon
            {
                Glyph = "\uE8B9", // Photo icon
                FontSize = 32,
                Foreground = GetThemeBrush(xamlRoot, "AppAccent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var previewImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                Visibility = Visibility.Collapsed
            };
            previewGrid.Children.Add(placeholderIcon);
            previewGrid.Children.Add(previewImage);
            previewBorder.Child = previewGrid;

            Grid.SetColumn(previewBorder, 0);
            coverGrid.Children.Add(previewBorder);

            // Controls on the right of the image preview
            var coverControls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
            coverControls.Children.Add(new TextBlock
            {
                Text = "Portada de la playlist",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                Foreground = GetThemeBrush(xamlRoot, "AppTextPrimary")
            });
            coverControls.Children.Add(new TextBlock
            {
                Text = "JPG, PNG o WEBP (opcional)",
                FontSize = 11,
                Foreground = GetThemeBrush(xamlRoot, "AppTextMuted")
            });

            var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var chooseBtn = new Button
            {
                Background = GetThemeBrush(xamlRoot, "AppAccentContainer"),
                Foreground = GetThemeBrush(xamlRoot, "AppAccentText"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            var chooseBtnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chooseBtnContent.Children.Add(new FontIcon { Glyph = "\uEB9F", FontSize = 12 });
            chooseBtnContent.Children.Add(new TextBlock { Text = "Elegir Imagen...", VerticalAlignment = VerticalAlignment.Center });
            chooseBtn.Content = chooseBtnContent;

            var removeBtn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = GetThemeBrush(xamlRoot, "AppDanger"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Visibility = Visibility.Collapsed
            };
            ToolTipService.SetToolTip(removeBtn, "Quitar imagen seleccionada");
            removeBtn.Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 };

            chooseBtn.Click += async (s, e) =>
            {
                try
                {
                    var picker = new FileOpenPicker();
                    picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                    picker.ViewMode = PickerViewMode.Thumbnail;
                    picker.FileTypeFilter.Add(".jpg");
                    picker.FileTypeFilter.Add(".jpeg");
                    picker.FileTypeFilter.Add(".png");
                    picker.FileTypeFilter.Add(".webp");
                    picker.FileTypeFilter.Add(".bmp");
                    picker.FileTypeFilter.Add(".jxl");

                    if (MainWindow.Instance != null)
                    {
                        IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                        InitializeWithWindow.Initialize(picker, hwnd);
                    }

                    var file = await picker.PickSingleFileAsync();
                    if (file != null)
                    {
                        string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
                        Directory.CreateDirectory(cacheFolder);
                        string ext = Path.GetExtension(file.Path);
                        string destPath = Path.Combine(cacheFolder, $"pl_{Guid.NewGuid():N}{ext}");
                        File.Copy(file.Path, destPath, true);

                        selectedCoverPath = destPath;
                        previewImage.Source = ImageHelper.LoadBitmapSync(destPath);
                        previewImage.Visibility = Visibility.Visible;
                        placeholderIcon.Visibility = Visibility.Collapsed;
                        removeBtn.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error picking playlist cover: " + ex.Message);
                }
            };

            removeBtn.Click += (s, e) =>
            {
                selectedCoverPath = null;
                previewImage.Source = null;
                previewImage.Visibility = Visibility.Collapsed;
                placeholderIcon.Visibility = Visibility.Visible;
                removeBtn.Visibility = Visibility.Collapsed;
            };

            buttonsRow.Children.Add(chooseBtn);
            buttonsRow.Children.Add(removeBtn);
            coverControls.Children.Add(buttonsRow);

            Grid.SetColumn(coverControls, 1);
            coverGrid.Children.Add(coverControls);
            coverSection.Child = coverGrid;
            rootPanel.Children.Add(coverSection);

            // 2. Name Input
            var nameBox = new TextBox
            {
                Header = "Nombre de la Playlist *",
                PlaceholderText = "Ej. Mis Favoritas, Rock en Español...",
                Text = defaultName,
                MaxLength = 80,
                CornerRadius = new CornerRadius(10)
            };
            rootPanel.Children.Add(nameBox);

            // 3. Description Input
            var descBox = new TextBox
            {
                Header = "Descripción",
                PlaceholderText = "Añade una descripción opcional...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 72,
                MaxLength = 300,
                CornerRadius = new CornerRadius(10)
            };
            rootPanel.Children.Add(descBox);

            // 4. Initial Track / Tracks To Add indicator (if applicable)
            var multipleTracksList = initialTracksToAdd?.ToList();
            if (initialTrackToAdd != null)
            {
                var trackBorder = new Border
                {
                    Background = GetThemeBrush(xamlRoot, "AppAccentSurface"),
                    BorderBrush = GetThemeBrush(xamlRoot, "AppBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                var trackRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                trackRow.Children.Add(new FontIcon
                {
                    Glyph = "\uEC4F",
                    FontSize = 14,
                    Foreground = GetThemeBrush(xamlRoot, "AppAccent"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                trackRow.Children.Add(new TextBlock
                {
                    Text = $"Se añadirá: {initialTrackToAdd.Title} - {initialTrackToAdd.Artist}",
                    FontSize = 12,
                    Foreground = GetThemeBrush(xamlRoot, "AppAccentText"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                trackBorder.Child = trackRow;
                rootPanel.Children.Add(trackBorder);
            }
            else if (multipleTracksList != null && multipleTracksList.Any())
            {
                var trackBorder = new Border
                {
                    Background = GetThemeBrush(xamlRoot, "AppAccentSurface"),
                    BorderBrush = GetThemeBrush(xamlRoot, "AppBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                var trackRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                trackRow.Children.Add(new FontIcon
                {
                    Glyph = "\uE8F1",
                    FontSize = 14,
                    Foreground = GetThemeBrush(xamlRoot, "AppAccent"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                trackRow.Children.Add(new TextBlock
                {
                    Text = $"Se añadirán {multipleTracksList.Count} canciones de \"{collectionName ?? "álbum"}\"",
                    FontSize = 12,
                    Foreground = GetThemeBrush(xamlRoot, "AppAccentText"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                trackBorder.Child = trackRow;
                rootPanel.Children.Add(trackBorder);
            }

            // --- ContentDialog Setup ---
            var dialog = new ContentDialog
            {
                Title = "Nueva Playlist",
                Content = rootPanel,
                PrimaryButtonText = "Crear",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default
            };

            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    string chosenName = string.IsNullOrWhiteSpace(nameBox.Text) ? defaultName : nameBox.Text.Trim();
                    string chosenDesc = descBox.Text?.Trim() ?? string.Empty;

                    var newPl = new Playlist
                    {
                        Name = chosenName,
                        Description = chosenDesc,
                        CoverImagePath = selectedCoverPath,
                        TrackCount = 0
                    };

                    var dbService = MainViewModel.Instance?.DbService;
                    if (dbService != null)
                    {
                        await dbService.AddPlaylistAsync(newPl);

                        if (initialTrackToAdd != null)
                        {
                            await dbService.AddTrackToPlaylistAsync(newPl.Name, initialTrackToAdd);
                        }
                        else if (multipleTracksList != null && multipleTracksList.Any())
                        {
                            await dbService.AddTracksToPlaylistAsync(newPl.Name, multipleTracksList);
                        }

                        if (MainViewModel.Instance != null)
                        {
                            await MainViewModel.Instance.LoadGlobalPlaylistsAsync();

                            if (MainViewModel.Instance.CurrentView is PlaylistsViewModel pvm)
                            {
                                await pvm.LoadPlaylistsAsync();
                            }
                        }

                        string feedback;
                        if (initialTrackToAdd != null)
                        {
                            feedback = $"Playlist '{newPl.Name}' creada con \"{initialTrackToAdd.Title}\"";
                        }
                        else if (multipleTracksList != null && multipleTracksList.Any())
                        {
                            feedback = $"Playlist '{newPl.Name}' creada con {multipleTracksList.Count} canciones de \"{collectionName ?? "álbum"}\"";
                        }
                        else
                        {
                            feedback = $"Playlist '{newPl.Name}' creada con éxito";
                        }

                        MainWindow.Instance?.ShowToast(feedback);
                    }

                    return newPl;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Dialog error: " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Shows the complete dialog to edit an existing playlist (Image, Name, Description).
        /// </summary>
        public static async Task<bool> ShowEditPlaylistDialogAsync(XamlRoot xamlRoot, Playlist playlist)
        {
            if (xamlRoot == null || playlist == null) return false;

            string? selectedCoverPath = playlist.CoverImagePath;
            bool imageChanged = false;

            var rootPanel = new StackPanel { Spacing = 16, Width = 380 };

            // 1. Cover Image Section
            var coverSection = new Border
            {
                Background = GetThemeBrush(xamlRoot, "AppSurfaceContainer"),
                BorderBrush = GetThemeBrush(xamlRoot, "AppBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14)
            };

            var coverGrid = new Grid();
            coverGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            coverGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var previewBorder = new Border
            {
                Width = 90,
                Height = 90,
                CornerRadius = new CornerRadius(12),
                Background = GetThemeBrush(xamlRoot, "AppAccentSurface"),
                BorderBrush = GetThemeBrush(xamlRoot, "AppBorder"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 14, 0)
            };

            var previewGrid = new Grid();
            var placeholderIcon = new FontIcon
            {
                Glyph = "\uE8B9",
                FontSize = 32,
                Foreground = GetThemeBrush(xamlRoot, "AppAccent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = string.IsNullOrEmpty(selectedCoverPath) ? Visibility.Visible : Visibility.Collapsed
            };

            var previewImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                Visibility = !string.IsNullOrEmpty(selectedCoverPath) ? Visibility.Visible : Visibility.Collapsed
            };

            if (!string.IsNullOrEmpty(selectedCoverPath) && File.Exists(selectedCoverPath))
            {
                try
                {
                    previewImage.Source = ImageHelper.LoadBitmapSync(selectedCoverPath);
                }
                catch { }
            }

            previewGrid.Children.Add(placeholderIcon);
            previewGrid.Children.Add(previewImage);
            previewBorder.Child = previewGrid;

            Grid.SetColumn(previewBorder, 0);
            coverGrid.Children.Add(previewBorder);

            var coverControls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
            coverControls.Children.Add(new TextBlock
            {
                Text = "Portada de la playlist",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                Foreground = GetThemeBrush(xamlRoot, "AppTextPrimary")
            });
            coverControls.Children.Add(new TextBlock
            {
                Text = "Cambiar o quitar imagen",
                FontSize = 11,
                Foreground = GetThemeBrush(xamlRoot, "AppTextMuted")
            });

            var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var chooseBtn = new Button
            {
                Background = GetThemeBrush(xamlRoot, "AppAccentContainer"),
                Foreground = GetThemeBrush(xamlRoot, "AppAccentText"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            var chooseBtnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chooseBtnContent.Children.Add(new FontIcon { Glyph = "\uEB9F", FontSize = 12 });
            chooseBtnContent.Children.Add(new TextBlock { Text = "Cambiar Imagen...", VerticalAlignment = VerticalAlignment.Center });
            chooseBtn.Content = chooseBtnContent;

            var removeBtn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = GetThemeBrush(xamlRoot, "AppDanger"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Visibility = !string.IsNullOrEmpty(selectedCoverPath) ? Visibility.Visible : Visibility.Collapsed
            };
            ToolTipService.SetToolTip(removeBtn, "Quitar imagen");
            removeBtn.Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 };

            chooseBtn.Click += async (s, e) =>
            {
                try
                {
                    var picker = new FileOpenPicker();
                    picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                    picker.ViewMode = PickerViewMode.Thumbnail;
                    picker.FileTypeFilter.Add(".jpg");
                    picker.FileTypeFilter.Add(".jpeg");
                    picker.FileTypeFilter.Add(".png");
                    picker.FileTypeFilter.Add(".webp");
                    picker.FileTypeFilter.Add(".bmp");
                    picker.FileTypeFilter.Add(".jxl");

                    if (MainWindow.Instance != null)
                    {
                        IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                        InitializeWithWindow.Initialize(picker, hwnd);
                    }

                    var file = await picker.PickSingleFileAsync();
                    if (file != null)
                    {
                        string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
                        Directory.CreateDirectory(cacheFolder);
                        string ext = Path.GetExtension(file.Path);
                        string destPath = Path.Combine(cacheFolder, $"pl_{Guid.NewGuid():N}{ext}");
                        File.Copy(file.Path, destPath, true);

                        selectedCoverPath = destPath;
                        imageChanged = true;
                        previewImage.Source = ImageHelper.LoadBitmapSync(destPath);
                        previewImage.Visibility = Visibility.Visible;
                        placeholderIcon.Visibility = Visibility.Collapsed;
                        removeBtn.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error picking playlist cover: " + ex.Message);
                }
            };

            removeBtn.Click += (s, e) =>
            {
                selectedCoverPath = null;
                imageChanged = true;
                previewImage.Source = null;
                previewImage.Visibility = Visibility.Collapsed;
                placeholderIcon.Visibility = Visibility.Visible;
                removeBtn.Visibility = Visibility.Collapsed;
            };

            buttonsRow.Children.Add(chooseBtn);
            buttonsRow.Children.Add(removeBtn);
            coverControls.Children.Add(buttonsRow);

            Grid.SetColumn(coverControls, 1);
            coverGrid.Children.Add(coverControls);
            coverSection.Child = coverGrid;
            rootPanel.Children.Add(coverSection);

            // 2. Name
            var nameBox = new TextBox
            {
                Header = "Nombre de la Playlist *",
                Text = playlist.Name,
                MaxLength = 80,
                CornerRadius = new CornerRadius(10)
            };
            rootPanel.Children.Add(nameBox);

            // 3. Description
            var descBox = new TextBox
            {
                Header = "Descripción",
                PlaceholderText = "Añade una descripción...",
                Text = playlist.Description ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 72,
                MaxLength = 300,
                CornerRadius = new CornerRadius(10)
            };
            rootPanel.Children.Add(descBox);

            var dialog = new ContentDialog
            {
                Title = "Editar Playlist",
                Content = rootPanel,
                PrimaryButtonText = "Guardar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default
            };

            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    string oldName = playlist.Name;
                    string newName = string.IsNullOrWhiteSpace(nameBox.Text) ? playlist.Name : nameBox.Text.Trim();
                    string newDesc = descBox.Text?.Trim() ?? string.Empty;

                    playlist.Name = newName;
                    playlist.Description = newDesc;
                    if (imageChanged)
                    {
                        playlist.CoverImagePath = selectedCoverPath;
                    }

                    var dbService = MainViewModel.Instance?.DbService;
                    if (dbService != null)
                    {
                        await dbService.UpdatePlaylistAsync(playlist);

                        if (MainViewModel.Instance != null)
                        {
                            await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
                            if (MainViewModel.Instance.CurrentView is PlaylistsViewModel pvm)
                            {
                                await pvm.LoadPlaylistsAsync();
                            }
                        }

                        MainWindow.Instance?.ShowToast($"Playlist '{playlist.Name}' actualizada");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Dialog error: " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Creates a MenuFlyoutSubItem ("Añadir a Playlist...") to be nested inside any track context menu.
        /// </summary>
        public static MenuFlyoutSubItem CreateAddToPlaylistSubItem(Track track, XamlRoot xamlRoot)
        {
            var subItem = new MenuFlyoutSubItem
            {
                Text = "Añadir a Playlist...",
                Icon = new FontIcon { Glyph = "\uE93C" }
            };

            PopulatePlaylistItems(subItem.Items, track, xamlRoot);
            return subItem;
        }

        /// <summary>
        /// Shows a standalone MenuFlyout targeting an anchor element for adding a track to playlists.
        /// </summary>
        public static void ShowAddToPlaylistFlyout(FrameworkElement anchor, Track track, XamlRoot xamlRoot)
        {
            if (anchor == null || track == null) return;

            var flyout = new MenuFlyout();

            // Header title
            var headerItem = new MenuFlyoutItem
            {
                Text = $"Añadir \"{track.Title}\" a:",
                IsEnabled = false
            };
            flyout.Items.Add(headerItem);
            flyout.Items.Add(new MenuFlyoutSeparator());

            PopulatePlaylistItems(flyout.Items, track, xamlRoot);

            flyout.ShowAt(anchor);
        }

        private static void PopulatePlaylistItems(IList<MenuFlyoutItemBase> items, Track track, XamlRoot xamlRoot)
        {
            // 1. "+ Nueva Playlist..." item
            var createItem = new MenuFlyoutItem
            {
                Text = "Nueva Playlist...",
                Icon = new FontIcon { Glyph = "\uE710" }
            };
            createItem.Click += async (s, e) =>
            {
                await ShowCreatePlaylistDialogAsync(xamlRoot, track);
            };
            items.Add(createItem);

            items.Add(new MenuFlyoutSeparator());

            // 2. Existing playlists
            var globalPlaylists = MainViewModel.Instance?.GlobalPlaylists?
                .Where(p => !p.IsCreateCard)
                .ToList();

            if (globalPlaylists != null && globalPlaylists.Any())
            {
                foreach (var pl in globalPlaylists)
                {
                    var plItem = new MenuFlyoutItem
                    {
                        Text = pl.Name,
                        Icon = new FontIcon { Glyph = "\uE8F1" }
                    };
                    plItem.Click += async (s, e) =>
                    {
                        if (MainViewModel.Instance?.DbService != null)
                        {
                            await MainViewModel.Instance.DbService.AddTrackToPlaylistAsync(pl.Name, track);
                            await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
                            if (MainViewModel.Instance.CurrentView is PlaylistsViewModel pvm)
                            {
                                await pvm.LoadPlaylistsAsync();
                            }
                            MainWindow.Instance?.ShowToast($"\"{track.Title}\" añadida a {pl.Name}");
                        }
                    };
                    items.Add(plItem);
                }
            }
            else
            {
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "(No hay playlists creadas aún)",
                    IsEnabled = false
                };
                items.Add(emptyItem);
            }
        }

        /// <summary>
        /// Shows a standalone MenuFlyout targeting an anchor element for adding multiple tracks (e.g. from an album) to playlists.
        /// </summary>
        public static void ShowAddTracksToPlaylistFlyout(FrameworkElement anchor, IEnumerable<Track> tracks, string collectionTitle, XamlRoot xamlRoot)
        {
            if (anchor == null || tracks == null) return;
            var trackList = tracks.ToList();
            if (!trackList.Any()) return;

            var flyout = new MenuFlyout();

            var headerItem = new MenuFlyoutItem
            {
                Text = $"Añadir álbum \"{collectionTitle}\" ({trackList.Count} canciones) a:",
                IsEnabled = false
            };
            flyout.Items.Add(headerItem);
            flyout.Items.Add(new MenuFlyoutSeparator());

            PopulateMultipleTracksPlaylistItems(flyout.Items, trackList, collectionTitle, xamlRoot);

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Creates a MenuFlyoutSubItem ("Añadir a Playlist...") for a collection of tracks.
        /// </summary>
        public static MenuFlyoutSubItem CreateAddTracksToPlaylistSubItem(IEnumerable<Track> tracks, string collectionTitle, XamlRoot xamlRoot)
        {
            var subItem = new MenuFlyoutSubItem
            {
                Text = "Añadir álbum a Playlist...",
                Icon = new FontIcon { Glyph = "\uE8F1" }
            };

            var trackList = tracks.ToList();
            PopulateMultipleTracksPlaylistItems(subItem.Items, trackList, collectionTitle, xamlRoot);
            return subItem;
        }

        private static void PopulateMultipleTracksPlaylistItems(IList<MenuFlyoutItemBase> items, List<Track> tracks, string collectionTitle, XamlRoot xamlRoot)
        {
            // 1. "+ Nueva Playlist..."
            var createItem = new MenuFlyoutItem
            {
                Text = "Nueva Playlist...",
                Icon = new FontIcon { Glyph = "\uE710" }
            };
            createItem.Click += async (s, e) =>
            {
                await ShowCreatePlaylistDialogAsync(xamlRoot, null, tracks, collectionTitle);
            };
            items.Add(createItem);

            items.Add(new MenuFlyoutSeparator());

            // 2. Existing playlists
            var globalPlaylists = MainViewModel.Instance?.GlobalPlaylists?
                .Where(p => !p.IsCreateCard)
                .ToList();

            if (globalPlaylists != null && globalPlaylists.Any())
            {
                foreach (var pl in globalPlaylists)
                {
                    var plItem = new MenuFlyoutItem
                    {
                        Text = pl.Name,
                        Icon = new FontIcon { Glyph = "\uE8F1" }
                    };
                    plItem.Click += async (s, e) =>
                    {
                        if (MainViewModel.Instance?.DbService != null)
                        {
                            await MainViewModel.Instance.DbService.AddTracksToPlaylistAsync(pl.Name, tracks);
                            await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
                            if (MainViewModel.Instance.CurrentView is PlaylistsViewModel pvm)
                            {
                                await pvm.LoadPlaylistsAsync();
                            }
                            MainWindow.Instance?.ShowToast($"{tracks.Count} canciones de \"{collectionTitle}\" añadidas a {pl.Name}");
                        }
                    };
                    items.Add(plItem);
                }
            }
            else
            {
                var emptyItem = new MenuFlyoutItem
                {
                    Text = "(No hay playlists creadas aún)",
                    IsEnabled = false
                };
                items.Add(emptyItem);
            }
        }

        private static SolidColorBrush GetThemeBrush(XamlRoot xamlRoot, string key)
        {
            var theme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Dark;
            var dictionaryKey = theme == ElementTheme.Light ? "Light" : "Dark";
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(dictionaryKey, out var dictionary) &&
                dictionary is ResourceDictionary themeResources &&
                themeResources.TryGetValue(key, out var resource) &&
                resource is SolidColorBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
}

