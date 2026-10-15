using System.Text;

namespace Coreloom.Core.Gaming;

/// <summary>
/// One node of Valve's text "KeyValues" format (Steam's <c>libraryfolders.vdf</c> and <c>appmanifest_*.acf</c>): a key
/// with either a string value or child nodes. Key lookups ignore case, like Steam does.
/// </summary>
public sealed class KeyValues
{
    private readonly List<KeyValues> _children = [];

    public KeyValues(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>The value, or null when the node has children instead.</summary>
    public string? Value { get; }

    public IReadOnlyList<KeyValues> Children => _children;

    /// <summary>The first child with this key, or null.</summary>
    public KeyValues? this[string key] => _children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The string value of a child, or null.</summary>
    public string? Get(string key) => this[key]?.Value;

    public long? GetLong(string key) => long.TryParse(Get(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : null;

    internal void Add(KeyValues child) => _children.Add(child);

    /// <summary>Parses a text KeyValues document; returns its root node (null for empty or unreadable text).</summary>
    public static KeyValues? Parse(string text)
    {
        var tokens = new Tokenizer(text);
        var root = new KeyValues(string.Empty);
        ParseBody(tokens, root, topLevel: true);
        return root.Children.Count > 0 ? root.Children[0] : null;
    }

    private static void ParseBody(Tokenizer tokens, KeyValues parent, bool topLevel)
    {
        while (tokens.Next(out var token, out bool quoted))
        {
            if (!quoted && token == "}")
            {
                if (!topLevel)
                {
                    return;
                }

                continue;
            }

            if (!quoted && token == "{")
            {
                continue; // stray brace
            }

            string key = token;
            if (!tokens.Next(out var next, out bool nextQuoted))
            {
                parent.Add(new KeyValues(key, string.Empty));
                return;
            }

            if (!nextQuoted && next == "{")
            {
                var node = new KeyValues(key);
                ParseBody(tokens, node, topLevel: false);
                parent.Add(node);
            }
            else if (!nextQuoted && next == "}")
            {
                parent.Add(new KeyValues(key, string.Empty));
                if (!topLevel)
                {
                    return;
                }
            }
            else
            {
                parent.Add(new KeyValues(key, next));
            }

            tokens.SkipConditional();
        }
    }

    private sealed class Tokenizer(string text)
    {
        private int _at;

        public bool Next(out string token, out bool quoted)
        {
            SkipSpaceAndComments();
            token = string.Empty;
            quoted = false;
            if (_at >= text.Length)
            {
                return false;
            }

            char c = text[_at];
            if (c is '{' or '}')
            {
                _at++;
                token = c.ToString();
                return true;
            }

            if (c == '"')
            {
                quoted = true;
                _at++;
                var sb = new StringBuilder();
                while (_at < text.Length && text[_at] != '"')
                {
                    char ch = text[_at++];
                    if (ch == '\\' && _at < text.Length)
                    {
                        char e = text[_at++];
                        sb.Append(e switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            _ => e, // \\ and \" (and anything else) stand for the character itself
                        });
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }

                _at++; // closing quote
                token = sb.ToString();
                return true;
            }

            int start = _at;
            while (_at < text.Length && !char.IsWhiteSpace(text[_at]) && text[_at] is not ('{' or '}' or '"'))
            {
                _at++;
            }

            token = text[start.._at];
            return true;
        }

        /// <summary>Skips a platform condition such as <c>[$WIN32]</c> after a value.</summary>
        public void SkipConditional()
        {
            int save = _at;
            while (_at < text.Length && text[_at] is ' ' or '\t')
            {
                _at++;
            }

            if (_at < text.Length && text[_at] == '[')
            {
                int end = text.IndexOf(']', _at);
                _at = end < 0 ? text.Length : end + 1;
            }
            else
            {
                _at = save;
            }
        }

        private void SkipSpaceAndComments()
        {
            while (_at < text.Length)
            {
                if (char.IsWhiteSpace(text[_at]))
                {
                    _at++;
                }
                else if (text[_at] == '/' && _at + 1 < text.Length && text[_at + 1] == '/')
                {
                    int end = text.IndexOf('\n', _at);
                    _at = end < 0 ? text.Length : end + 1;
                }
                else
                {
                    return;
                }
            }
        }
    }
}
