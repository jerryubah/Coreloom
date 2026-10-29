using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Behaviors;
using Coreloom.App.Interop;
using Coreloom.App.Themes;
using Coreloom.Core.Settings;
using Coreloom.ViewModels;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Views;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsCompactSidebarProperty = DependencyProperty.Register(
        nameof(IsCompactSidebar), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private const double ExpandedSidebarWidth = 232;
    private const double CompactSidebarWidth = 62;
    private const double AutoCompactWidth = 980;

    private readonly ShellViewModel _shell;
    private bool _syncingNavigation;
    private long _lastPulseSequence;
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        DataContext = shell;
        InitializeComponent();
        TextBoxFocus.Attach(this); // clicking outside a text box (or Esc) leaves it

        SettingsNav.ItemsSource = new[] { shell.Settings };
        SyncNavigationSelection();
        UpdateMaximizeGlyph();

        shell.PropertyChanged += OnShellPropertyChanged;
        shell.Timeline.PropertyChanged += OnTimelinePropertyChanged;
        SourceInitialized += (_, _) => ThemeService.Instance?.ApplyToWindow(this);
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged += (_, _) => UpdateSidebarText();
        }

        UpdateSidebarText();
        Loaded += (_, _) =>
        {
            SnapLayoutSupport.Attach(this, MaximizeButton);
            UpdateSidebar(animate: false);
        };
        StateChanged += (_, _) => OnWindowStateChanged();
        SizeChanged += (_, _) => UpdateSidebar(animate: true);
        IsVisibleChanged += (_, _) => _shell.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
    }

    /// <summary>True when the sidebar shows icons only (user setting or narrow window).</summary>
    public bool IsCompactSidebar
    {
        get => (bool)GetValue(IsCompactSidebarProperty);
        set => SetValue(IsCompactSidebarProperty, value);
    }

    /// <summary>Set by the app when closing should really exit instead of hiding to the tray.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Raised when the window was hidden to the tray instead of closing.</summary>
    public event EventHandler? HiddenToTray;

    public Func<DesktopSettings>? DesktopSettingsProvider { get; set; }

    public void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        Activate();
        Topmost = true;  // reliably bring to front
        Topmost = false;
        Focus();
    }

    public void ApplyPlacement(WindowPlacement? placement)
    {
        if (placement is null)
        {
            return;
        }

        var bounds = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!virtualScreen.IntersectsWith(bounds) || bounds.Width < MinWidth || bounds.Height < MinHeight)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Current normal-state bounds, or null when the window was never shown (bounds unknown).</summary>
    public WindowPlacement? CapturePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return null;
        }

        return new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    /// <summary>Placement captured when the window last started closing (still valid after it is closed).</summary>
    public WindowPlacement? LastPlacement { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        LastPlacement = CapturePlacement() ?? LastPlacement;
        var desktop = DesktopSettingsProvider?.Invoke();
        if (!AllowClose && desktop?.CloseToTray == true)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void OnWindowStateChanged()
    {
        if (WindowState != WindowState.Minimized)
        {
            _restoreState = WindowState;
        }

        RootBorder.Margin = WindowState == WindowState.Maximized ? NativeWindow.MaximizedInset(this) : new Thickness(0);
        UpdateMaximizeGlyph();

        if (WindowState == WindowState.Minimized && DesktopSettingsProvider?.Invoke().MinimizeToTray == true)
        {
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }

        _shell.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
    }

    private void UpdateMaximizeGlyph()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = FindResource(maximized ? "Glyph.Restore" : "Glyph.Maximize");
        MaximizeButton.ToolTip = maximized ? "Restore down" : "Maximize";
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
        if (e.PropertyName == nameof(Coreloom.ViewModels.Infrastructure.TimelineViewModel.Now) && _shell.Timeline.Now != _lastPulseSequence)
        {
            _lastPulseSequence = _shell.Timeline.Now;
            Pulse();
        }
    }

    /// <summary>One short "heartbeat" per sample (no continuous animation, so idle rendering stays at zero).</summary>
    private void Pulse()
    {
        if (Motion.ReducedMotion || !IsVisible || _shell.IsMonitoringPaused)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(650);
        var grow = new DoubleAnimation(0.6, 1.7, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.45, 0, duration);
        LivePulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        LivePulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        LivePulse.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateSidebar(bool animate)
    {
        bool compact = _shell.IsSidebarCollapsed || ActualWidth < AutoCompactWidth;
        if (compact != IsCompactSidebar || !animate)
        {
            IsCompactSidebar = compact;
            double target = compact ? CompactSidebarWidth : ExpandedSidebarWidth;
            if (animate && !Motion.ReducedMotion && IsLoaded)
            {
                var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Sidebar.BeginAnimation(WidthProperty, animation);
            }
            else
            {
                Sidebar.BeginAnimation(WidthProperty, null);
                Sidebar.Width = target;
            }
        }
    }

    /// <summary>
    /// With Mica the whole window is see-through to the desktop, and ClearType can't be blended onto see-through pixels
    /// (it fringes), so text is smoothed in grayscale then; on Coreloom's own backgrounds (opaque) text uses ClearType.
    /// </summary>
    private void UpdateSidebarText()
    {
        if (ThemeService.Instance?.IsMicaActive == true)
        {
            TextOptions.SetTextRenderingMode(RootBorder, TextRenderingMode.Grayscale);
        }
        else
        {
            RootBorder.ClearValue(TextOptions.TextRenderingModeProperty); // the window's (ClearType unless Windows has it off)
        }
    }

    private void SyncNavigationSelection()
    {
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

    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNavigation)
        {
            return;
        }

        if (sender is ListBox list && list.SelectedItem is PageViewModel page)
        {
            _shell.Navigate(page);
            SyncNavigationSelection();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
