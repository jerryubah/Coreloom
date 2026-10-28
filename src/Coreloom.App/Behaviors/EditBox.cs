using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Coreloom.App.Behaviors;

/// <summary>
/// A text box for editing in place (renaming a device): <c>FocusOnShow</c> puts the caret in it with the text selected as
/// soon as it appears, and <c>CommitOnLeave</c> runs a command when the keyboard leaves it (clicking elsewhere keeps what
/// was typed, as renaming in File Explorer does).
/// </summary>
public static class EditBox
{
    public static readonly DependencyProperty FocusOnShowProperty = DependencyProperty.RegisterAttached(
        "FocusOnShow", typeof(bool), typeof(EditBox), new PropertyMetadata(false, OnFocusOnShowChanged));

    public static readonly DependencyProperty CommitOnLeaveProperty = DependencyProperty.RegisterAttached(
        "CommitOnLeave", typeof(ICommand), typeof(EditBox), new PropertyMetadata(null, OnCommitOnLeaveChanged));

    public static bool GetFocusOnShow(DependencyObject element) => (bool)element.GetValue(FocusOnShowProperty);

    public static void SetFocusOnShow(DependencyObject element, bool value) => element.SetValue(FocusOnShowProperty, value);

    public static ICommand? GetCommitOnLeave(DependencyObject element) => (ICommand?)element.GetValue(CommitOnLeaveProperty);

    public static void SetCommitOnLeave(DependencyObject element, ICommand? value) => element.SetValue(CommitOnLeaveProperty, value);

    private static void OnFocusOnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.IsVisibleChanged -= OnVisibleChanged;
        if (e.NewValue is true)
        {
            box.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (e.NewValue is false)
        {
            // Saved or cancelled: the keyboard leaves the hidden box for the window.
            if (box.IsKeyboardFocusWithin && Window.GetWindow(box) is { } window && !window.Focus())
            {
                Keyboard.ClearFocus();
            }

            return;
        }

        {
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (box.IsVisible && box.Focus())
                {
                    box.SelectAll();
                }
            });
        }
    }

    private static void OnCommitOnLeaveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.LostKeyboardFocus -= OnLostFocus;
        if (e.NewValue is not null)
        {
            box.LostKeyboardFocus += OnLostFocus;
        }
    }

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box && GetCommitOnLeave(box) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
