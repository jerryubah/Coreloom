using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Coreloom.App.Controls;
using Coreloom.App.Interop;
using Coreloom.App.Themes;
using Coreloom.Core.Startup;

namespace Coreloom.App.Views;

/// <summary>
/// Borderless 16:9 startup splash: flowing ribbons of light, the app icon (the dark-mode icon with the dark theme) on a
/// breathing halo, the wordmark, a glowing loading bar with moving stripes and the current step. Created and driven by
/// <see cref="Services.SplashScreenHost"/> on its own UI thread, so it keeps moving while the main thread builds the
/// dashboard. It never decides when to close; the app hands off when its window has painted.
/// <para>
/// Startup can't be measured, so the bar moves on with each step the app reports and creeps while a step takes long
/// (<see cref="SplashPolicy.ProgressTarget"/>); it only fills the last stretch when the app is ready.
/// </para>
/// </summary>
public partial class SplashWindow : Window
{
    private const double CornerRadius = 18;

    private static readonly SolidColorBrush ErrorBrush = Frozen(Color.FromRgb(0xDD, 0x74, 0x6C));

    private readonly bool _reducedMotion;
    private readonly Stopwatch _stageClock = Stopwatch.StartNew();
    private int _stage;
    private double _progress;
    private bool _finishing;
    private bool _rendering;
    private TimeSpan _lastFrame;
    private bool _closing;
    private bool _closed;
    private bool _failed;

    public SplashWindow(bool reducedMotion, string status, bool darkIcon)
    {
        // Resource lookups and inherited values stop at this window and continue at the system theme. App.xaml's
        // dictionaries belong to the main UI thread (and are being filled while this window is up), so nothing on
        // this thread may read them.
        InheritanceBehavior = InheritanceBehavior.SkipToThemeNext;
        _reducedMotion = reducedMotion;
        InitializeComponent();
        ShowStatus(status);
        LogoImage.Source = LoadIcon(darkIcon);
        Waves.IsAnimated = !reducedMotion;
        Root.SizeChanged += (_, _) => ShapeCorners();

        SourceInitialized += (_, _) =>
        {
            SplashHandle = new WindowInteropHelper(this).Handle;
            NativeWindow.MakeSplash(SplashHandle);  // no taskbar button, not in Alt+Tab, never takes focus
        };
        Loaded += (_, _) => BeginEntrance();
    }

    /// <summary>Native handle once created; the main thread uses it to keep the splash above the app during the handoff.</summary>
    public IntPtr SplashHandle { get; private set; }

