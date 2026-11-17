using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace JolieCat.UI.Localization
{
    /// <summary>
    /// The interface language: English (as written in the code) or a translation from
    /// the built-in dictionaries (Localization/uk.json, ru.json: English text → translation).
    /// Anything without a translation stays English.
    /// </summary>
    public static class Loc
    {
        /// <summary>The languages offered: code and the name shown in the switcher.</summary>
        public static IReadOnlyList<(string Code, string Name)> Languages { get; } = new[]
        {
            ("en", "English"),
            ("uk", "Українська"),
            ("ru", "Русский"),
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Dictionaries = new();
        private static Dictionary<string, string>? _current;

        /// <summary>"en", "uk" or "ru".</summary>
        public static string Language { get; private set; } = "en";

        /// <summary>Raised after the language changed (the open windows are re-translated).</summary>
        public static event Action? Changed;

        public static void SetLanguage(string code)
        {
            code = code is "uk" or "ru" ? code : "en";
            if (code == Language && (code == "en" || _current is not null)) return;
            Language = code;
            _current = code == "en" ? null : Load(code);
            Changed?.Invoke();
        }

        private static Dictionary<string, string> Load(string code)
        {
            if (Dictionaries.TryGetValue(code, out var cached)) return cached;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"JolieCat.UI.Localization.{code}.json");
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                    if (map is not null)
                        foreach (var (k, v) in map)
                            if (!string.IsNullOrEmpty(v)) result[k] = v;
                }
            }
            catch (JsonException)
            {
            }
            Dictionaries[code] = result;
            return result;
        }

        /// <summary>
        /// <paramref name="english"/> in the current language. Tries the text as it is,
        /// then without menu access-key underscores, then without a trailing "..." (the
        /// dots are kept); otherwise returns it unchanged.
        /// </summary>
        public static string T(string english)
        {
            if (_current is null || string.IsNullOrEmpty(english)) return english;
            if (_current.TryGetValue(english, out var t)) return t;
            var plain = english.Replace("_", "");
            if (plain != english && _current.TryGetValue(plain, out t)) return t;
            if (plain.EndsWith("...", StringComparison.Ordinal) && _current.TryGetValue(plain[..^3], out t)) return t + "...";
            // "Lasso (L)": the name translated, the key kept.
            var open = plain.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && plain.EndsWith(')') && _current.TryGetValue(plain[..open], out t)) return t + plain[open..];
            return english;
        }

        /// <summary>True when a translation exists for <paramref name="english"/> (for tests and tools).</summary>
        public static bool Has(string language, string english) => Load(language).ContainsKey(english);

        /// <summary>Every translation of a language (for tests).</summary>
        public static IReadOnlyDictionary<string, string> All(string language) => Load(language);
    }
}
