namespace GrayCat.Core.Generation;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Block properties arrive in several shapes: real CLR values set by the editor,
/// JsonElement after loading a project file, or JSON text typed into a field.
/// These helpers turn all of them into plain values.
/// </summary>
public static class PropertyValues
{
    /// <summary>Converts a value to string / bool / double / List / Dictionary (recursively).</summary>
    public static object? ToPlain(object? value, bool parseJsonStrings = true)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement element:
                return FromElement(element);
            case string s:
                if (parseJsonStrings && LooksLikeJson(s))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(s);
                        return FromElement(doc.RootElement);
                    }
                    catch (JsonException)
                    {
                        return s;
                    }
                }
                return s;
            case bool or double:
                return value;
            case int or long or float or decimal or short or byte:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            case IDictionary<string, object?> dict:
                return dict.ToDictionary(kv => kv.Key, kv => ToPlain(kv.Value, parseJsonStrings));
            case IDictionary<string, string> sdict:
                return sdict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            case System.Collections.IEnumerable list:
                return list.Cast<object?>().Select(v => ToPlain(v, parseJsonStrings)).ToList();
            default:
                return value.ToString();
        }
    }

    /// <summary>
    /// Shape used by the editor: primitives stay primitives, arrays and objects become JSON text
    /// so they can be edited in a text box.
    /// </summary>
    public static object ToEditable(object? value)
    {
        var plain = ToPlain(value, parseJsonStrings: false);
        return plain switch
        {
            null => string.Empty,
            string or bool or double => plain,
            _ => JsonSerializer.Serialize(plain)
        };
    }

    public static void NormalizeForEditing(Dictionary<string, object> properties)
    {
        foreach (var key in properties.Keys.ToList())
        {
            properties[key] = ToEditable(properties[key]);
        }
    }

    public static string AsString(object? value, string fallback = "")
    {
        var plain = ToPlain(value, parseJsonStrings: false);
        return plain switch
        {
            null => fallback,
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(plain)
        };
    }

    public static bool AsBool(object? value, bool fallback = false)
    {
        var plain = ToPlain(value, parseJsonStrings: false);
        return plain switch
        {
            bool b => b,
            double d => d != 0,
            string s when bool.TryParse(s.Trim(), out var b) => b,
            string s when s.Trim() is "1" or "yes" or "on" => true,
            string s when s.Trim() is "0" or "no" or "off" => false,
            _ => fallback
        };
    }

    public static double AsDouble(object? value, double fallback = 0)
    {
        var plain = ToPlain(value, parseJsonStrings: false);
        return plain switch
        {
            double d when !double.IsNaN(d) && !double.IsInfinity(d) => d,
            bool b => b ? 1 : 0,
            string s when double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => fallback
        };
    }

    public static List<object?> AsList(object? value)
    {
        return ToPlain(value) switch
        {
            List<object?> list => list,
            string s when !string.IsNullOrWhiteSpace(s) =>
                s.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(x => (object?)x).ToList(),
            Dictionary<string, object?> single => new List<object?> { single },
            _ => new List<object?>()
        };
    }

    private static bool LooksLikeJson(string s)
    {
        var t = s.TrimStart();
        return t.StartsWith('[') || t.StartsWith('{');
    }

    private static object? FromElement(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => e.EnumerateArray().Select(FromElement).ToList(),
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => FromElement(p.Value)),
        _ => null
    };
}
