using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Coreloom.Mac.Behaviors;

/// <summary>
/// A text box for editing in place (renaming a device): <c>FocusOnShow</c> puts the caret in it with the text selected as
/// soon as it appears, and <c>CommitOnLeave</c> runs a command when the keyboard leaves it (clicking elsewhere keeps what
/// was typed, as renaming in Finder does).
/// </summary>
public static class EditBox
{
    public static readonly AttachedProperty<bool> FocusOnShowProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("FocusOnShow", typeof(EditBox));

    public static readonly AttachedProperty<ICommand?> CommitOnLeaveProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("CommitOnLeave", typeof(EditBox));

    static EditBox()
    {
        FocusOnShowProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.NewValue is true)
            {
                box.PropertyChanged += OnBoxPropertyChanged;
                if (box.IsEffectivelyVisible)
                {
                    FocusSoon(box);
                }
            }
            else
            {
                box.PropertyChanged -= OnBoxPropertyChanged;
            }
        });
        CommitOnLeaveProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.LostFocus -= OnLostFocus;
            if (e.NewValue is not null)
            {
                box.LostFocus += OnLostFocus;
            }
        });
    }

    public static bool GetFocusOnShow(TextBox box) => box.GetValue(FocusOnShowProperty);

    public static void SetFocusOnShow(TextBox box, bool value) => box.SetValue(FocusOnShowProperty, value);

    public static ICommand? GetCommitOnLeave(TextBox box) => box.GetValue(CommitOnLeaveProperty);

    public static void SetCommitOnLeave(TextBox box, ICommand? value) => box.SetValue(CommitOnLeaveProperty, value);

    private static void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not TextBox box)
        {
            return;
        }

        if (e.NewValue is true)
        {
            FocusSoon(box);
        }
        else if (box.IsFocused)
        {
            // Saved or cancelled: the keyboard leaves the hidden box.
            TopLevel.GetTopLevel(box)?.FocusManager?.ClearFocus();
        }
    }

    private static void FocusSoon(TextBox box) => Dispatcher.UIThread.Post(() =>
    {
        if (box.IsEffectivelyVisible && box.Focus())
        {
            box.SelectAll();
        }
    }, DispatcherPriority.Input);

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && GetCommitOnLeave(box) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
