namespace Coreloom.Core.Settings;

/// <summary>A card's smallest and default size on the dashboard, in grid columns and rows.</summary>
public readonly record struct DashboardCardSize(int MinColumns, int MinRows, int DefaultColumns, int DefaultRows);

/// <summary>Known Overview cards, their default order, sizes and display names.</summary>
public static class DashboardCatalog
{
    /// <summary>The dashboard is at most six columns wide (fewer in a narrow window).</summary>
    public const int MaxColumns = 6;

    /// <summary>The tallest a card can be made, in rows.</summary>
    public const int MaxRows = 8;

    public const string GpuLoad = "gpu-load";
    public const string GpuTemperature = "gpu-temp";
    public const string CpuLoad = "cpu-load";
    public const string CpuTemperature = "cpu-temp";
    public const string Memory = "ram";
    public const string GpuPower = "gpu-power";
    public const string Health = "health";
    public const string LoadChart = "chart-load";
    public const string TemperatureChart = "chart-temps";
    public const string PowerChart = "chart-power";
    public const string MemoryDonut = "memory-donut";
    public const string StorageDonut = "storage-donut";
    public const string Network = "network";

    public static IReadOnlyList<(string Id, string Name, bool IsSummaryCard)> All { get; } =
    [
        (GpuLoad, "GPU utilization", true),
        (GpuTemperature, "GPU temperature", true),
        (CpuLoad, "CPU utilization", true),
        (CpuTemperature, "CPU temperature", true),
        (Memory, "RAM utilization", true),
        (GpuPower, "GPU power draw", true),
        (Health, "System health", false),
        (LoadChart, "Processor & graphics load chart", false),
        (TemperatureChart, "Temperatures chart", false),
        (PowerChart, "Power chart", false),
        (MemoryDonut, "Memory breakdown", false),
        (StorageDonut, "Storage", false),
        (Network, "Network", false),
    ];

    public static string NameOf(string id) => All.FirstOrDefault(c => c.Id == id).Name ?? id;

    public static bool IsSummary(string id) => All.FirstOrDefault(c => c.Id == id).IsSummaryCard;

    /// <summary>
    /// Cards as tall as their content (since 1.8.2: System health): on rows of their own they wrap what they say, and
    /// beside a taller card they stretch to its height and spread their content out.
    /// </summary>
    public static bool FitsContent(string id) => id == Health;

    /// <summary>
    /// Summary cards are one column by two rows and panels (charts, memory, storage, network) two columns by three, so six
    /// cards fill a wide row and three panels each row under them. A row is about half a summary card tall.
    /// </summary>
    public static DashboardCardSize SizeOf(string id) => id == Health
        ? new(2, 2, MaxColumns, 2) // a strip across the dashboard
        : IsSummary(id) ? new(1, 2, 1, 2) : new(2, 3, 2, 3);

    /// <summary>The card's size: what was set (kept within its limits), or its default.</summary>
    public static (int Columns, int Rows) SizeOf(DashboardCardSetting card)
    {
        var size = SizeOf(card.Id);
        return (Math.Clamp(card.Columns ?? size.DefaultColumns, size.MinColumns, MaxColumns),
                Math.Clamp(card.Rows ?? size.DefaultRows, size.MinRows, MaxRows));
    }

    /// <summary>What to store for a size: nothing when it is the default, so the default can change later.</summary>
    public static (int? Columns, int? Rows) Stored(string id, int columns, int rows)
    {
        var size = SizeOf(id);
        columns = Math.Clamp(columns, size.MinColumns, MaxColumns);
        rows = Math.Clamp(rows, size.MinRows, MaxRows);
        return (columns == size.DefaultColumns ? null : columns, rows == size.DefaultRows ? null : rows);
    }

    public static List<DashboardCardSetting> Defaults() =>
        All.Select(c => new DashboardCardSetting { Id = c.Id, Visible = true }).ToList();

    /// <summary>Removes unknown/duplicate ids, keeps sizes within limits and adds any missing known cards (visible).</summary>
    public static List<DashboardCardSetting> Normalize(List<DashboardCardSetting>? cards)
    {
        var known = All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DashboardCardSetting>();
        foreach (var card in cards ?? [])
        {
            if (card is null || string.IsNullOrEmpty(card.Id) || !known.Contains(card.Id) || !seen.Add(card.Id))
            {
                continue;
            }

            var (columns, rows) = SizeOf(card);
            var (storedColumns, storedRows) = Stored(card.Id, columns, rows);
            result.Add(new DashboardCardSetting { Id = card.Id, Visible = card.Visible, Columns = storedColumns, Rows = storedRows });
        }

        // Cards added in a later version go right after the card before them in the catalog (so a new card lands where
        // it would be on a fresh dashboard), or at the end.
        string? previous = null;
        foreach (var (id, _, _) in All)
        {
            if (seen.Add(id))
            {
                int after = previous is null ? -1 : result.FindIndex(c => c.Id == previous);
                var card = new DashboardCardSetting { Id = id, Visible = true };
                if (after >= 0)
                {
                    result.Insert(after + 1, card);
                }
                else
                {
                    result.Add(card);
                }
            }

            previous = id;
        }

        return result;
    }
}
