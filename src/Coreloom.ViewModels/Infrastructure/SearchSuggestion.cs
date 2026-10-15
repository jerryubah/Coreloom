namespace Coreloom.ViewModels.Infrastructure;

/// <summary>A result under a search box: what it is, a detail line and its icon.</summary>
public sealed record SearchSuggestion(string Title, string Detail, string IconKey) : StaticRecord;

/// <summary>Picks and orders the results a search box shows while typing.</summary>
public static class SearchSuggestions
{
    /// <summary>How many results the box lists (the page itself shows every match).</summary>
    public const int Limit = 6;

    /// <summary>
    /// The items whose name (or other text) matches, best first: names that start with the search, then names with a word
    /// starting with it, then the rest; alphabetical within each. Empty for an empty search. Titles are not repeated.
    /// </summary>
    public static IReadOnlyList<SearchSuggestion> Rank<T>(IEnumerable<T> items, string search, Func<T, string> name, Func<T, bool> matches, Func<T, SearchSuggestion> suggest)
    {
        search = search.Trim();
        if (search.Length == 0)
        {
            return [];
        }

        return items
            .Where(matches)
            .Select(item => (Item: item, Name: name(item)))
            .OrderBy(x => Score(x.Name, search))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => suggest(x.Item))
            .DistinctBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Limit)
            .ToList();
    }

    /// <summary>0 when the name starts with the search, 1 when one of its words does, 2 otherwise.</summary>
    public static int Score(string name, string search)
    {
        if (name.StartsWith(search, StringComparison.CurrentCultureIgnoreCase))
        {
            return 0;
        }

        int at = name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase);
        return at > 0 && !char.IsLetterOrDigit(name[at - 1]) ? 1 : 2;
    }
}
