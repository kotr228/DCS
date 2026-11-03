using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JolieCat.Core.Adjustments;
using JolieCat.Core.Actions;
using JolieCat.Core.Filters;

namespace JolieCat.UI.ViewModels
{
    /// <summary>The Actions panel: recording commands as they run, and playing them back.</summary>
    public partial class MainViewModel
    {
        private ActionsViewModel? _actions;

        public ActionsViewModel ActionsPanel
        {
            get
            {
                if (_actions is null)
                {
                    _actions = new ActionsViewModel(ActionsViewModel.DefaultStorePath) { Player = PlayActionStep };
                }
                return _actions;
            }
        }

        /// <summary>Lets tests use an in-memory panel instead of the user's saved actions.</summary>
        internal void UseActionsPanel(ActionsViewModel panel)
        {
            _actions = panel;
            panel.Player = PlayActionStep;
        }

        private void RecordAction(ActionStep step) => _actions?.Record(step);

        /// <summary>Finishes a filter/adjustment dialog's OK: on a Smart Filter layer the
        /// step is added to its filters (editable later), otherwise the previewed pixels
        /// are kept. Either way the step is recorded into a recording action.</summary>
        private void CommitFilter(ActionStep step)
        {
            if (!Canvas.CommitAsSmartFilter(step)) Canvas.CommitAdjustment();
            RecordAction(step);
        }

        /// <summary>File > Automate > Batch: plays an action on a folder of images.</summary>
        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void OpenBatch()
        {
            var panel = ActionsPanel;
            if (panel.IsRecording) panel.StopCommand.Execute(null);
            var options = new BatchViewModel(panel.Actions.Select(a => a.Model), panel.SelectedAction?.Model);
            var dialog = new Views.BatchDialog { DataContext = options, Owner = System.Windows.Application.Current.MainWindow };
            dialog.ShowDialog();
        }

        private void OnLayersCommandRecorded(string command) =>
            RecordAction(new ActionStep { Kind = "Layers", Command = command, Title = JolieCat.UI.ViewModels.Layers.LayersViewModel.TitleOf(command) });

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        internal static ActionStep StepForFilter(FilterOptionsViewModel options)
        {
            var title = FilterOptionsViewModel.TitleOf(options.Kind);
            if (options.HasSettings)
            {
                title += options.HasSecondAmount
                    ? $" ({N(options.Amount)}, {N(options.SecondAmount)})"
                    : $" ({N(options.Amount)}{options.Suffix})";
            }
            return new ActionStep { Kind = "Filter", Command = options.Kind.ToString(), Title = title }
                .With("Amount", options.Amount).With("Second", options.SecondAmount);
        }

        internal static ActionStep StepForCameraRaw(CameraRawSettings s) =>
            (new ActionStep { Kind = "CameraRaw", Command = "CameraRaw", Title = "Camera Raw Filter" }
                .With("Temperature", s.Temperature).With("Tint", s.Tint).With("Exposure", s.Exposure)
                .With("Contrast", s.Contrast).With("Highlights", s.Highlights).With("Shadows", s.Shadows)
                .With("Whites", s.Whites).With("Blacks", s.Blacks).With("Clarity", s.Clarity)
                .With("Vibrance", s.Vibrance).With("Saturation", s.Saturation));

        internal static ActionStep StepForSimpleAdjustment(SimpleAdjustmentViewModel options, string title) =>
            (new ActionStep { Kind = "Adjustment", Command = options.Kind.ToString(), Title = title }
                .With("V1", options.Slider1Value).With("V2", options.Slider2Value).With("V3", options.Slider3Value));

        internal static ActionStep StepForBlurGallery(BlurGallerySettings s, string title) =>
            (new ActionStep { Kind = "BlurGallery", Command = s.Iris ? "Iris" : "TiltShift", Title = $"{title} ({N(s.Blur)} px)" }
                .With("CenterX", s.CenterX).With("CenterY", s.CenterY).With("Size", s.Size).With("SizeY", s.SizeY)
                .With("Transition", s.Transition).With("Angle", s.Angle).With("Blur", s.Blur).With("Saturation", s.Saturation));

        /// <summary>A step for the adjustments described by <see cref="AdjustmentSettings"/>
        /// (Exposure, Black &amp; White, Color Balance, Photo Filter, Gradient Map, Channel
        /// Mixer, Selective Color).</summary>
        internal static ActionStep StepForSettings(AdjustmentSettings settings) =>
            new ActionStep { Kind = "Settings", Command = settings.Kind.ToString(), Title = settings.DisplayName }
                .With("Settings", settings.Serialize());

        internal static ActionStep StepForLevels(LevelsViewModel o) =>
            (new ActionStep { Kind = "Levels", Command = "Levels", Title = $"Levels ({o.InputBlack}, {N(o.Gamma)}, {o.InputWhite})" }
                .With("InBlack", o.InputBlack).With("InWhite", o.InputWhite).With("Gamma", o.Gamma)
                .With("OutBlack", o.OutputBlack).With("OutWhite", o.OutputWhite));

        internal static ActionStep StepForCurves(CurvesViewModel o) =>
            (new ActionStep { Kind = "Curves", Command = "Curves", Title = $"Curves ({o.Points.Count} points)" }
                .With("Points", string.Join(";", o.Points.Select(p => N(p.X) + ":" + N(p.Y)))));

        /// <summary>"x:y;x:y" back into curve points.</summary>
        internal static List<(double X, double Y)> ParseCurvePoints(string text)
        {
            var points = new List<(double X, double Y)>();
            foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = pair.Split(':');
                if (xy.Length == 2
                    && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    points.Add((x, y));
            }
            return points;
        }

