using Avalonia;
using Avalonia.Controls;

namespace Coreloom.Mac.Controls;

/// <summary>Marks the live readings window (as opposed to its preview in Settings): set on the window, inherited by its content.</summary>
public static class OverlayHost
{
    public static readonly AttachedProperty<bool> IsLiveProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsLive", typeof(OverlayHost), inherits: true);

    public static bool GetIsLive(Control element) => element.GetValue(IsLiveProperty);

    public static void SetIsLive(Control element, bool value) => element.SetValue(IsLiveProperty, value);
}
