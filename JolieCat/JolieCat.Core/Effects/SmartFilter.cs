using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using JolieCat.Core.Actions;
using SkiaSharp;

namespace JolieCat.Core.Effects
{
    /// <summary>
    /// One Smart Filter on a layer: a filter (described the same way an Actions step
    /// is - kind, command, settings) that is re-applied to the layer's untouched
    /// original whenever the filter list changes, so it can be edited, hidden or
    /// removed later.
    /// </summary>
    public sealed record SmartFilter(ActionStep Step, bool Enabled = true)
    {
        public string Title => string.IsNullOrEmpty(Step.Title) ? Step.Command : Step.Title;

        private static readonly JsonSerializerOptions Json = new();

        public static string Serialize(IEnumerable<SmartFilter> filters) => JsonSerializer.Serialize(filters.ToList(), Json);

        public static IReadOnlyList<SmartFilter> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SmartFilter>();
            try
            {
                return JsonSerializer.Deserialize<List<SmartFilter>>(json, Json) ?? new List<SmartFilter>();
            }
            catch (JsonException)
            {
                return Array.Empty<SmartFilter>();
            }
        }
    }

    /// <summary>
    /// Runs a Smart Filter's step on a bitmap. The app sets <see cref="Apply"/> at
    /// startup (the filters themselves live with the dialogs that edit them); it returns
    /// null for a step it can't run, which then leaves the image as it was.
    /// </summary>
    public static class SmartFilterEngine
    {
        public static Func<ActionStep, SKBitmap, SKBitmap?>? Apply { get; set; }

        /// <summary><paramref name="source"/> run through every enabled filter, in order.</summary>
        public static SKBitmap Render(SKBitmap source, IEnumerable<SmartFilter> filters)
        {
            var current = source.Copy();
            foreach (var filter in filters)
            {
                if (!filter.Enabled || Apply is null) continue;
                var next = Apply(filter.Step, current);
                if (next is null) continue;
                current.Dispose();
                current = next;
            }
            return current;
        }
    }
}
