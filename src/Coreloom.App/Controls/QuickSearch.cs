using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Behaviors;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.App.Controls;

/// <summary>What a row under the search box is.</summary>
public enum QuickSearchRowKind
{
    /// <summary>Nothing typed yet: "Start typing to search".</summary>
    Hint,

    /// <summary>"Show all matches for …": keeps the text as the page's filter.</summary>
    Filter,

    /// <summary>One result.</summary>
    Result,

    /// <summary>"No matches for …".</summary>
    Empty,
}

/// <summary>A row in the search box's list.</summary>
public sealed class QuickSearchRow(QuickSearchRowKind kind, string title, string detail, string iconKey, SearchSuggestion? suggestion = null)
{
    public QuickSearchRowKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string IconKey { get; } = iconKey;

    public SearchSuggestion? Suggestion { get; } = suggestion;

    public bool CanChoose => Kind is QuickSearchRowKind.Filter or QuickSearchRowKind.Result;
}

/// <summary>
/// A search box that answers as you type. Clicking it (or Ctrl+K / Ctrl+F on its page) drops a panel down from it:
/// "Start typing to search" at first, then "Show all matches for …" and the best few results (title, detail and icon).
/// Up and Down move through the rows, Enter picks one (a result fills the box with its name, so the page shows just that),
/// Esc closes the panel and × clears the search. The page filters as you type, as before.
/// </summary>
public sealed class QuickSearch : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(QuickSearch),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged) { DefaultUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(QuickSearch), new PropertyMetadata("Search"));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(QuickSearch), new PropertyMetadata("Start typing to search", (d, _) => ((QuickSearch)d).Rebuild()));

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(QuickSearch), new PropertyMetadata(null, (d, _) => ((QuickSearch)d).Rebuild()));

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(QuickSearch), new PropertyMetadata(false, OnIsDropDownOpenChanged));

    private static readonly DependencyPropertyKey HasTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasText), typeof(bool), typeof(QuickSearch), new PropertyMetadata(false));

    public static readonly DependencyProperty HasTextProperty = HasTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HighlightedRowPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HighlightedRow), typeof(QuickSearchRow), typeof(QuickSearch), new PropertyMetadata(null));

    public static readonly DependencyProperty HighlightedRowProperty = HighlightedRowPropertyKey.DependencyProperty;

    public static readonly RoutedCommand ClearCommand = new(nameof(ClearCommand), typeof(QuickSearch));

    private TextBox? _box;
    private Popup? _popup;
    private ListBox? _list;
    private FrameworkElement? _panel;
    private Window? _window;
    private int _highlight = -1;

    public QuickSearch()
    {
        Rows = [];
        Focusable = false;
        CommandBindings.Add(new CommandBinding(ClearCommand, (_, _) => Clear()));
        Loaded += (_, _) => HookWindow(Window.GetWindow(this));
        Unloaded += (_, _) =>
        {
            HookWindow(null);
            IsDropDownOpen = false;
        };
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Shown in the empty box.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>The panel's first line before anything is typed.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>The results for the current text (<see cref="SearchSuggestion"/>s from the page's view model).</summary>
    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public bool HasText => (bool)GetValue(HasTextProperty);

    public QuickSearchRow? HighlightedRow => (QuickSearchRow?)GetValue(HighlightedRowProperty);

    /// <summary>The panel's rows.</summary>
    public ObservableCollection<QuickSearchRow> Rows { get; }

    public override void OnApplyTemplate()
    {
        if (_box is not null)
        {
            _box.GotKeyboardFocus -= OnBoxFocused;
            _box.LostKeyboardFocus -= OnBoxLostFocus;
            _box.PreviewKeyDown -= OnBoxKeyDown;
            _box.PreviewMouseLeftButtonDown -= OnBoxMouseDown;
        }

        if (_list is not null)
        {
            _list.PreviewMouseLeftButtonDown -= OnListMouseDown;
            _list.MouseMove -= OnListMouseMove;
        }

        base.OnApplyTemplate();
        _box = GetTemplateChild("PART_TextBox") as TextBox;
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _list = GetTemplateChild("PART_List") as ListBox;
        _panel = GetTemplateChild("PART_Panel") as FrameworkElement;
        if (_box is not null)
        {
            _box.GotKeyboardFocus += OnBoxFocused;
            _box.LostKeyboardFocus += OnBoxLostFocus;
            _box.PreviewKeyDown += OnBoxKeyDown;
            _box.PreviewMouseLeftButtonDown += OnBoxMouseDown;
        }

        if (_list is not null)
        {
            _list.ItemsSource = Rows;
            _list.PreviewMouseLeftButtonDown += OnListMouseDown;
            _list.MouseMove += OnListMouseMove;
        }

        Rebuild();
    }

    /// <summary>Puts the caret in the box (Ctrl+K / Ctrl+F) with its text selected, and opens the panel.</summary>
    public void FocusSearch()
    {
        if (_box is null)
        {
            return;
        }

        _box.Focus();
        _box.SelectAll();
        IsDropDownOpen = true;
    }

    private void Clear()
    {
        Text = string.Empty;
        _box?.Focus();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var search = (QuickSearch)d;
        search.SetValue(HasTextPropertyKey, !string.IsNullOrEmpty(e.NewValue as string));
        search._highlight = -1;
        search.Rebuild();
        if (search._box?.IsKeyboardFocused == true)
        {
            search.IsDropDownOpen = true;
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var search = (QuickSearch)d;
        if (e.NewValue is true)
        {
            search.Rebuild();
            search.PlayOpen();
        }
        else
        {
            search._highlight = -1;
            search.UpdateHighlight();
        }
    }

    /// <summary>The panel eases in: a fade and a short slide down from the box.</summary>
    private void PlayOpen()
    {
        if (_panel is null)
        {
            return;
        }

        if (Motion.ReducedMotion)
        {
            _panel.BeginAnimation(OpacityProperty, null);
            _panel.Opacity = 1;
            return;
        }

        if (_panel.RenderTransform is not TranslateTransform slide || slide.IsFrozen)
        {
            slide = new TranslateTransform();
            _panel.RenderTransform = slide;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void Rebuild()
    {
        string text = (Text ?? string.Empty).Trim();
        var rows = new List<QuickSearchRow>();
        if (text.Length == 0)
        {
            rows.Add(new QuickSearchRow(QuickSearchRowKind.Hint, Hint, "Ctrl+K or Ctrl+F jumps here from anywhere on this page", "Icon.Search"));
        }
        else
        {
            var results = Suggestions?.OfType<SearchSuggestion>().ToList() ?? [];
            rows.Add(new QuickSearchRow(QuickSearchRowKind.Filter, $"Show all matches for “{text}”", "Filter the list on this page", "Icon.Search"));
            if (results.Count == 0)
            {
                rows.Add(new QuickSearchRow(QuickSearchRowKind.Empty, $"No matches for “{text}”", "Try another name or type", "Icon.Info"));
            }

            rows.AddRange(results.Select(r => new QuickSearchRow(QuickSearchRowKind.Result, r.Title, r.Detail, r.IconKey, r)));
        }

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        if (_highlight >= Rows.Count)
        {
            _highlight = -1;
        }

        UpdateHighlight();
    }

    private void Move(int by)
    {
        var choosable = Rows.Select((row, i) => (row, i)).Where(x => x.row.CanChoose).Select(x => x.i).ToList();
        if (choosable.Count == 0)
        {
            return;
        }

        int at = choosable.IndexOf(_highlight);
        at = at < 0 ? (by > 0 ? 0 : choosable.Count - 1) : Math.Clamp(at + by, 0, choosable.Count - 1);
        _highlight = choosable[at];
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        var row = _highlight >= 0 && _highlight < Rows.Count ? Rows[_highlight] : null;
        SetValue(HighlightedRowPropertyKey, row);
        if (_list is not null)
        {
            _list.SelectedItem = row;
            if (row is not null)
            {
                _list.ScrollIntoView(row);
            }
        }
    }

    private void Choose(QuickSearchRow? row)
    {
        row ??= Rows.FirstOrDefault(r => r.CanChoose);
        if (row is null || !row.CanChoose)
        {
            return;
        }

        if (row.Kind == QuickSearchRowKind.Result && row.Suggestion is { } suggestion)
        {
            Text = suggestion.Title;
            _box?.Select(_box.Text.Length, 0);
        }

        IsDropDownOpen = false;
    }

    private void OnBoxFocused(object sender, KeyboardFocusChangedEventArgs e) => IsDropDownOpen = true;

    private void OnBoxMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_box?.IsKeyboardFocused == true && !IsDropDownOpen)
        {
            IsDropDownOpen = true; // clicking the box again after Esc or a pick reopens the panel
        }
    }

    private void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Rows can't take focus, so focus only leaves the box when the user goes somewhere else.
        IsDropDownOpen = false;
    }

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                IsDropDownOpen = true;
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Choose(_highlight >= 0 && _highlight < Rows.Count ? Rows[_highlight] : null);
                e.Handled = true;
                break;
            case Key.Escape when IsDropDownOpen:
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Tab:
                IsDropDownOpen = false;
                break;
        }
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (RowAt(e.OriginalSource) is { } row && Rows.IndexOf(row) is int index && index != _highlight && row.CanChoose)
        {
            _highlight = index;
            UpdateHighlight();
        }
    }

    /// <summary>A row is picked on press, before anything the click does to focus can close the panel.</summary>
    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.OriginalSource) is { CanChoose: true } row)
        {
            Choose(row);
            e.Handled = true;
        }
    }

    private static QuickSearchRow? RowAt(object source)
    {
        for (var node = source as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ListBoxItem { DataContext: QuickSearchRow row })
            {
                return row;
            }
        }

        return null;
    }

    // ----------------------------------------------------------- window: shortcuts, closing when the window moves

    private void HookWindow(Window? window)
    {
        if (ReferenceEquals(window, _window))
        {
            return;
        }

        if (_window is not null)
        {
            _window.PreviewKeyDown -= OnWindowKeyDown;
            _window.Deactivated -= OnWindowDeactivated;
            _window.LocationChanged -= OnWindowDeactivated;
            _window.SizeChanged -= OnWindowDeactivated;
        }

        _window = window;
        if (_window is not null)
        {
            _window.PreviewKeyDown += OnWindowKeyDown;
            _window.Deactivated += OnWindowDeactivated;
            _window.LocationChanged += OnWindowDeactivated;
            _window.SizeChanged += OnWindowDeactivated;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => IsDropDownOpen = false;

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsVisible || Keyboard.Modifiers != ModifierKeys.Control || (e.Key != Key.K && e.Key != Key.F))
        {
            return;
        }

        FocusSearch();
        e.Handled = true;
    }
}
