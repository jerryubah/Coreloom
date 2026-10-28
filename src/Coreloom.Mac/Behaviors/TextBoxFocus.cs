using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Coreloom.Mac.Behaviors;

/// <summary>
/// Lets people leave a text box the way they expect: clicking anywhere outside it, or pressing Esc in it, takes the
/// keyboard out of the box (the caret goes away and what was typed is kept). Without this, a click on a card or on
/// empty space leaves the caret blinking and typing still goes into the box. Pop-ups (a menu, a list of suggestions) are
/// windows of their own and aren't affected.
/// </summary>
public static class TextBoxFocus
{
    public static void Attach(Window window)
    {
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: false);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Window window || window.FocusManager?.GetFocusedElement() is not TextBox box)
        {
            return;
        }

        if (e.Source is Visual source && (ReferenceEquals(source, box) || box.IsVisualAncestorOf(source)))
        {
            return; // a click in the box itself (placing the caret, selecting)
        }

        Leave(window, box);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // A box for editing in place handles Esc itself (it cancels the edit).
        if (e.Key == Key.Escape && sender is Window window && window.FocusManager?.GetFocusedElement() is TextBox box && EditBox.GetCommitOnLeave(box) is null)
        {
            Leave(window, box);
            e.Handled = true;
        }
    }

    private static void Leave(Window window, TextBox box)
    {
        // What was typed is committed to its binding, as when the box loses focus any other way.
        Avalonia.Data.BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
        window.FocusManager?.ClearFocus();
        if (window.FocusManager?.GetFocusedElement() is TextBox)
        {
            window.Focus(); // some platforms keep it: the window itself takes the focus instead
        }
    }
}
