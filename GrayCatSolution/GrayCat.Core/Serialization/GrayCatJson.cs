namespace GrayCat.Core.Serialization;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One set of JSON settings for project files and service traffic.
/// Enums are written as strings but numeric values are still accepted, so older files keep loading.
/// </summary>
public static class GrayCatJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: true);

    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    /// <summary>camelCase output for files consumed by generated JavaScript code.</summary>
    public static JsonSerializerOptions Web { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void ApplyTo(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        if (!options.Converters.Any(c => c is JsonStringEnumConverter))
            options.Converters.Add(new JsonStringEnumConverter());
    }
}
