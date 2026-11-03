using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Options shown for the Horizontal Text and Vertical Text tools (Photoshop's
    /// Character and Paragraph settings, plus Warp Text's Arc).</summary>
    public partial class TextToolOptionsViewModel : ObservableObject
    {
        private static IReadOnlyList<string>? _systemFonts;

        /// <summary>Every font family installed on this computer.</summary>
        public IReadOnlyList<string> AvailableFontFamilies => _systemFonts ??= LoadFonts();

        private static IReadOnlyList<string> LoadFonts()
        {
            try
            {
                var fonts = SKFontManager.Default.FontFamilies
                    .Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith('@'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (fonts.Count > 0) return fonts;
            }
            catch (Exception)
            {
                // Font enumeration can fail on locked-down systems; fall back below.
            }
            return new[] { "Segoe UI", "Arial", "Georgia", "Consolas", "Times New Roman", "Verdana" };
        }

        public static IReadOnlyList<TextAlignment> AllAlignments { get; } = Enum.GetValues<TextAlignment>();

        public static IReadOnlyList<TextWarpStyle> AllWarpStyles { get; } = Enum.GetValues<TextWarpStyle>();

        [ObservableProperty]
        private TextWarpStyle warpStyle = TextWarpStyle.Arc;

        [ObservableProperty]
        private string fontFamily = "Segoe UI";

        [ObservableProperty]
        private double fontSize = 24;

        [ObservableProperty]
        private bool isBold;

        [ObservableProperty]
        private bool isItalic;

        [ObservableProperty]
        private bool isUnderline;

        [ObservableProperty]
        private bool isStrikethrough;

        [ObservableProperty]
        private TextAlignment alignment = TextAlignment.Left;

        /// <summary>Line spacing, % of normal.</summary>
        [ObservableProperty]
        private double leading = 100;

        /// <summary>Letter spacing, 1/1000 em.</summary>
        [ObservableProperty]
        private double tracking;

        /// <summary>Warp Text "Arc", -100..100.</summary>
        [ObservableProperty]
        private double warp;

        /// <summary>These settings applied to <paramref name="text"/> (its words, place and
        /// color stay).</summary>
        public TextContent ApplyTo(TextContent text) => text with
        {
            FontFamily = FontFamily,
            FontSize = (float)FontSize,
            Bold = IsBold,
            Italic = IsItalic,
            Underline = IsUnderline,
            Strikethrough = IsStrikethrough,
            Alignment = Alignment,
            Leading = (float)Leading,
            Tracking = (float)Tracking,
            Warp = (float)Warp,
            WarpStyle = WarpStyle,
        };

        /// <summary>Shows <paramref name="text"/>'s settings.</summary>
        public void LoadFrom(TextContent text)
        {
            FontFamily = text.FontFamily;
            FontSize = text.FontSize;
            IsBold = text.Bold;
            IsItalic = text.Italic;
            IsUnderline = text.Underline;
            IsStrikethrough = text.Strikethrough;
            Alignment = text.Alignment;
            Leading = text.Leading;
            Tracking = text.Tracking;
            Warp = text.Warp;
            WarpStyle = text.WarpStyle;
        }
    }
}