        private void RecordImageSize(int width, int height, int oldWidth, int oldHeight)
        {
            // Proportional resizes replay as "this width, keep the aspect ratio", so the
            // same action works on images of any shape.
            var proportional = Math.Abs((double)width / height - (double)oldWidth / oldHeight) < 0.01;
            RecordAction(new ActionStep { Kind = "ImageSize", Command = "ImageSize", Title = proportional ? $"Image Size (width {width} px)" : $"Image Size ({width} x {height})" }
                .With("Width", width).With("Height", height).With("Proportional", proportional ? 1 : 0));
        }

        /// <summary>Runs one recorded step on the active document, without dialogs.</summary>
        internal bool PlayActionStep(ActionStep step) => !IsSaving && PlayActionStep(step, ActiveDocument ?? FallbackDocument);

        /// <summary>The image filter a recorded step stands for (Filter, Camera Raw,
        /// Brightness/Contrast, Hue/Saturation, Blur Gallery, Levels, Curves, the other
        /// adjustments, Shadows/Highlights, Replace Color), or null for a step that isn't
        /// one. Used by Actions playback and by Smart Filters.</summary>
        internal static Func<SkiaSharp.SKBitmap, SkiaSharp.SKBitmap>? FilterFor(ActionStep step)
        {
            switch (step.Kind)
            {
                case "Filter":
                {
                    if (!Enum.TryParse<FilterKind>(step.Command, out var kind)) return null;
                    var options = new FilterOptionsViewModel(kind) { Amount = step.Number("Amount"), SecondAmount = step.Number("Second") };
                    return options.Apply;
                }

                case "CameraRaw":
                {
                    var settings = CameraRawSettingsOf(step);
                    return source => CameraRawFilter.Apply(source, settings);
                }

                case "Adjustment":
                {
                    if (!Enum.TryParse<SimpleAdjustmentKind>(step.Command, out var kind)) return null;
                    var options = new SimpleAdjustmentViewModel(kind)
                    {
                        Slider1Value = step.Number("V1"), Slider2Value = step.Number("V2"), Slider3Value = step.Number("V3"),
                    };
                    return options.Apply;
                }

                case "BlurGallery":
                {
                    var settings = BlurGallerySettingsOf(step);
                    return source => BlurGallery.Apply(source, settings);
                }

                case "Levels":
                {
                    var lut = LevelsAdjustment.BuildLut((int)step.Number("InBlack"), (int)step.Number("InWhite", 255), step.Number("Gamma", 1),
                        (int)step.Number("OutBlack"), (int)step.Number("OutWhite", 255));
                    return lut.Apply;
                }

                case "Curves":
                {
                    var points = ParseCurvePoints(step.Text("Points"));
                    if (points.Count < 2) return null;
                    return CurvesAdjustment.BuildLut(points).Apply;
                }

                case "Settings":
                {
                    if (AdjustmentSettings.Deserialize(step.Text("Settings")) is not { } settings) return null;
                    return source => ApplySettings(source, settings);
                }

                case "Tone":
                    return ToneFilterFor(step);

                default:
                    return null;
            }
        }

        internal static CameraRawSettings CameraRawSettingsOf(ActionStep step) => new()
        {
            Temperature = step.Number("Temperature"), Tint = step.Number("Tint"), Exposure = step.Number("Exposure"),
            Contrast = step.Number("Contrast"), Highlights = step.Number("Highlights"), Shadows = step.Number("Shadows"),
            Whites = step.Number("Whites"), Blacks = step.Number("Blacks"), Clarity = step.Number("Clarity"),
            Vibrance = step.Number("Vibrance"), Saturation = step.Number("Saturation"),
        };

        internal static BlurGallerySettings BlurGallerySettingsOf(ActionStep step) => new()
        {
            Iris = step.Command == "Iris",
            CenterX = step.Number("CenterX", 0.5), CenterY = step.Number("CenterY", 0.5),
            Size = step.Number("Size", 0.15), SizeY = step.Number("SizeY", 0.2),
            Transition = step.Number("Transition", 0.2), Angle = step.Number("Angle"),
            Blur = step.Number("Blur", 15), Saturation = step.Number("Saturation"),
        };

        /// <summary>Runs one recorded step on <paramref name="document"/> - which needn't
        /// be open in a tab (Batch processes files without showing them).</summary>
        internal static bool PlayActionStep(ActionStep step, DocumentViewModel document)
        {
            var layers = document.Layers;
            var canvas = document.Canvas;
            var layer = layers.ActiveLayer?.Model;

            switch (step.Kind)
            {
                case "Layers":
                    return layers.RunRecordableCommand(step.Command);

                case "ImageSize":
                {
                    var width = (int)Math.Round(step.Number("Width"));
                    var height = (int)Math.Round(step.Number("Height"));
                    if (width <= 0 || height <= 0) return false;
                    if (step.Number("Proportional") != 0)
                        height = Math.Max(1, (int)Math.Round(width * (double)layers.DocumentHeight / layers.DocumentWidth));
                    if (width == layers.DocumentWidth && height == layers.DocumentHeight) return true;
                    layers.ResampleDocument(width, height);
                    return true;
                }

                default:
                {
                    if (FilterFor(step) is not { } filter) return false;
                    if (layer is null || layer.IsGroup || layer.IsAdjustment) return false;
                    if (layer.HasSmartFilters)
                    {
                        canvas.AddSmartFilter(layer, step);
                        return true;
                    }
                    canvas.BeginAdjustment(string.IsNullOrEmpty(step.Title) ? step.Command : step.Title);
                    canvas.UpdateAdjustmentPreview(filter);
                    canvas.CommitAdjustment();
                    return true;
                }
            }
        }
    }
}
