using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Coreloom.Mac.Input;
using Coreloom.ViewModels.Pages;

namespace Coreloom.Mac.Views;

// Code-behind for page views. Pages contain no logic beyond small input conveniences: everything is bound to the view models.

public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
        Scrim.PointerPressed += (_, e) =>
        {
            if (DataContext is OverviewViewModel page)
            {
                page.Customize.CloseCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}

public partial class GamingPage : UserControl
{
    public GamingPage() => InitializeComponent();
}

public partial class BenchmarkingPage : UserControl
{
    public BenchmarkingPage()
    {
        InitializeComponent();
        Scrim.PointerPressed += (_, e) =>
        {
            if (DataContext is BenchmarkingViewModel page)
            {
                page.CloseDetailsCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}

public partial class GpuPage : UserControl
{
    public GpuPage()
    {
        InitializeComponent();
        HotkeyButton.AddHandler(KeyDownEvent, OnHotkeyKeyDown, RoutingStrategies.Tunnel);
        HotkeyButton.LostFocus += OnHotkeyLostFocus;
        Scrim.PointerPressed += (_, e) =>
        {
            if (DataContext is GpuViewModel page)
            {
                page.OverlaySettings.CloseCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    /// <summary>While the overlay's hotkey button waits, the next key press (with any modifiers) becomes the hotkey.</summary>
    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not GpuViewModel { OverlaySettings: { IsCapturingHotkey: true } settings })
        {
            return;
        }

        settings.CaptureHotkey(KeyCodes.VirtualKey(e.Key), KeyCodes.Modifiers(e.KeyModifiers));
        e.Handled = true; // nothing else reacts to keys typed as a hotkey (Space or Return would click the button again)
    }

    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GpuViewModel { OverlaySettings: { IsCapturingHotkey: true } settings })
        {
            settings.CaptureHotkey(0x1B, Coreloom.Core.Settings.HotkeyModifiers.None); // clicking elsewhere cancels, like Esc
        }
    }
}

public partial class CpuPage : UserControl
{
    public CpuPage() => InitializeComponent();
}

public partial class MemoryPage : UserControl
{
    public MemoryPage() => InitializeComponent();
}

public partial class StoragePage : UserControl
{
    public StoragePage() => InitializeComponent();
}

public partial class DevicesPage : UserControl
{
    public DevicesPage() => InitializeComponent();
}

public partial class NetworkPage : UserControl
{
    public NetworkPage() => InitializeComponent();
}

public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();
}

public partial class OptimizePage : UserControl
{
    public OptimizePage() => InitializeComponent();
}

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}
