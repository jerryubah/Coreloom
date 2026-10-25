using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;


namespace Coreloom.Mac.Diagnostics;

/// <summary>TEMPORARY (probe branches only): Avalonia's layout warnings in Coreloom's log, and the Overview header's
/// sizes 15 s after the page opens.</summary>
internal static class LayoutProbe
{
    public static void Install() => Logger.Sink = new Sink();

    public static void Watch(Control page)
    {
        page.Loaded += (_, _) => DispatcherTimer.RunOnce(() => Dump(page), TimeSpan.FromSeconds(15));
    }

    private static void Dump(Control page)
    {
        var header = page.GetVisualDescendants().OfType<Controls.SquircleBorder>().FirstOrDefault();
        if (header is null)
        {
            Coreloom.Core.Diagnostics.Log.Info("Probe", "no header");
            return;
        }

        var lines = new List<string>();
        lines.Add("Ancestors:");
        for (var v = header.GetVisualParent(); v is not null; v = v.GetVisualParent())
        {
            string layout = v is Layoutable l ? $" desired={l.DesiredSize} measureValid={l.IsMeasureValid} arrangeValid={l.IsArrangeValid}" : string.Empty;
            lines.Add($"  {v.GetType().Name} bounds={v.Bounds}{layout}");
        }

        Walk(header, 0, lines);
        Coreloom.Core.Diagnostics.Log.Info("Probe", "Overview header layout:\n" + string.Join("\n", lines));
    }

    private static void Walk(Visual visual, int depth, List<string> lines)
    {
        if (depth > 9 || lines.Count > 120)
        {
            return;
        }

        string extra = visual is TextBlock t ? $" \"{t.Text}\"" : string.Empty;
        string layout = visual is Layoutable l
            ? $" desired={l.DesiredSize} measureValid={l.IsMeasureValid} arrangeValid={l.IsArrangeValid}"
            : string.Empty;
        string rows = visual is Grid g ? " rows=[" + string.Join(",", g.RowDefinitions.Select(r => r.ActualHeight.ToString("0.#"))) + "]" : string.Empty;
        lines.Add($"{new string(' ', depth * 2)}{visual.GetType().Name} bounds={visual.Bounds}{layout}{rows}{extra}");
        foreach (var child in visual.GetVisualChildren())
        {
            Walk(child, depth + 1, lines);
        }
    }

    private sealed class Sink : Avalonia.Logging.ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Write(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Write(level, area, source, messageTemplate, propertyValues);

        private static void Write(LogEventLevel level, string area, object? source, string template, object?[] values)
        {
            string message = template;
            foreach (var value in values)
            {
                int start = message.IndexOf('{');
                int end = start >= 0 ? message.IndexOf('}', start) : -1;
                if (end < 0)
                {
                    break;
                }

                message = message[..start] + value + message[(end + 1)..];
            }

            Coreloom.Core.Diagnostics.Log.WarnThrottled(area + message, TimeSpan.FromSeconds(30), "Avalonia", $"{level} {area} ({source?.GetType().Name}): {message}");
        }
    }
}
