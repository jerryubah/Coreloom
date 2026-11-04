using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Mac.Behaviors;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.Mac.Controls;

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
public sealed class QuickSearchRow(QuickSearchRowKind kind, string title, string? detail, string iconKey, SearchSuggestion? suggestion = null)
{
    public QuickSearchRowKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string? Detail { get; } = detail;

    public string IconKey { get; } = iconKey;

    public SearchSuggestion? Suggestion { get; } = suggestion;

    public bool CanChoose => Kind is QuickSearchRowKind.Filter or QuickSearchRowKind.Result;

    public bool IsFilter => Kind == QuickSearchRowKind.Filter;

    public bool IsQuiet => Kind is QuickSearchRowKind.Hint or QuickSearchRowKind.Empty;
}

/// <summary>
/// A search box that answers as you type. Clicking it (or ⌘K / ⌘F on its page) drops a panel down from it: "Start typing
/// to search" at first, then "Show all matches for …" and the best few results. Up and Down move through the rows,
/// Return picks one (a result fills the box with its name, so the page shows just that), Esc closes the panel and ×
/// clears the search. The page filters as you type.
/// </summary>
[TemplatePart("PART_TextBox", typeof(TextBox))]
[TemplatePart("PART_Popup", typeof(Popup))]
[TemplatePart("PART_List", typeof(ListBox))]
[TemplatePart("PART_Panel", typeof(Control))]
[PseudoClasses(":hastext", ":searching")]
public sealed class QuickSearch : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<QuickSearch, string?>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<QuickSearch, string?>(nameof(Placeholder), "Search");

    public static readonly StyledProperty<string?> HintProperty = AvaloniaProperty.Register<QuickSearch, string?>(nameof(Hint), "Start typing to search");

    public static readonly StyledProperty<IEnumerable?> SuggestionsProperty = AvaloniaProperty.Register<QuickSearch, IEnumerable?>(nameof(Suggestions));

    public static readonly StyledProperty<bool> IsDropDownOpenProperty = AvaloniaProperty.Register<QuickSearch, bool>(nameof(IsDropDownOpen));

    private TextBox? _box;
    private ListBox? _list;
    private Control? _panel;
    private TopLevel? _topLevel;
    private int _highlight = -1;

    public QuickSearch()
    {
        Rows = [];
        Focusable = false;
        ClearCommand = new RelayCommand(Clear);
        AttachedToVisualTree += (_, _) => HookWindow(TopLevel.GetTopLevel(this));
        DetachedFromVisualTree += (_, _) =>
        {
            HookWindow(null);
            IsDropDownOpen = false;
        };
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>The results for the current text (<see cref="SearchSuggestion"/>s from the page's view model).</summary>
    public IEnumerable? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>The × button.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>The panel's rows.</summary>
    public ObservableCollection<QuickSearchRow> Rows { get; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_box is not null)
        {
            _box.GotFocus -= OnBoxFocused;
            _box.LostFocus -= OnBoxLostFocus;
            _box.RemoveHandler(KeyDownEvent, OnBoxKeyDown);
            _box.RemoveHandler(PointerPressedEvent, OnBoxPointerPressed);
            _box.PropertyChanged -= OnBoxPropertyChanged;
        }

        if (_list is not null)
        {
            _list.RemoveHandler(PointerPressedEvent, OnListPointerPressed);
            _list.RemoveHandler(PointerMovedEvent, OnListPointerMoved);
        }

        base.OnApplyTemplate(e);
        _box = e.NameScope.Find<TextBox>("PART_TextBox");
        _list = e.NameScope.Find<ListBox>("PART_List");
        _panel = e.NameScope.Find<Control>("PART_Panel");
        if (_box is not null)
        {
            _box.GotFocus += OnBoxFocused;
            _box.LostFocus += OnBoxLostFocus;
            _box.AddHandler(KeyDownEvent, OnBoxKeyDown, RoutingStrategies.Tunnel);
            _box.AddHandler(PointerPressedEvent, OnBoxPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _box.PropertyChanged += OnBoxPropertyChanged;
        }

        if (_list is not null)
        {
            _list.ItemsSource = Rows;
            _list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
            _list.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
        }

        Rebuild();
    }

    /// <summary>Puts the caret in the box (⌘K / ⌘F) with its text selected, and opens the panel.</summary>
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            PseudoClasses.Set(":hastext", !string.IsNullOrEmpty(change.NewValue as string));
            _highlight = -1;
            Rebuild();
            if (_box?.IsFocused == true)
            {
                IsDropDownOpen = true;
            }
        }
        else if (change.Property == HintProperty || change.Property == SuggestionsProperty)
        {
            Rebuild();
        }
        else if (change.Property == IsDropDownOpenProperty)
        {
            if (change.NewValue is true)
            {
                Rebuild();
                PlayOpen();
            }
            else
            {
                _highlight = -1;
                UpdateHighlight();
            }
        }
    }

    private void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsFocusedProperty)
        {
            PseudoClasses.Set(":searching", e.NewValue is true);
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
            _panel.Opacity = 1;
            return;
        }

        if (_panel.RenderTransform is not TranslateTransform slide)
        {
            slide = new TranslateTransform();
            _panel.RenderTransform = slide;
        }

        var panel = _panel;
        Animator.Run(TimeSpan.FromMilliseconds(150), Ease.Linear, t => panel.Opacity = t);
        Animator.Run(TimeSpan.FromMilliseconds(220), Ease.CubicOut, t => slide.Y = -6 * (1 - t));
    }

    private void Rebuild()
    {
        string text = (Text ?? string.Empty).Trim();
        var rows = new List<QuickSearchRow>();
        if (text.Length == 0)
        {
            rows.Add(new QuickSearchRow(QuickSearchRowKind.Hint, Hint ?? string.Empty, null, "Icon.Search"));
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
            if (_box is not null)
            {
                _box.CaretIndex = _box.Text?.Length ?? 0;
                _box.ClearSelection();
            }
        }

        IsDropDownOpen = false;
    }

    private void OnBoxFocused(object? sender, GotFocusEventArgs e) => IsDropDownOpen = true;

    private void OnBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_box?.IsFocused == true && !IsDropDownOpen)
        {
            IsDropDownOpen = true; // clicking the box again after Esc or a pick reopens the panel
        }
    }

    private void OnBoxLostFocus(object? sender, RoutedEventArgs e) => IsDropDownOpen = false;

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
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

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (RowAt(e.Source) is { } row && Rows.IndexOf(row) is int index && index != _highlight && row.CanChoose)
        {
            _highlight = index;
            UpdateHighlight();
        }
    }

    /// <summary>A row is picked on press, before anything the click does to focus can close the panel.</summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RowAt(e.Source) is { CanChoose: true } row)
        {
            Choose(row);
            e.Handled = true;
        }
    }

    private static QuickSearchRow? RowAt(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is ListBoxItem { DataContext: QuickSearchRow row })
            {
                return row;
            }
        }

        return null;
    }

    // ----------------------------------------------------------- window: shortcuts, closing when the window moves

    private void HookWindow(TopLevel? topLevel)
    {
        if (ReferenceEquals(topLevel, _topLevel))
        {
            return;
        }

        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
            if (_topLevel is Window old)
            {
                old.Deactivated -= OnWindowDeactivated;
                old.PositionChanged -= OnWindowMoved;
                old.Resized -= OnWindowResized;
            }
        }

        _topLevel = topLevel;
        if (_topLevel is not null)
        {
            _topLevel.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            if (_topLevel is Window window)
            {
                window.Deactivated += OnWindowDeactivated;
                window.PositionChanged += OnWindowMoved;
                window.Resized += OnWindowResized;
            }
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => IsDropDownOpen = false;

    private void OnWindowMoved(object? sender, PixelPointEventArgs e) => IsDropDownOpen = false;

    private void OnWindowResized(object? sender, WindowResizedEventArgs e) => IsDropDownOpen = false;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool command = e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control;
        if (e.Handled || !IsEffectivelyVisible || !command || (e.Key != Key.K && e.Key != Key.F))
        {
            return;
        }

        FocusSearch();
        e.Handled = true;
    }
}