    public void SetStatus(string text)
    {
        if (!_failed && !_closed && !string.IsNullOrWhiteSpace(text) && text != StatusText.Text)
        {
            ShowStatus(text);
            _stage++;
            _stageClock.Restart();
            Announce(StatusText);
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusLabel.Text = text.ToUpperInvariant().TrimEnd('.', '…') + "…";
    }

    /// <summary>Closes the window unless it has already closed (Close() on a closed window throws).</summary>
    public void CloseOnce()
    {
        if (!_closed)
        {
            _closing = true;
            Close();
        }
    }

    /// <summary>Stops the loading line and shows a short message. The app closes the splash after its error dialog.</summary>
    public void ShowError(string message)
    {
        if (_closing || _closed)
        {
            return;
        }

        _failed = true;
        StopRendering();
        Body.BeginAnimation(OpacityProperty, null);
        Body.Opacity = 1;
        BodyOffset.BeginAnimation(TranslateTransform.YProperty, null);
        BodyOffset.Y = 0;

        StripeOffset.BeginAnimation(TranslateTransform.XProperty, null);
        Fill.Width = Bar.Width;
        Fill.Background = ErrorBrush;
        Fill.Opacity = 0.85;
        FillGlow.Visibility = Visibility.Collapsed;

        ShowStatus("Startup interrupted");
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        Announce(ErrorText);
    }

    /// <summary>Fades the whole splash out over the app window, then closes it (instantly with reduced motion).</summary>
    public void FadeOutAndClose()
    {
        if (_closing || _closed)
        {
            return;
        }

        _closing = true;
        _finishing = true; // the bar fills the rest of the way as the splash fades
        if (_reducedMotion)
        {
            CloseOnce();
            return;
        }

        var fade = Animate(1, 0, SplashPolicy.HandoffDuration, new KeySpline(0.22, 0.6, 0.32, 1));
        fade.Completed += (_, _) => CloseOnce();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        StopRendering();
        base.OnClosed(e);
    }

    /// <summary>Squircle corners for the window and its rim (the window itself is transparent outside them).</summary>
    private void ShapeCorners()
    {
        var rect = new Rect(Root.RenderSize);
        Root.Clip = Squircle.Create(rect, new CornerRadius(CornerRadius));
        Rim.Data = Squircle.Inset(rect, new CornerRadius(CornerRadius), 0.5);
    }

    private static BitmapImage? LoadIcon(bool dark)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(AppIcons.Png(dark, 512), UriKind.Absolute);
            image.DecodePixelWidth = 336; // sharp up to 300 % scaling
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // the splash still shows the wordmark
        }
    }

    // ------------------------------------------------------------------ the loading bar

    private void StartRendering()
    {
        if (!_rendering)
        {
            _rendering = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopRendering()
    {
        if (_rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        if (now == _lastFrame)
        {
            return;
        }

        var frame = _lastFrame == TimeSpan.Zero ? TimeSpan.FromMilliseconds(16) : now - _lastFrame;
        _lastFrame = now;
        if (_finishing)
        {
            double step = frame.TotalMilliseconds / SplashPolicy.ProgressFinish.TotalMilliseconds;
            _progress = Math.Min(1, _progress + Math.Max(step * (1 - _progress) * 2.2, step * 0.2));
        }
        else
        {
            _progress = SplashPolicy.ProgressStep(_progress, SplashPolicy.ProgressTarget(_stage, _stageClock.Elapsed), frame);
        }

        SetFill(_progress);
    }

    private void SetFill(double fraction)
    {
        double width = Math.Round(Bar.Width * Math.Clamp(fraction, 0, 1), 1);
        Fill.Width = Math.Max(width, Fill.CornerRadius.TopLeft * 2 * Math.Min(1, fraction * 20));
        FillGlow.Width = Fill.Width + 12;
    }

    private void BeginEntrance()
    {
        if (_failed || _closing || _closed)
        {
            return;
        }

        StartRendering();
        if (_reducedMotion)
        {
            // No fade, rise, stripes or breathing; the bar still moves on with each step.
            Body.Opacity = 1;
            BodyOffset.Y = 0;
            return;
        }

        var entrance = new KeySpline(0.16, 1, 0.3, 1);
        var fade = Animate(0, 1, SplashPolicy.EntranceDuration, entrance);
        var rise = Animate(SplashPolicy.EntranceRise, 0, SplashPolicy.EntranceDuration, entrance);
        fade.BeginTime = SplashPolicy.EntranceDelay;
        rise.BeginTime = SplashPolicy.EntranceDelay;
        BodyOffset.Y = SplashPolicy.EntranceRise;
        Body.BeginAnimation(OpacityProperty, fade);
        BodyOffset.BeginAnimation(TranslateTransform.YProperty, rise);

        // The bar's stripes slide along, and the icon's halo breathes.
        StripeOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 14, TimeSpan.FromMilliseconds(700)) { RepeatBehavior = RepeatBehavior.Forever });
        var breathe = new DoubleAnimation(0.92, 1.08, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        HaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
        HaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        Halo.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>Tells screen readers that a live-region text changed (WPF doesn't raise this by itself).</summary>
    private static void Announce(UIElement element) =>
        UIElementAutomationPeer.FromElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

    private static DoubleAnimationUsingKeyFrames Animate(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
