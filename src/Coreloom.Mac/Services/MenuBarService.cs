using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Mac.Services;

/// <summary>
/// Coreloom's item in the menu bar: a monochrome template icon (macOS tints it for light and dark menu bars) and a menu
/// with live readings at the top (CPU, GPU and memory, as in the Windows tray tooltip), then Open Coreloom,
/// Pause/Resume monitoring, Settings, Check for Updates, Install update (when one is ready) and Quit.
/// </summary>
public sealed class MenuBarService : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly NativeMenu _menu;
    private readonly NativeMenuItem _pauseItem;
    private readonly NativeMenuItem _updateItem;
    private readonly NativeMenuItemSeparator _updateSeparator;
    private readonly List<NativeMenuItem> _readings = [];
    private readonly NativeMenuItemSeparator _readingsSeparator;
    private string _text = string.Empty;

    public MenuBarService(Action open, Action togglePause, Action openSettings, Action checkUpdates, Action installUpdate, Action quit)
    {
        _menu = new NativeMenu();
        _readingsSeparator = new NativeMenuItemSeparator();
        _updateItem = Item("Install update", installUpdate);
        _updateItem.IsVisible = false;
        _updateSeparator = new NativeMenuItemSeparator { IsVisible = false };
        _pauseItem = Item("Pause Monitoring", togglePause);

        _menu.Add(_readingsSeparator);
        _menu.Add(_updateItem);
        _menu.Add(_updateSeparator);
        _menu.Add(Item("Open Coreloom", open));
        _menu.Add(_pauseItem);
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(Item("Settings…", openSettings));
        _menu.Add(Item("Check for Updates…", checkUpdates));
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(Item("Quit Coreloom", quit, new KeyGesture(Key.Q, KeyModifiers.Meta)));

        _icon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Coreloom",
            Menu = _menu,
            IsVisible = true,
        };
        MacOSProperties.SetIsTemplateIcon(_icon, true);
        var icons = new TrayIcons { _icon };
        if (Application.Current is { } app)
        {
            TrayIcon.SetIcons(app, icons);
        }

        SetReadings("Coreloom");
    }

    /// <summary>The live readings: "Coreloom\nCPU 12 % · 48 °C\nGPU 3 %\nRAM 61 %" (the first line, the app's name, isn't repeated).</summary>
    public void SetReadings(string text)
    {
        if (text == _text)
        {
            return;
        }

        _text = text;
        _icon.ToolTipText = text.Replace('\n', ' ').Replace("Coreloom ", "Coreloom — ", StringComparison.Ordinal);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(MacWording).ToList();
        while (_readings.Count < lines.Count)
        {
            var item = new NativeMenuItem { IsEnabled = false };
            _menu.Items.Insert(_readings.Count, item);
            _readings.Add(item);
        }

        for (int i = 0; i < _readings.Count; i++)
        {
            bool shown = i < lines.Count;
            _readings[i].IsVisible = shown;
            if (shown && _readings[i].Header != lines[i])
            {
                _readings[i].Header = lines[i];
            }
        }

        _readingsSeparator.IsVisible = lines.Count > 0;
    }

    public void SetPaused(bool paused) => _pauseItem.Header = paused ? "Resume Monitoring" : "Pause Monitoring";

    /// <summary>Shows "Install update to 1.7.0" at the top of the menu, or hides it (null).</summary>
    public void SetUpdateItem(string? text)
    {
        _updateItem.IsVisible = text is not null;
        _updateSeparator.IsVisible = text is not null;
        if (text is not null)
        {
            _updateItem.Header = text;
        }
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }

    /// <summary>The tray text says "RAM"; a Mac says memory.</summary>
    internal static string MacWording(string line) => line.StartsWith("RAM ", StringComparison.Ordinal) ? "Memory " + line[4..] : line;

    private static NativeMenuItem Item(string header, Action action, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Error("App", $"The menu bar item \"{header}\" failed.", e);
            }
        };
        return item;
    }

    private static WindowIcon LoadIcon()
    {
        // 36 px for an 18-point menu bar icon: sharp on Retina screens, and macOS scales it for the others.
        using var stream = AssetLoader.Open(new Uri("avares://Coreloom/Assets/menubar-template@2x.png"));
        return new WindowIcon(stream);
    }
}
