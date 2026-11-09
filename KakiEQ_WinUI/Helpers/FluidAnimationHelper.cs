using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace KakiEQ_WinUI.Helpers
{
    /// <summary>
    /// Provides 100% hit-test-safe XAML transitions, responsive full-width GridView column sizing,
    /// and GPU-accelerated Composition micro-interactions (hover elevation, border illumination, page entrance).
    /// </summary>
    public static class FluidAnimationHelper
    {
        // ====================================================================
        // 1. ATTACHED PROPERTY: IsFluidLayoutEnabled (Safe XAML Reposition Transitions)
        // ====================================================================
        public static readonly DependencyProperty IsFluidLayoutEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsFluidLayoutEnabled",
                typeof(bool),
                typeof(FluidAnimationHelper),
                new PropertyMetadata(false, OnIsFluidLayoutEnabledChanged));

        public static bool GetIsFluidLayoutEnabled(DependencyObject obj) =>
            (bool)obj.GetValue(IsFluidLayoutEnabledProperty);

        public static void SetIsFluidLayoutEnabled(DependencyObject obj, bool value) =>
            obj.SetValue(IsFluidLayoutEnabledProperty, value);

        private static void OnIsFluidLayoutEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && e.NewValue is bool enabled && enabled)
            {
                EnsureSafeRepositionTransition(element);
            }
        }

        // ====================================================================
        // 2. ATTACHED PROPERTY: EnableGridViewReflow (100% Full-Width Responsive Grid + Smooth Reflow)
        // ====================================================================
        public static readonly DependencyProperty EnableGridViewReflowProperty =
            DependencyProperty.RegisterAttached(
                "EnableGridViewReflow",
                typeof(bool),
                typeof(FluidAnimationHelper),
                new PropertyMetadata(false, OnEnableGridViewReflowChanged));

        public static bool GetEnableGridViewReflow(DependencyObject obj) =>
            (bool)obj.GetValue(EnableGridViewReflowProperty);

        public static void SetEnableGridViewReflow(DependencyObject obj, bool value) =>
            obj.SetValue(EnableGridViewReflowProperty, value);

        private static void OnEnableGridViewReflowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListViewBase listViewBase && e.NewValue is bool enabled && enabled)
            {
                EnsureListViewBaseTransitions(listViewBase);

                if (listViewBase is GridView gridView)
                {
                    gridView.Loaded -= GridView_ResponsiveSizeChanged;
                    gridView.Loaded += GridView_ResponsiveSizeChanged;
                    gridView.SizeChanged -= GridView_ResponsiveSizeChanged;
                    gridView.SizeChanged += GridView_ResponsiveSizeChanged;
                }
            }
        }

        private static void EnsureListViewBaseTransitions(ListViewBase listViewBase)
        {
            try
            {
                if (listViewBase.ItemContainerTransitions == null)
                {
                    listViewBase.ItemContainerTransitions = new TransitionCollection();
                }

                bool hasReposition = false;
                foreach (var t in listViewBase.ItemContainerTransitions)
                {
                    if (t is RepositionThemeTransition)
                    {
                        hasReposition = true;
                        break;
                    }
                }

                if (!hasReposition)
                {
                    listViewBase.ItemContainerTransitions.Add(new RepositionThemeTransition
                    {
                        IsStaggeringEnabled = false
                    });
                }
            }
            catch { }
        }

        private static void GridView_ResponsiveSizeChanged(object sender, RoutedEventArgs e)
        {
            if (sender is GridView gridView)
            {
                UpdateGridViewFullWidthItems(gridView);
            }
        }

        /// <summary>
        /// Dynamically adjusts ItemsWrapGrid.ItemWidth and ItemHeight so that cards always fill 100%
        /// of the horizontal space with zero wasted gap on the right.
        /// </summary>
        public static void UpdateGridViewFullWidthItems(GridView gridView)
        {
            try
            {
                if (gridView.ActualWidth <= 100) return;
                if (gridView.ItemsPanelRoot is not ItemsWrapGrid wrapGrid) return;

                // Do not override fixed small side-grids (like AlbumDetailView's 330px "More by Artist" grid or ArtistDetailView's 145x200 tiles)
                if (!double.IsNaN(gridView.Width) && gridView.Width <= 400) return;
                if (gridView.Name == "ArtistAlbumsGridView") return;

                double availWidth = gridView.ActualWidth - gridView.Padding.Left - gridView.Padding.Right - 2.0;
                if (availWidth <= 120) return;

                string name = gridView.Name ?? string.Empty;
                bool isHomeGrid = name is "HistoryGridView" or "SuggestedGridView" or "FavoriteGridView";

                int columns;
                if (isHomeGrid)
                {
                    // Home grids have 10 (or 20) items: use 10 columns on wide/maximized screens, 5 columns on normal screens
                    if (availWidth >= 1420)
                        columns = 10;
                    else
                        columns = 5;
                }
                else
                {
                    // Albums, Artists, Playlists: target ~172px per card, minimum 3 columns
                    columns = Math.Max(3, (int)Math.Round(availWidth / 172.0));
                }

                double exactItemWidth = Math.Floor(availWidth / columns);
                if (exactItemWidth < 120) exactItemWidth = 120;

                // Maintain aspect ratio (cover + text area below)
                double textHeight = name == "PlaylistsGridView" ? 60.0 : 52.0;
                double exactItemHeight = Math.Floor(exactItemWidth + textHeight);

                if (Math.Abs(wrapGrid.ItemWidth - exactItemWidth) >= 1.0 ||
                    Math.Abs(wrapGrid.ItemHeight - exactItemHeight) >= 1.0)
                {
                    wrapGrid.ItemWidth = exactItemWidth;
                    wrapGrid.ItemHeight = exactItemHeight;
                }
            }
            catch { }
        }

        // ====================================================================
        // 3. ATTACHED PROPERTY: HoverMode ("Card", "Button", "Icon", "Fab", "Nav")
        // ====================================================================
        public static readonly DependencyProperty HoverModeProperty =
            DependencyProperty.RegisterAttached(
                "HoverMode",
                typeof(string),
                typeof(FluidAnimationHelper),
                new PropertyMetadata(null, OnHoverModeChanged));

        public static string? GetHoverMode(DependencyObject obj) =>
            (string?)obj.GetValue(HoverModeProperty);

        public static void SetHoverMode(DependencyObject obj, string? value) =>
            obj.SetValue(HoverModeProperty, value);

        private static void OnHoverModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && e.NewValue is string mode && !string.IsNullOrEmpty(mode))
            {
                AttachHoverBehavior(element, mode);
            }
        }

        private static void EnsureSafeRepositionTransition(FrameworkElement element)
        {
            // Keep generic inner Grids/Borders free of RepositionThemeTransition so scrolling
            // and layout passes remain 120 FPS stutter-free; GridView.ItemContainerTransitions
            // handles card reflow animations when maximizing/restoring the window.
        }

        private static readonly SolidColorBrush HoverCardBorderBrush =
            new(Windows.UI.Color.FromArgb(220, 187, 134, 252)); // #BB86FC glow

        private static readonly SolidColorBrush HoverNavBackgroundBrush =
            new(Windows.UI.Color.FromArgb(32, 187, 134, 252));

        private static readonly SolidColorBrush HoverIconBackgroundBrush =
            new(Windows.UI.Color.FromArgb(36, 187, 134, 252));

        private static readonly SolidColorBrush TransparentBrush =
            new(Microsoft.UI.Colors.Transparent);

        private static readonly DependencyProperty OriginalStrokeProperty =
            DependencyProperty.RegisterAttached("OriginalStroke", typeof(Brush), typeof(FluidAnimationHelper), new PropertyMetadata(null));

        private static readonly DependencyProperty OriginalStrokeThicknessProperty =
            DependencyProperty.RegisterAttached("OriginalStrokeThickness", typeof(double), typeof(FluidAnimationHelper), new PropertyMetadata(1.2));

        private static readonly DependencyProperty IsCurrentlyHoveredProperty =
            DependencyProperty.RegisterAttached("IsCurrentlyHovered", typeof(bool), typeof(FluidAnimationHelper), new PropertyMetadata(false));

        private static readonly DependencyProperty IsTranslationPreparedProperty =
            DependencyProperty.RegisterAttached("IsTranslationPrepared", typeof(bool), typeof(FluidAnimationHelper), new PropertyMetadata(false));

        private static readonly DependencyProperty CachedBorderRectProperty =
            DependencyProperty.RegisterAttached("CachedBorderRect", typeof(Rectangle), typeof(FluidAnimationHelper), new PropertyMetadata(null));

        private static void AttachHoverBehavior(FrameworkElement element, string mode)
        {
            element.PointerEntered += (s, e) =>
            {
                element.SetValue(IsCurrentlyHoveredProperty, true);

                if (!(bool)element.GetValue(IsTranslationPreparedProperty))
                {
                    ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                    element.SetValue(IsTranslationPreparedProperty, true);
                }

                if (mode == "Card")
                {
                    AnimateTranslation(element, 0f, -2.5f, 140);
                    HighlightUniformBorder(element, true);
                }
                else if (mode == "Fab")
                {
                    AnimateTranslation(element, 0f, -1.5f, 120);
                }
                else if (mode == "Icon")
                {
                    HighlightUniformBorder(element, true);
                    if (element is Button btn && (btn.Background == null || (btn.Background is SolidColorBrush scb && scb.Color.A == 0)))
                    {
                        btn.Background = HoverIconBackgroundBrush;
                    }
                }
                else if (mode == "Nav")
                {
                    AnimateTranslation(element, 3f, 0f, 130);
                    if (element is Button navBtn && navBtn.Background is SolidColorBrush bgBrush && bgBrush.Color.A == 0)
                    {
                        navBtn.Background = HoverNavBackgroundBrush;
                    }
                }
                else // "Button"
                {
                    AnimateTranslation(element, 0f, -1f, 120);
                    HighlightUniformBorder(element, true);
                }
            };

            void ResetHoverState()
            {
                if (!(bool)element.GetValue(IsCurrentlyHoveredProperty))
                    return;

                element.SetValue(IsCurrentlyHoveredProperty, false);
                AnimateTranslation(element, 0f, 0f, 140);
                HighlightUniformBorder(element, false);

                if (mode == "Icon" && element is Button btn && ReferenceEquals(btn.Background, HoverIconBackgroundBrush))
                {
                    btn.Background = TransparentBrush;
                }
                else if (mode == "Nav" && element is Button navBtn && ReferenceEquals(navBtn.Background, HoverNavBackgroundBrush))
                {
                    navBtn.Background = TransparentBrush;
                }
            }

            element.PointerExited += (s, e) => ResetHoverState();
            element.PointerCanceled += (s, e) => ResetHoverState();
            element.PointerCaptureLost += (s, e) => ResetHoverState();
            element.Unloaded += (s, e) => ResetHoverState();
        }

        private static void HighlightUniformBorder(FrameworkElement root, bool isHovered)
        {
            try
            {
                var rect = root.GetValue(CachedBorderRectProperty) as Rectangle;
                if (rect == null)
                {
                    rect = FindUniformBorderRectangle(root);
                    if (rect != null)
                    {
                        root.SetValue(CachedBorderRectProperty, rect);
                    }
                }

                if (rect != null)
                {
                    if (isHovered)
                    {
                        if (root.DataContext is not Models.AlbumItem { IsSelected: true } &&
                            rect.GetValue(OriginalStrokeProperty) == null && rect.Stroke != null)
                        {
                            rect.SetValue(OriginalStrokeProperty, rect.Stroke);
                            rect.SetValue(OriginalStrokeThicknessProperty, rect.StrokeThickness);
                        }
                        rect.Stroke = HoverCardBorderBrush;
                        rect.StrokeThickness = root.DataContext is Models.AlbumItem { IsSelected: true } ? 2.1 : 1.65;
                    }
                    else
                    {
                        if (root.DataContext is Models.AlbumItem albumItem)
                        {
                            rect.Stroke = albumItem.CardBorderBrush;
                            rect.StrokeThickness = albumItem.CardBorderThickness;
                        }
                        else
                        {
                            if (rect.GetValue(OriginalStrokeProperty) is Brush origBrush)
                            {
                                rect.Stroke = origBrush;
                            }
                            if (rect.GetValue(OriginalStrokeThicknessProperty) is double origThickness)
                            {
                                rect.StrokeThickness = origThickness;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static Rectangle? FindUniformBorderRectangle(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Rectangle rect && rect.Tag as string == "UniformBorderRect")
                {
                    return rect;
                }
                if (child is Grid or Border)
                {
                    var found = FindUniformBorderRectangle(child);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static void AnimateTranslation(UIElement element, float x, float y, int durationMs)
        {
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;

                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.16f, 1.0f),
                    new Vector2(0.3f, 1.0f));

                var transAnim = compositor.CreateVector3KeyFrameAnimation();
                transAnim.InsertKeyFrame(1.0f, new Vector3(x, y, 0f), easing);
                transAnim.Duration = TimeSpan.FromMilliseconds(durationMs);

                visual.StartAnimation("Translation", transAnim);
            }
            catch { }
        }

        /// <summary>
        /// Runs a smooth, hit-test-safe GPU entrance animation on the active Page content when Maximizing or Restoring.
        /// </summary>
        public static void PlayWindowStateReflowAnimation(FrameworkElement? pageElement, bool isMaximized)
        {
            if (pageElement == null) return;
            try
            {
                ElementCompositionPreview.SetIsTranslationEnabled(pageElement, true);
                var visual = ElementCompositionPreview.GetElementVisual(pageElement);
                var compositor = visual.Compositor;

                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.16f, 1.0f),
                    new Vector2(0.3f, 1.0f));

                var transAnim = compositor.CreateVector3KeyFrameAnimation();
                transAnim.InsertKeyFrame(0f, new Vector3(0f, isMaximized ? 8f : -6f, 0f));
                transAnim.InsertKeyFrame(1f, Vector3.Zero, easing);
                transAnim.Duration = TimeSpan.FromMilliseconds(260);

                var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(0f, 0.90f);
                opacityAnim.InsertKeyFrame(1f, 1.0f, easing);
                opacityAnim.Duration = TimeSpan.FromMilliseconds(220);

                visual.StartAnimation("Translation", transAnim);
                visual.StartAnimation("Opacity", opacityAnim);

                // Refresh all GridViews inside the page so their columns fill 100% of the new window width
                RefreshAllGridViews(pageElement);
            }
            catch { }
        }

        private static void RefreshAllGridViews(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is GridView gv)
                {
                    UpdateGridViewFullWidthItems(gv);
                }
                else
                {
                    RefreshAllGridViews(child);
                }
            }
        }

        /// <summary>
        /// Toggles a persistently mounted Page between active (visible & interactive) and inactive
        /// (transparent & hit-test disabled, while remaining mounted in the visual tree so its
        /// virtualized GridView/ListView items and GPU cover textures are never destroyed).
        /// </summary>
        public static void SetPageActiveState(FrameworkElement? page, bool isActive, bool animateEntrance = true)
        {
            if (page == null) return;
            try
            {
                ElementCompositionPreview.SetIsTranslationEnabled(page, true);
                var visual = ElementCompositionPreview.GetElementVisual(page);

                if (!isActive)
                {
                    page.IsHitTestVisible = false;
                    Canvas.SetZIndex(page, 0);
                    visual.StopAnimation("Opacity");
                    visual.StopAnimation("Translation");
                    visual.Opacity = 0f;
                    page.Opacity = 0.0;
                    return;
                }

                page.IsHitTestVisible = true;
                Canvas.SetZIndex(page, 10);
                page.Opacity = 1.0;
                RefreshAllGridViews(page);

                if (!animateEntrance)
                {
                    visual.StopAnimation("Opacity");
                    visual.StopAnimation("Translation");
                    visual.Opacity = 1.0f;
                    return;
                }

                PlayPageEntranceAnimation(page);
            }
            catch { }
        }

        /// <summary>
        /// Runs a fast, modern fade + vertical glide animation when a Page is navigated to.
        /// </summary>
        public static void PlayPageEntranceAnimation(FrameworkElement? page)
        {
            if (page == null) return;
            try
            {
                page.Opacity = 1.0;
                ElementCompositionPreview.SetIsTranslationEnabled(page, true);
                var visual = ElementCompositionPreview.GetElementVisual(page);
                var compositor = visual.Compositor;

                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.16f, 1.0f),
                    new Vector2(0.3f, 1.0f));

                var transAnim = compositor.CreateVector3KeyFrameAnimation();
                transAnim.InsertKeyFrame(0f, new Vector3(0f, 8f, 0f));
                transAnim.InsertKeyFrame(1f, Vector3.Zero, easing);
                transAnim.Duration = TimeSpan.FromMilliseconds(160);

                var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(0f, 0.88f);
                opacityAnim.InsertKeyFrame(1f, 1.0f, easing);
                opacityAnim.Duration = TimeSpan.FromMilliseconds(140);

                visual.StartAnimation("Translation", transAnim);
                visual.StartAnimation("Opacity", opacityAnim);
            }
            catch { }
        }
    }
}
