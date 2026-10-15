using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Coreloom.App.Themes;
using Coreloom.Core.Diagnostics;

namespace Coreloom.App.Services;

/// <summary>
/// Notification-area icon with a themed WPF context menu (Install update when a new version is out, Open,
/// Pause/Resume, Settings, Check for updates, Exit) and balloon notifications (shown as toasts on Windows 10/11). Uses WinForms' NotifyIcon, which
/// handles Explorer restarts and DPI changes reliably.
/// </summary>
public sealed partial class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pauseItem;
    private readonly MenuItem _updateItem;
    private readonly Separator _updateSeparator;
    private Action? _balloonAction;

    /// <summary>Sizes of the supplied Coreloom PNGs that suit the notification area (16 px at 100 % scaling).</summary>
    private static readonly int[] TrayIconSizes = [16, 24, 32, 48];
    private bool _dark;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    public TrayIconService(Action open, Action togglePause, Action openSettings, Action checkUpdates, Action installUpdate, Action exit)
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(dark: false),
            Text = "Coreloom",
            Visible = true,
        };

        _menu = new ContextMenu();
        _updateItem = CreateItem("Install update", installUpdate, bold: true);
        _updateItem.Visibility = Visibility.Collapsed;
        _updateSeparator = new Separator { Visibility = Visibility.Collapsed };
        _menu.Items.Add(_updateItem);
        _menu.Items.Add(_updateSeparator);
        _menu.Items.Add(CreateItem("Open dashboard", open, bold: true));
        _pauseItem = CreateItem("Pause monitoring", togglePause);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CreateItem("Settings", openSettings));
        _menu.Items.Add(CreateItem("Check for updates", checkUpdates));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CreateItem("Exit Coreloom", exit));

        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                ShowMenu();
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                open();
            }
        };
        _icon.BalloonTipClicked += (_, _) => (_balloonAction ?? open)();
    }

    public void SetTooltip(string text)
    {
        // NotifyIcon.Text is limited to 127 characters.
        string trimmed = string.IsNullOrWhiteSpace(text) ? "Coreloom" : text.Length > 127 ? text[..127] : text;
        if (_icon.Text != trimmed)
        {
            _icon.Text = trimmed;
        }
    }

    public void SetPaused(bool paused) => _pauseItem.Header = paused ? "Resume monitoring" : "Pause monitoring";

    /// <summary>Shows "Install update" at the top of the menu (e.g. "Install update to 1.2.0"), or hides it when null.</summary>
    public void SetUpdateItem(string? header)
    {
        var visibility = header is null ? Visibility.Collapsed : Visibility.Visible;
        _updateItem.Header = header ?? "Install update";
        _updateItem.Visibility = visibility;
        _updateSeparator.Visibility = visibility;
    }

    public void Notify(string title, string message, bool warning = false, Action? onClick = null)
    {
        try
        {
            _balloonAction = onClick;
            _icon.ShowBalloonTip(8000, title, message, warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn("Tray", "Showing a notification failed.", ex);
        }
    }

    private void ShowMenu()
    {
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
        // Bring the menu's popup to the foreground so clicking elsewhere closes it.
        if (PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            SetForegroundWindow(source.Handle);
        }
    }

    private static MenuItem CreateItem(string header, Action action, bool bold = false)
    {
        var item = new MenuItem { Header = header };
        if (bold)
        {
            item.FontWeight = FontWeights.SemiBold;
        }

        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// The Coreloom icon at the size the notification area needs at the current DPI. Uses the supplied PNG that is
    /// the smallest one at least as large as the requested size, so the shell scales down rather than up.
    /// </summary>
    /// <summary>Shows the dark-mode icon (dark glass tile) or the light one, following the app's theme.</summary>
    public void SetDark(bool dark)
    {
        if (_dark == dark)
        {
            return;
        }

        _dark = dark;
        var previous = _icon.Icon;
        _icon.Icon = LoadIcon(dark);
        previous?.Dispose();   // NotifyIcon does not own the icon it shows
    }

    private static System.Drawing.Icon LoadIcon(bool dark)
    {
        try
        {
            int wanted = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
            int size = TrayIconSizes.FirstOrDefault(s => s >= wanted);
            if (size == 0)
            {
                size = TrayIconSizes[^1];
            }

            var resource = Application.GetResourceStream(new Uri(AppIcons.Png(dark, size), UriKind.Absolute));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var bitmap = new System.Drawing.Bitmap(stream);
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using var temporary = System.Drawing.Icon.FromHandle(handle);
                    return (System.Drawing.Icon)temporary.Clone();   // an independent copy that owns its own handle
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Tray", "Loading the tray icon failed; using the application icon.", ex);
        }

        return System.Drawing.Icon.ExtractAssociatedIcon(LifetimeService.ExecutablePath) ?? System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        var icon = _icon.Icon;
        _icon.Dispose();
        icon?.Dispose();   // NotifyIcon does not own the icon it shows
    }
}
