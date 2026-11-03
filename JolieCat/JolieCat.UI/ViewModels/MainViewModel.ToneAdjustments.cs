using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Rendering;
using JolieCat.Core.Actions;
using JolieCat.Core.Adjustments;
using JolieCat.Core.Filters;
using JolieCat.UI.Views;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>Image &gt; Adjustments &gt; Shadows/Highlights and Replace Color: settings
    /// dialogs with a live preview on the active layer, recordable into actions.</summary>
    public partial class MainViewModel
    {
        private bool CanAdjustPixels() =>
            !IsSaving && Layers.ActiveLayer is { } layer && !layer.Model.IsGroup && !layer.Model.IsAdjustment;

        [RelayCommand]
        private void ShadowsHighlights()
        {
            if (!CanAdjustPixels()) return;
            var options = new FieldsDialogViewModel("Shadows/Highlights",
                "Brightens dark areas and darkens bright ones, judged by their surroundings.");
            var shadows = options.Add(new SliderDialogField("Shadows", 0, 100, 35, "%"));
            var highlights = options.Add(new SliderDialogField("Highlights", 0, 100, 0, "%"));
            var tone = options.Add(new SliderDialogField("Tone", 1, 100, 50, "%"));
            var radius = options.Add(new SliderDialogField("Radius", 0, 500, 30, "px"));

            ActionStep Step() => new ActionStep
                {
                    Kind = "Tone", Command = "ShadowsHighlights",
                    Title = $"Shadows/Highlights ({N(shadows.Value)}%, {N(highlights.Value)}%)",
                }
                .With("Shadows", shadows.Value).With("Highlights", highlights.Value)
                .With("Tone", tone.Value).With("Radius", radius.Value);

            RunToneDialog(options, Step);
        }

        [RelayCommand]
        private void ReplaceColor()
        {
            if (!CanAdjustPixels()) return;
            var fore = Canvas.PrimaryColor;
            var options = new FieldsDialogViewModel("Replace Color",
                "Changes the hue, saturation and lightness of one color. It starts with the foreground color; click the swatch to pick another.");
            var color = options.Add(new ColorDialogField("Color", $"#{fore.R:X2}{fore.G:X2}{fore.B:X2}"));
            var fuzziness = options.Add(new SliderDialogField("Fuzziness", 0, 200, 40));
            var hue = options.Add(new SliderDialogField("Hue", -180, 180, 0, "°"));
            var saturation = options.Add(new SliderDialogField("Saturation", -100, 100, 0));
            var lightness = options.Add(new SliderDialogField("Lightness", -100, 100, 0));

            ActionStep Step() => new ActionStep
                {
                    Kind = "Tone", Command = "ReplaceColor",
                    Title = $"Replace Color ({color.Hex}, hue {N(hue.Value)})",
                }
                .With("Color", color.Hex).With("Fuzziness", fuzziness.Value).With("Hue", hue.Value)
                .With("Saturation", saturation.Value).With("Lightness", lightness.Value);

            RunToneDialog(options, Step);
        }

        /// <summary>Edit &gt; Auto-Align Layers / File &gt; Automate &gt; Merge to HDR Pro:
        /// runs the layers command and explains when there was nothing to do.</summary>
        [RelayCommand]
        private void RunMultiShot(string which)
        {
            if (IsSaving) return;
            var command = which == "Hdr" ? Layers.MergeToHdrProCommand : Layers.AutoAlignLayersCommand;
            command.Execute(null);
            var message = Layers.LastMultiShotMessage;
            if (!message.StartsWith("Aligned", StringComparison.Ordinal) && !message.StartsWith("Merged", StringComparison.Ordinal))
                System.Windows.MessageBox.Show(message, which == "Hdr" ? "Merge to HDR Pro" : "Auto-Align Layers",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>Edit &gt; Puppet Warp: picks the Puppet Warp tool.</summary>
        [RelayCommand]
        private void PuppetWarp() => Toolbox.SelectTool(Shared.Enums.ToolType.PuppetWarp);

        /// <summary>Image &gt; Adjustments &gt; Match Color: takes on the colors of another
        /// open document or another layer. The step stores the source's color
        /// statistics, so a recorded action replays without the source being open.</summary>
        [RelayCommand]
        private void MatchColor()
        {
            if (!CanAdjustPixels() || ActiveDocument is not { } current) return;

            const string none = "(none)";
            var sources = new System.Collections.Generic.Dictionary<string, Func<SKBitmap>>();
            foreach (var document in Documents.Where(d => !ReferenceEquals(d, current)))
            {
                var d = document;
                sources[UniqueName(sources, d.Title)] = () => SceneCompositor.Flatten(d.Layers.Scene, d.Layers.DocumentWidth, d.Layers.DocumentHeight);
            }
            foreach (var layer in current.Layers.Scene.Layers.Where(l => !l.IsGroup && !l.IsAdjustment && !ReferenceEquals(l, Layers.ActiveLayer?.Model)))
            {
                var l = layer;
                sources[UniqueName(sources, $"Layer: {l.Name}")] = () => l.Bitmap.Copy();
            }

            var options = new FieldsDialogViewModel("Match Color",
                "Gives this layer the overall colors and brightness of another image. Neutralize removes a color cast.");
            var source = options.Add(new ChoiceDialogField("Source", new[] { none }.Concat(sources.Keys).ToList(),
                sources.Count > 0 ? sources.Keys.First() : none));
            var luminance = options.Add(new SliderDialogField("Luminance", 1, 200, 100, "%"));
            var intensity = options.Add(new SliderDialogField("Color Intensity", 0, 200, 100, "%"));
            var fade = options.Add(new SliderDialogField("Fade", 0, 100, 0, "%"));
            var neutralize = options.Add(new ChoiceDialogField("Neutralize", new[] { "Off", "On" }, sources.Count > 0 ? "Off" : "On"));

            var stats = new System.Collections.Generic.Dictionary<string, ColorStats>();
            ColorStats? StatsOf(string name)
            {
                if (!sources.TryGetValue(name, out var image)) return null;
                if (!stats.TryGetValue(name, out var s))
                {
                    using var bitmap = image();
                    stats[name] = s = ColorStats.Of(bitmap);
                }
                return s;
            }

            ActionStep Step()
            {
                var s = StatsOf(source.Selected);
                var step = new ActionStep
                    {
                        Kind = "Tone", Command = "MatchColor",
                        Title = s is null ? "Match Color (neutralize)" : $"Match Color ({source.Selected})",
                    }
                    .With("Luminance", luminance.Value).With("Intensity", intensity.Value).With("Fade", fade.Value)
                    .With("Neutralize", neutralize.Selected == "On" ? 1 : 0).With("HasSource", s is null ? 0 : 1);
                if (s is { } v)
                    step = step.With("MY", v.MeanY).With("SY", v.SpreadY).With("MB", v.MeanCb).With("SB", v.SpreadCb)
                        .With("MR", v.MeanCr).With("SR", v.SpreadCr);
                return step;
            }

            RunToneDialog(options, Step);
        }

        private static string UniqueName(System.Collections.Generic.IDictionary<string, Func<SKBitmap>> names, string name)
        {
            var unique = name;
            for (var n = 2; names.ContainsKey(unique); n++) unique = $"{name} ({n})";
            return unique;
        }

        /// <summary>Filter &gt; Blur &gt; Lens Blur: an out-of-focus camera blur with bokeh.</summary>
        [RelayCommand]
        private void LensBlur()
        {
            if (!CanAdjustPixels()) return;
            var options = new FieldsDialogViewModel("Lens Blur",
                "Blurs like an out-of-focus lens: points become discs, and highlights brighter than Threshold bloom into bright circles.");
            var radius = options.Add(new SliderDialogField("Radius", 0, 100, 15, "px"));
            var brightness = options.Add(new SliderDialogField("Specular Brightness", 0, 100, 0));
            var threshold = options.Add(new SliderDialogField("Specular Threshold", 0, 255, 230));

            ActionStep Step() => new ActionStep
                {
                    Kind = "Tone", Command = "LensBlur", Title = $"Lens Blur ({N(radius.Value)} px)",
                }
                .With("Radius", radius.Value).With("Brightness", brightness.Value).With("Threshold", threshold.Value);

            RunToneDialog(options, Step);
        }

        /// <summary>Filter &gt; Lens Correction: distortion, color fringes and vignetting.</summary>
        [RelayCommand]
        private void LensCorrection()
        {
            if (!CanAdjustPixels()) return;
            var options = new FieldsDialogViewModel("Lens Correction",
                "Fixes what a camera lens does to a photo: bent straight lines, colored edges and dark corners.");
            var distortion = options.Add(new SliderDialogField("Remove Distortion", -100, 100, 0));
            var redCyan = options.Add(new SliderDialogField("Fix Red/Cyan Fringe", -100, 100, 0));
            var blueYellow = options.Add(new SliderDialogField("Fix Blue/Yellow Fringe", -100, 100, 0));
            var vignette = options.Add(new SliderDialogField("Vignette Amount", -100, 100, 0));
            var midpoint = options.Add(new SliderDialogField("Vignette Midpoint", 0, 100, 50));

            ActionStep Step() => new ActionStep
                {
                    Kind = "Tone", Command = "LensCorrection",
                    Title = $"Lens Correction ({N(distortion.Value)}, vignette {N(vignette.Value)})",
                }
                .With("Distortion", distortion.Value).With("RedCyan", redCyan.Value).With("BlueYellow", blueYellow.Value)
                .With("Vignette", vignette.Value).With("Midpoint", midpoint.Value);

            RunToneDialog(options, Step);
        }

        /// <summary>Filter &gt; Render &gt; Lighting Effects.</summary>
        [RelayCommand]
        private void LightingEffects()
        {
            if (!CanAdjustPixels()) return;
            string[] kinds = { "Spot", "Omni", "Infinite" };
            var options = new FieldsDialogViewModel("Lighting Effects",
                "Lights the layer like a lamp in a dark room. Texture turns the image's brightness into relief that catches the light.");
            var kind = options.Add(new ChoiceDialogField("Light Type", kinds));
            var color = options.Add(new ColorDialogField("Color", "#FFF4E0"));
            var x = options.Add(new SliderDialogField("Center X", 0, 100, 40, "%"));
            var y = options.Add(new SliderDialogField("Center Y", 0, 100, 40, "%"));
            var radius = options.Add(new SliderDialogField("Size", 5, 150, 60, "%"));
            var intensity = options.Add(new SliderDialogField("Intensity", 0, 200, 100, "%"));
            var ambient = options.Add(new SliderDialogField("Ambience", 0, 100, 25, "%"));
            var texture = options.Add(new SliderDialogField("Texture", 0, 100, 0));

            ActionStep Step() => new ActionStep
                {
                    Kind = "Tone", Command = "LightingEffects", Title = $"Lighting Effects ({kind.Selected})",
                }
                .With("Kind", Array.IndexOf(kinds, kind.Selected)).With("Color", color.Hex).With("X", x.Value).With("Y", y.Value)
                .With("Radius", radius.Value).With("Intensity", intensity.Value).With("Ambient", ambient.Value).With("Texture", texture.Value);

            RunToneDialog(options, Step);
        }

        /// <summary>Filter &gt; Render &gt; Lens Flare.</summary>
        [RelayCommand]
        private void LensFlare()
        {
            if (!CanAdjustPixels()) return;
            string[] lenses = { "50-300mm Zoom", "35mm Prime", "105mm Prime" };
            var options = new FieldsDialogViewModel("Lens Flare", "Where the light shines from is given in percent of the image's width and height.");
            var brightness = options.Add(new SliderDialogField("Brightness", 10, 300, 100, "%"));
            var x = options.Add(new SliderDialogField("Center X", 0, 100, 30, "%"));
            var y = options.Add(new SliderDialogField("Center Y", 0, 100, 30, "%"));
            var lens = options.Add(new ChoiceDialogField("Lens Type", lenses));

            ActionStep Step() => new ActionStep
                {
                    Kind = "Tone", Command = "LensFlare", Title = $"Lens Flare ({N(brightness.Value)}%)",
                }
                .With("Brightness", brightness.Value).With("X", x.Value).With("Y", y.Value)
                .With("Lens", Array.IndexOf(lenses, lens.Selected));

            RunToneDialog(options, Step);
        }

        /// <summary>Shows <paramref name="options"/>, previewing <paramref name="step"/>'s
        /// filter on the canvas; OK keeps it (or adds it to a Smart Filter layer).</summary>
        private void RunToneDialog(FieldsDialogViewModel options, Func<ActionStep> step)
        {
            Canvas.BeginAdjustment(options.Title);
            void Preview()
            {
                if (ToneFilterFor(step()) is { } filter) Canvas.UpdateAdjustmentPreview(filter);
            }
            options.Changed += Preview;
            Preview();

            var applied = FieldsDialog.Show(options);
            options.Changed -= Preview;
            if (applied) CommitFilter(step());
            else Canvas.CancelAdjustment();
        }

        internal static Func<SKBitmap, SKBitmap>? ToneFilterFor(ActionStep step) => step.Command switch
        {
            "ShadowsHighlights" => source => ToneFilters.ShadowsHighlights(source,
                step.Number("Shadows", 35), step.Number("Highlights"), step.Number("Tone", 50), step.Number("Radius", 30)),
            "ReplaceColor" => source => ToneFilters.ReplaceColor(source,
                LayerStyleViewModel.Parse(step.Text("Color"), SKColors.Red), step.Number("Fuzziness", 40),
                step.Number("Hue"), step.Number("Saturation"), step.Number("Lightness")),
            "MatchColor" => source => PhotoFilters.MatchColor(source,
                step.Number("HasSource") >= 0.5
                    ? new ColorStats(step.Number("MY"), step.Number("SY", 1), step.Number("MB"), step.Number("SB", 1), step.Number("MR"), step.Number("SR", 1))
                    : null,
                step.Number("Luminance", 100), step.Number("Intensity", 100), step.Number("Fade"), step.Number("Neutralize") >= 0.5),
            "LensBlur" => source => PhotoFilters.LensBlur(source,
                (float)step.Number("Radius", 15), (float)step.Number("Brightness"), (float)step.Number("Threshold", 230)),
            "LensCorrection" => source => ArtFilters.LensCorrection(source,
                (float)step.Number("Distortion"), (float)step.Number("RedCyan"), (float)step.Number("BlueYellow"),
                (float)step.Number("Vignette"), (float)step.Number("Midpoint", 50)),
            "LightingEffects" => source => ArtFilters.LightingEffects(source, (int)step.Number("Kind"),
                (float)(step.Number("X", 40) / 100), (float)(step.Number("Y", 40) / 100), (float)(step.Number("Radius", 60) / 100),
                (float)step.Number("Intensity", 100), (float)step.Number("Ambient", 25),
                LayerStyleViewModel.Parse(step.Text("Color"), SKColors.White), (float)step.Number("Texture")),
            "LensFlare" => source => ArtFilters.LensFlare(source,
                (float)(step.Number("X", 30) / 100), (float)(step.Number("Y", 30) / 100), (float)step.Number("Brightness", 100),
                (int)step.Number("Lens")),
            _ => null,
        };

        /// <summary>Runs <paramref name="source"/> through an adjustment's color filter.</summary>
        internal static SKBitmap ApplySettings(SKBitmap source, AdjustmentSettings settings)
        {
            var result = new SKBitmap(source.Info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            using var filter = settings.CreateColorFilter();
            using var paint = new SKPaint { ColorFilter = filter };
            canvas.DrawBitmap(source, 0, 0, paint);
            return result;
        }
    }
}
