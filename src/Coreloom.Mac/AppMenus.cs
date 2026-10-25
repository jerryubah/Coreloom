using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Coreloom.Core.Diagnostics;
using Coreloom.Mac.Native;
using Coreloom.Mac.Services;
using Coreloom.Mac.Views;
using Coreloom.ViewModels;

namespace Coreloom.Mac;

/// <summary>
/// The menu bar menus every Mac app has. The app menu: About Coreloom, Settings… (⌘,), Check for Updates…, Hide (⌘H),
/// Hide Others (⌥⌘H), Show All and Quit (⌘Q). With the window in front: View (the pages, ⌘1–⌘9, the sidebar, pausing),
/// Window (Minimize ⌘M, Zoom, Close ⌘W, Enter Full Screen ⌃⌘F) and Help.
/// </summary>
internal static class AppMenus
{
    public sealed record Actions(Action ShowWindow, Action OpenSettings, Action CheckForUpdates, Action Quit);

    public static void Install(Application app, MainWindow window, ShellViewModel shell, Actions actions)
    {
        var appMenu = new NativeMenu();
        appMenu.Add(Item("About Coreloom", About));
        appMenu.Add(new NativeMenuItemSeparator());
        appMenu.Add(Item("Settings…", actions.OpenSettings, new KeyGesture(Key.OemComma, KeyModifiers.Meta)));
        appMenu.Add(Item("Check for Updates…", actions.CheckForUpdates));
        appMenu.Add(new NativeMenuItemSeparator());
        appMenu.Add(Item("Hide Coreloom", AppKit.Hide, new KeyGesture(Key.H, KeyModifiers.Meta)));
        appMenu.Add(Item("Hide Others", AppKit.HideOthers, new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt)));
        appMenu.Add(Item("Show All", AppKit.UnhideAll));
        appMenu.Add(new NativeMenuItemSeparator());
        appMenu.Add(Item("Quit Coreloom", actions.Quit, new KeyGesture(Key.Q, KeyModifiers.Meta)));
        NativeMenu.SetMenu(app, appMenu);

        var view = new NativeMenu();
        int number = 1;
        foreach (var page in shell.Pages)
        {
            var item = Item(page.Title, () =>
            {
                shell.Navigate(page);
                actions.ShowWindow();
            }, number <= 9 ? new KeyGesture(Key.D0 + number, KeyModifiers.Meta) : null);
            item.IsEnabled = page.IsEnabled;
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(page.IsEnabled))
                {
                    item.IsEnabled = page.IsEnabled;
                }
            };
            view.Add(item);
            number++;
        }

        view.Add(new NativeMenuItemSeparator());
        var sidebar = Item(shell.IsSidebarCollapsed ? "Show Sidebar" : "Hide Sidebar", () => shell.ToggleSidebarCommand.Execute(null),
            new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Control));
        var pause = Item(shell.IsMonitoringPaused ? "Resume Monitoring" : "Pause Monitoring", () => shell.IsMonitoringPaused = !shell.IsMonitoringPaused);
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.IsSidebarCollapsed))
            {
                sidebar.Header = shell.IsSidebarCollapsed ? "Show Sidebar" : "Hide Sidebar";
            }
            else if (e.PropertyName == nameof(ShellViewModel.IsMonitoringPaused))
            {
                pause.Header = shell.IsMonitoringPaused ? "Resume Monitoring" : "Pause Monitoring";
            }
        };
        view.Add(sidebar);
        view.Add(pause);

        var windowMenu = new NativeMenu();
        windowMenu.Add(Item("Minimize", () => window.WindowState = WindowState.Minimized, new KeyGesture(Key.M, KeyModifiers.Meta)));
        windowMenu.Add(Item("Zoom", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        windowMenu.Add(Item("Close Window", window.Close, new KeyGesture(Key.W, KeyModifiers.Meta)));
        windowMenu.Add(new NativeMenuItemSeparator());
        windowMenu.Add(Item("Enter Full Screen", () => window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen,
            new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)));

        var help = new NativeMenu();
        string repository = shell.Updates.RepositoryUrl ?? "https://github.com/jerryubah/Coreloom";
        help.Add(Item("Coreloom Help", () => Commands.Start("/usr/bin/open", repository + "#readme")));
        help.Add(Item("What's New in Coreloom", () =>
        {
            shell.ShowWhatsNewCommand.Execute(null);
            actions.ShowWindow();
        }));
        help.Add(Item("Report an Issue…", () => Commands.Start("/usr/bin/open", repository + "/issues/new/choose")));
        help.Add(new NativeMenuItemSeparator());
        help.Add(Item("Show Log Folder", () => shell.Settings.OpenLogsCommand.Execute(null)));

        var bar = new NativeMenu();
        bar.Add(new NativeMenuItem("View") { Menu = view });
        bar.Add(new NativeMenuItem("Window") { Menu = windowMenu });
        bar.Add(new NativeMenuItem("Help") { Menu = help });
        NativeMenu.SetMenu(window, bar);
    }

    /// <summary>The standard About panel (name, version, icon and copyright from the app's Info.plist).</summary>
    private static void About()
    {
        AppKit.Activate();
        AppKit.OrderFrontAbout();
    }

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
                Log.Error("App", $"The menu item \"{header}\" failed.", e);
            }
        };
        return item;
    }
}
