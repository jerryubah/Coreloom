using System.Windows.Controls;
using System.Windows.Input;
using Coreloom.Core.Settings;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Views;

// Code-behind for page views. Pages contain no logic beyond small input conveniences: everything is bound to the view models.

public partial class OverviewPage : UserControl
{
    public OverviewPage() => InitializeComponent();
}

public partial class GamingPage : UserControl
{
    public GamingPage() => InitializeComponent();
}

public partial class GpuPage : UserControl
{
    public GpuPage() => InitializeComponent();

    /// <summary>While the overlay's hotkey button waits, the next key press (with any modifiers) becomes the hotkey.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not GpuViewModel { OverlaySettings: { IsCapturingHotkey: true } settings })
        {
            return;
        }

        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        settings.CaptureHotkey(KeyInterop.VirtualKeyFromKey(key), modifiers);
        e.Handled = true; // nothing else reacts to keys typed as a hotkey (Space or Enter would click the button again)
    }

    private void OnHotkeyLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is GpuViewModel { OverlaySettings: { IsCapturingHotkey: true } settings })
        {
            settings.CaptureHotkey(0x1B, HotkeyModifiers.None); // clicking elsewhere cancels, like Esc
        }
    }

    /// <summary>Color boxes commit on focus loss; Enter commits immediately too.</summary>
    private void OnCommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
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

    /// <summary>Text boxes commit on focus loss; Enter commits immediately too.</summary>
    private void OnCommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
    }
}
