using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coreloom.Mac.Input;
using Coreloom.ViewModels.Pages;

namespace Coreloom.Mac.Views;

// Code-behind for page views. Pages contain no logic beyond small input conveniences: everything is bound to the view models.

public partial class OverviewPage : UserControl
{
    private SystemSummaryViewModel? _specs;
    private bool _remeasureQueued;

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_specs is not null)
        {
            _specs.LeftSpecs.CollectionChanged -= OnSpecsChanged;
            _specs.RightSpecs.CollectionChanged -= OnSpecsChanged;
        }

        _specs = (DataContext as OverviewViewModel)?.Specs;
        if (_specs is not null)
        {
            _specs.LeftSpecs.CollectionChanged += OnSpecsChanged;
            _specs.RightSpecs.CollectionChanged += OnSpecsChanged;
        }
    }

    private void OnSpecsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRemeasure();

    /// <summary>
    /// The Mac's specs come in after the page has been laid out (Storage once the drives are known). Once they have,
    /// the header is measured again from the top, so the card is as tall as its specs at every window width.
    /// </summary>
    private void QueueRemeasure()
    {
        if (_remeasureQueued)
        {
            return;
        }

        _remeasureQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _remeasureQueued = false;
            Header.InvalidateMeasure();
            foreach (var layoutable in Header.GetVisualDescendants().OfType<Layoutable>())
            {
                layoutable.InvalidateMeasure();
            }
        }, DispatcherPriority.Background);
    }
}

public partial class GamingPage : UserControl
{
    private GamingViewModel? _page;

    public GamingPage()
    {
        InitializeComponent();
        EditorScrim.PointerPressed += (_, e) =>
        {
            if (DataContext is GamingViewModel page)
            {
                page.Editor.CancelCommand.Execute(null);
                e.Handled = true;
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (_page is not null)
            {
                _page.PropertyChanged -= OnPageChanged;
            }

            _page = DataContext as GamingViewModel;
            if (_page is not null)
            {
                _page.PropertyChanged += OnPageChanged;
            }

            ShowView();
        };
    }

    private void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamingViewModel.IsGridView) or null)
        {
            ShowView();
        }
    }

    /// <summary>
    /// The library as cards in a list or covers in a grid. The template is set before the panel: a new panel makes every
    /// item again, with the template now set.
    /// </summary>
    private void ShowView()
    {
        bool grid = _page?.IsGridView == true;
        var template = (Avalonia.Controls.Templates.IDataTemplate)Resources[grid ? "GameTile" : "GameCard"]!;
        var panel = (ITemplate<Panel?>)Resources[grid ? "GridPanel" : "ListPanel"]!;
        if (ReferenceEquals(Library.ItemTemplate, template) && ReferenceEquals(Library.ItemsPanel, panel))
        {
            return;
        }

        Library.ItemTemplate = template;
        Library.ItemsPanel = panel;
    }
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

    /// <summary>Return in a text box (an alert's threshold, a rule's name, the custom accent) applies what was typed.</summary>
    private void OnCommitOnEnter(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && sender is TextBox box)
        {
            Avalonia.Data.BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            e.Handled = true;
        }
    }
}
