using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace Coreloom.PageShots;

/// <summary>
/// Since 1.8.6: after each shot, looks for icon buttons (the × buttons among them) that an ancestor cuts off — a scroll
/// area, a card that clips its content, a clip — and writes what it found to clip-check.txt. A button scrolled fully
/// out of view isn't counted; one that's partly outside its clip is.
/// </summary>
internal static class ClipCheck
{
    public static string? Folder { get; set; }

    public static void Run(FrameworkElement page, string shot)
    {
        if (Folder is null)
        {
            return;
        }

        string? close = (Application.Current.TryFindResource("Icon.Close") as Geometry)?.ToString();
        var iconStyle = Application.Current.TryFindResource("Button.Icon") as Style;
        var lines = new List<string>();
        int checkedButtons = 0;
        foreach (var button in Descendants(page).OfType<Button>())
        {
            if (!button.IsVisible || button.ActualWidth < 1 || button.ActualHeight < 1)
            {
                continue;
            }

            bool isX = close is not null && Descendants(button).OfType<ShapePath>().Any(p => p.Data?.ToString() == close);
            if (!isX && !IsBasedOn(button.Style, iconStyle))
            {
                continue;
            }

            checkedButtons++;
            var bounds = new Rect(0, 0, button.ActualWidth, button.ActualHeight);
            for (DependencyObject? parent = VisualTreeHelper.GetParent(button); parent is Visual visual; parent = VisualTreeHelper.GetParent(parent))
            {
                var clip = VisualTreeHelper.GetClip(visual)?.Bounds;
                if (clip is null && visual is FrameworkElement { ClipToBounds: true } clipping)
                {
                    clip = new Rect(0, 0, clipping.ActualWidth, clipping.ActualHeight);
                }

                if (clip is not { } area || area.IsEmpty)
                {
                    if (ReferenceEquals(visual, page))
                    {
                        break;
                    }

                    continue;
                }

                Rect rect;
                try
                {
                    rect = button.TransformToAncestor(visual).TransformBounds(bounds);
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                var inside = Rect.Intersect(area, rect);
                bool partly = !inside.IsEmpty && (inside.Width < rect.Width - 0.75 || inside.Height < rect.Height - 0.75);
                if (partly)
                {
                    string what = isX ? "×" : "icon";
                    string name = System.Windows.Automation.AutomationProperties.GetName(button) is { Length: > 0 } n ? n : button.ToolTip as string ?? "(unnamed)";
                    lines.Add($"{shot}: the {what} button \"{name}\" is cut by a {visual.GetType().Name}: {Math.Round(rect.Width - inside.Width, 1)} × {Math.Round(rect.Height - inside.Height, 1)} px outside");
                    break;
                }

                if (ReferenceEquals(visual, page))
                {
                    break;
                }
            }
        }

        lines.Insert(0, $"{shot}: {checkedButtons} icon buttons checked, {lines.Count} cut off");
        File.AppendAllLines(System.IO.Path.Combine(Folder, "clip-check.txt"), lines);
    }

    private static bool IsBasedOn(Style? style, Style? target)
    {
        for (var s = style; s is not null; s = s.BasedOn)
        {
            if (ReferenceEquals(s, target))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }
}
