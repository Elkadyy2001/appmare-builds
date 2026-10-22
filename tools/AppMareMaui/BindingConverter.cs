using System.Globalization;
using System.Text.Json;

namespace AppMare_Maui.Services;

/// <summary>
/// Thin JSON-path binding converter for generated apps.
/// Supports simple paths, nested paths, and array indexing.
/// All logic stays in event actions — this only resolves display values.
/// </summary>
public class BindingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string path || string.IsNullOrEmpty(path)) return value;
        if (value is JsonElement je) return Walk(je, path);
        return null;
    }

    /// <summary>
    /// Converts a user-entered value back to a JsonElement so two-way
    /// bindings (Entry, Switch, Slider, etc.) round-trip correctly.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return default(JsonElement);
        try
        {
            var ctx = AppSerializationContext.Current;
            if (ctx != null)
            {
                var ti = ctx.GetTypeInfo(value.GetType());
                if (ti != null) return System.Text.Json.JsonSerializer.SerializeToElement(value, ti);
            }
            return System.Text.Json.JsonSerializer.SerializeToElement(value);
        }
        catch { return value; }
    }

    // ── Path walker ───────────────────────────────────────────────────────────

    private static object Walk(JsonElement root, string path)
    {
        // Split "Items[0].Address.City" → [("Items",0), ("Address",-1), ("City",-1)]
        var segments = ParseSegments(path);
        object current = root;

        foreach (var (key, index) in segments)
        {
            if (current is not JsonElement el) return null;

            // Navigate into object property
            if (!string.IsNullOrEmpty(key))
            {
                if (el.ValueKind != JsonValueKind.Object) return null;
                if (!el.TryGetProperty(key, out el))
                {
                    // Case-insensitive fallback
                    bool found = false;
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                        { el = prop.Value; found = true; break; }
                    }
                    if (!found) return null;
                }
                current = el;
            }

            // Navigate into array index
            if (index >= 0)
            {
                if (el.ValueKind != JsonValueKind.Array) return null;
                int i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    if (i++ == index) { current = item; break; }
                }
                if (i <= index) return null; // out of bounds
            }
        }

        return current is JsonElement final ? Unwrap(final) : current;
    }

    // ── ConvertParameter path → segment list ─────────────────────────────────
    // Handles:  Title | Address.City | Items[0] | Items[0].Title | [2].Name

    private static List<(string key, int index)> ParseSegments(string path)
    {
        var result = new List<(string, int)>();
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracketOpen = part.IndexOf('[');
            if (bracketOpen < 0)
            {
                result.Add((part, -1));
                continue;
            }

            // key part before '['
            var key = bracketOpen > 0 ? part[..bracketOpen] : string.Empty;

            // extract all [N] indexes on this segment (e.g. "matrix[0][1]")
            var rest = part[bracketOpen..];
            result.Add((key, -1));  // add the key first (index resolved next)
            result[^1] = (key, -1); // placeholder — overwrite below

            int lastKey = result.Count - 1;
            result.RemoveAt(lastKey);
            result.Add((key, -1));  // final with no index yet

            int pos = 0;
            bool firstIndex = true;
            while (pos < rest.Length)
            {
                if (rest[pos] != '[') break;
                var close = rest.IndexOf(']', pos + 1);
                if (close < 0) break;
                if (int.TryParse(rest.AsSpan(pos + 1, close - pos - 1), out var idx))
                {
                    if (firstIndex)
                    {
                        // merge into the key segment
                        result[^1] = (key, idx);
                        firstIndex = false;
                    }
                    else
                    {
                        // nested index like [0][1] — each becomes its own step
                        result.Add((string.Empty, idx));
                    }
                }
                pos = close + 1;
            }
        }
        return result;
    }

    // ── Unwrap a leaf JsonElement to its CLR value ────────────────────────────

    internal static object Unwrap(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String  => el.GetString(),
        JsonValueKind.Number  => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True    => true,
        JsonValueKind.False   => false,
        JsonValueKind.Null    => null,
        JsonValueKind.Array   => UnwrapArray(el),
        JsonValueKind.Object  => el,   // leave objects as JsonElement for nested bindings
        _                     => null
    };

    private static List<object> UnwrapArray(JsonElement el)
    {
        var list = new List<object>();
        foreach (var item in el.EnumerateArray())
            list.Add(Unwrap(item));
        return list;
    }
}
