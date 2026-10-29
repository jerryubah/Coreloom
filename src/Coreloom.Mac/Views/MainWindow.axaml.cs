using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Coreloom.Core.Settings;
using Coreloom.Mac.Behaviors;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;

namespace Coreloom.Mac.Views;

/// <summary>
/// The main window: the glass sidebar (with the window's close, minimize and zoom buttons in its top row) and the page. The title bar area drags the
/// window and a double-click zooms it. Closing the window keeps Coreloom running in the menu bar (unless that's turned
/// off); the Dock icon or the menu bar item brings it back.
/// </summary>
public partial class MainWindow : Window
{
    public static readonly StyledProperty<bool> IsCompactSidebarProperty = AvaloniaProperty.Register<MainWindow, bool>(nameof(IsCompactSidebar));

    private const double ExpandedSidebarWidth = 232;
    private const double CompactSidebarWidth = 76; // wide enough for the window buttons inside the glass
    private const double AutoCompactWidth = 980;

    private readonly ShellViewModel? _shell;
    private bool _syncingNavigation;
    private long _lastPulseSequence;
    private Animator.Tween? _sidebarTween;
    private PixelPoint _lastPosition;
    private Size _lastSize = new(1360, 880);
    private Avalonia.Threading.DispatcherTimer? _buttonsTimer;
    private bool _buttonsPlaceable = true;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ShellViewModel shell)
        : this()
    {
        _shell = shell;
        DataContext = shell;
        SettingsNav.ItemsSource = new[] { shell.Settings };
        SyncNavigationSelection();
        NavList.SelectionChanged += OnNavigationSelectionChanged;
        SettingsNav.SelectionChanged += OnNavigationSelectionChanged;

        shell.PropertyChanged += OnShellPropertyChanged;
        shell.Timeline.PropertyChanged += OnTimelinePropertyChanged;
        foreach (var dragArea in new Control[] { TitleBar, SidebarDrag })
        {
            dragArea.PointerPressed += OnTitleBarPressed;
        }

        Loaded += (_, _) =>
        {
            UpdateSidebar(animate: false);
            PlaceWindowButtons();

            // macOS lays the title bar out again now and then (resizing, full screen, becoming active): put the buttons
            // back inside the glass whenever it has (a quick check when they're in place).
            _buttonsTimer = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), Avalonia.Threading.DispatcherPriority.Background, (_, _) => PlaceWindowButtons());
            _buttonsTimer.Start();
            // Once laid out: where macOS put the window buttons, and that clicks near the top reach Coreloom (logged).
            Avalonia.Threading.DispatcherTimer.RunOnce(CheckTitleBar, TimeSpan.FromSeconds(2));
        };
        PositionChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _lastPosition = e.Point;
            }
        };
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _lastSize = e.NewSize;
            }

            UpdateSidebar(animate: true);
            PlaceWindowButtons();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty)
            {
                _shell?.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
            }
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        TextBoxFocus.Attach(this); // clicking outside a text box (or Esc) leaves it

        // The translucent sidebar: macOS's vibrancy (a blur of what's behind the window) through the sidebar's glass.
        if (Themes.ThemeService.Instance is { } theme)
        {
            ApplyTranslucency(theme);
            theme.ThemeChanged += (_, _) => ApplyTranslucency(theme);
        }
    }

    /// <summary>
    /// Puts the close, minimize and zoom buttons inside the sidebar's glass: centred on its top row and left-aligned with
    /// its inset (centred across the narrow sidebar). Not in full screen, where macOS shows them in its own bar.
    /// </summary>
    private void PlaceWindowButtons()
    {
        if (!_buttonsPlaceable || WindowState == WindowState.FullScreen || !IsVisible || Sidebar.Bounds.Width <= 0
            || TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } handle || Sidebar.TranslatePoint(default, this) is not { } glass)
        {
            return;
        }

        const double ButtonsWidth = 54; // three 14-point buttons, 20 points apart
        double inset = IsCompactSidebar ? Math.Max(6, (Sidebar.Bounds.Width - ButtonsWidth) / 2) : 16;
        try
        {
            _buttonsPlaceable = Native.WindowChrome.PlaceButtons(handle.Handle, glass.X + inset, glass.Y + 20);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _buttonsPlaceable = false;
            Coreloom.Core.Diagnostics.Log.Warn("Window", "Couldn't place the window buttons inside the sidebar.", e);
        }
    }

    /// <summary>
    /// Logs where macOS put the window buttons and which view a click on the controls near the top would reach (a check
    /// for the Mac verification runs: the buttons should sit inside the glass, and every control should still be ours).
    /// </summary>
    private void CheckTitleBar()
    {
        try
        {
            if (TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } handle)
            {
                return;
            }

            var points = new List<(string, double, double, bool)>();
            void Add(string name, Control? control)
            {
                if (control is { IsVisible: true } && control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this) is { } p)
                {
                    points.Add((name, p.X, p.Y, true));
                }
            }

            Add("collapse", CollapseButton);
            Add("first page", NavList.ContainerFromIndex(0) as Control);
            Add("range", RangePicker);
            Add("freeze", FreezeButton);
            if (Sidebar.TranslatePoint(default, this) is { } glass)
            {
                points.Add(("glass top", glass.X + (Sidebar.Bounds.Width / 2), glass.Y + 4, false));
            }

            var (report, reach) = Native.WindowChrome.Describe(handle.Handle, points);
            string glassText = Sidebar.TranslatePoint(default, this) is { } g
                ? FormattableString.Invariant($"glass {g.X:0},{g.Y:0} {Sidebar.Bounds.Width:0}×{Sidebar.Bounds.Height:0}")
                : "glass ?";
            if (reach)
            {
                Coreloom.Core.Diagnostics.Log.Info("Window", $"Title bar: {glassText}; {report}");
            }
            else
            {
                Coreloom.Core.Diagnostics.Log.Warn("Window", $"Title bar: a control near the top doesn't get clicks. {glassText}; {report}");
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            Coreloom.Core.Diagnostics.Log.Warn("Window", "Checking the title bar failed.", e);
        }
    }

    private void ApplyTranslucency(Themes.ThemeService theme)
    {
        TransparencyLevelHint = theme.IsTranslucent ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None] : [WindowTransparencyLevel.None];
        Backdrop.SeeThroughGlass = theme.IsTranslucent;
    }

    public bool IsCompactSidebar
    {
        get => GetValue(IsCompactSidebarProperty);
        set => SetValue(IsCompactSidebarProperty, value);
    }

    /// <summary>True while Coreloom is quitting: the window really closes instead of hiding.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Raised when closing the window only hid it (Coreloom keeps running in the menu bar).</summary>
    public event EventHandler? HiddenToMenuBar;

    public Func<DesktopSettings>? DesktopSettingsProvider { get; set; }

    public WindowPlacement? LastPlacement { get; private set; }

    /// <summary>Shows the window (from the menu bar, the Dock or a second launch) and brings it to the front.</summary>
    public void ShowFromMenuBar()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void ApplyPlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
        {
            FitToScreen();
            return;
        }

        var screens = Screens.All;
        var bounds = new PixelRect((int)placement.Left, (int)placement.Top, (int)placement.Width, (int)placement.Height);
        if (screens.Count > 0 && !screens.Any(s => s.Bounds.Intersects(bounds)))
        {
            return; // the screen it was on is gone
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)placement.Left, (int)placement.Top);
        Width = placement.Width;
        Height = placement.Height;
        _lastPosition = Position;
        _lastSize = new Size(Width, Height);
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// The first opening: the default size, made smaller to fit a small screen (a 13-inch MacBook Air, a scaled display),
    /// with room around it, and centred.
    /// </summary>
    private void FitToScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null || screen.Scaling <= 0)
        {
            return;
        }

        double width = screen.WorkingArea.Width / screen.Scaling;
        double height = screen.WorkingArea.Height / screen.Scaling;
        Width = Math.Max(MinWidth, Math.Min(Width, width * 0.92));
        Height = Math.Max(MinHeight, Math.Min(Height, height * 0.92));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public WindowPlacement? CapturePlacement()
    {
        if (!double.IsFinite(_lastSize.Width) || !double.IsFinite(_lastSize.Height) || _lastSize.Width <= 0)
        {
            return null;
        }

        return new WindowPlacement
        {
            Left = _lastPosition.X,
            Top = _lastPosition.Y,
            Width = _lastSize.Width,
            Height = _lastSize.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        LastPlacement = CapturePlacement() ?? LastPlacement;
        var desktop = DesktopSettingsProvider?.Invoke();
        if (!AllowClose && desktop?.CloseToTray != false)
        {
            e.Cancel = true;
            Hide();
            HiddenToMenuBar?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            // macOS's "double-click a window's title bar to zoom".
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>⌘1 … ⌘9 open the pages in the sidebar's order; ⌃⌘S shows or hides the sidebar.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null || e.Handled)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Meta && e.Key is >= Key.D1 and <= Key.D9)
        {
            var pages = _shell.Pages.Where(p => p.IsEnabled).ToList();
            int index = e.Key - Key.D1;
            if (index < pages.Count)
            {
                _shell.Navigate(pages[index]);
                e.Handled = true;
            }
        }
        else if (e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Control) && e.Key == Key.S)
        {
            _shell.ToggleSidebarCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.CurrentPage):
                SyncNavigationSelection();
                break;
            case nameof(ShellViewModel.IsSidebarCollapsed):
                UpdateSidebar(animate: true);
                break;
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_shell is not null && e.PropertyName == nameof(Coreloom.ViewModels.Infrastructure.TimelineViewModel.Now) && _shell.Timeline.Now != _lastPulseSequence)
        {
            _lastPulseSequence = _shell.Timeline.Now;
            Pulse();
        }
    }

    /// <summary>The live dot sends out a ring with every reading.</summary>
    private void Pulse()
    {
        if (Motion.ReducedMotion || !IsVisible || _shell?.IsMonitoringPaused != false || LivePulse.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        Animator.Run(TimeSpan.FromMilliseconds(650), Ease.CubicOut, t =>
        {
            double s = 0.6 + (1.1 * t);
            scale.ScaleX = s;
            scale.ScaleY = s;
            LivePulse.Opacity = 0.45 * (1 - t);
        });
    }

    private void UpdateSidebar(bool animate)
    {
        if (_shell is null)
        {
            return;
        }

        bool compact = _shell.IsSidebarCollapsed || Bounds.Width < AutoCompactWidth;
        if (compact == IsCompactSidebar && animate)
        {
            return;
        }

        IsCompactSidebar = compact;
        double target = compact ? CompactSidebarWidth : ExpandedSidebarWidth;
        _sidebarTween?.Cancel();
        if (animate && !Motion.ReducedMotion && IsLoaded)
        {
            double from = Sidebar.Bounds.Width > 0 ? Sidebar.Bounds.Width : Sidebar.Width;
            _sidebarTween = Animator.Run(TimeSpan.FromMilliseconds(180), Ease.CubicOut, t => Sidebar.Width = from + ((target - from) * t));
        }
        else
        {
            Sidebar.Width = target;
        }
    }

    private void SyncNavigationSelection()
    {
        if (_shell is null)
        {
            return;
        }

        _syncingNavigation = true;
        try
        {
            var page = _shell.CurrentPage;
            NavList.SelectedItem = page is SettingsViewModel ? null : page;
            SettingsNav.SelectedItem = page is SettingsViewModel ? page : null;
        }
        finally
        {
            _syncingNavigation = false;
        }
    }

    private void OnNavigationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingNavigation || _shell is null)
        {
            return;
        }

        if (sender is ListBox list && list.SelectedItem is PageViewModel page)
        {
            _shell.Navigate(page);
            SyncNavigationSelection();
        }
    }
}
