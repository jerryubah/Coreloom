using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Coreloom.App.Behaviors;

namespace Coreloom.App.Controls;

/// <summary>
/// Hosts the current page view and keeps one view instance per page view model, so navigating back to a
/// page is instant (no template re-instantiation, no re-creation of dozens of charts). Views are created from
/// the DataTemplate registered for the view model's type, exactly as a ContentControl would; they are simply
/// not thrown away. Removing a view from the tree still raises Unloaded, so charts detach their event handlers
/// while hidden and re-attach on Loaded.
/// <para>
/// Since 1.9.0 the page you leave blurs and fades out over a sixth of a second before the next one comes in (its own
/// entrance brings it from a blur to sharp); nothing moves with reduced motion.
/// </para>
/// </summary>
public sealed class PageHost : ContentControl
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(PageHost), new PropertyMetadata(null, OnPageChanged));

    /// <summary>How long the page you leave takes to go.</summary>
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(150);

    private readonly Dictionary<object, FrameworkElement> _views = new(ReferenceEqualityComparer.Instance);
    private object? _pending;
    private bool _leaving;

    public PageHost()
    {
        Focusable = false;
        IsTabStop = false;
    }

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (PageHost)d;
        object? page = e.NewValue;
        if (page is null)
        {
            host._pending = null;
            host.Content = null;
            return;
        }

        if (host._leaving)
        {
            host._pending = page; // the one that counts is the last asked for while the old page goes
            return;
        }

        if (host.Content is FrameworkElement leaving && !Motion.ReducedMotion && host.IsVisible && leaving.Opacity > 0)
        {
            host._pending = page;
            host.Leave(leaving);
            return;
        }

        host.Show(page);
    }

    /// <summary>The page you leave: a blur and a fade, then the next page.</summary>
    private void Leave(FrameworkElement view)
    {
        _leaving = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var blur = new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        view.Effect = blur;
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 6, LeaveDuration) { EasingFunction = ease });
        var fade = new DoubleAnimation(1, 0, LeaveDuration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            view.BeginAnimation(UIElement.OpacityProperty, null);
            view.Opacity = 1;
            if (ReferenceEquals(view.Effect, blur))
            {
                view.Effect = null;
            }

            _leaving = false;
            object? next = _pending;
            _pending = null;
            if (next is not null)
            {
                Show(next);
            }
        };
        view.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Show(object page)
    {
        if (!_views.TryGetValue(page, out var view))
        {
            view = CreateView(page);
            if (view is null)
            {
                Content = page; // no template: let the ContentControl show whatever it can
                return;
            }

            view.DataContext = page;
            _views[page] = view;
        }

        Content = view;
    }

    private FrameworkElement? CreateView(object page)
    {
        var template = TryFindResource(new DataTemplateKey(page.GetType())) as DataTemplate;
        return template?.LoadContent() as FrameworkElement;
    }
}
