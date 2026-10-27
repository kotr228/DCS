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

        private void RecordFilter(FilterOptionsViewModel options)
        {
            var title = FilterOptionsViewModel.TitleOf(options.Kind);
            if (options.HasSettings)
            {
                title += options.HasSecondAmount
                    ? $" ({N(options.Amount)}, {N(options.SecondAmount)})"
                    : $" ({N(options.Amount)}{options.Suffix})";
            }
            RecordAction(new ActionStep { Kind = "Filter", Command = options.Kind.ToString(), Title = title }
                .With("Amount", options.Amount).With("Second", options.SecondAmount));
        }

        private void RecordCameraRaw(CameraRawSettings s) =>
            RecordAction(new ActionStep { Kind = "CameraRaw", Command = "CameraRaw", Title = "Camera Raw Filter" }
                .With("Temperature", s.Temperature).With("Tint", s.Tint).With("Exposure", s.Exposure)
                .With("Contrast", s.Contrast).With("Highlights", s.Highlights).With("Shadows", s.Shadows)
                .With("Whites", s.Whites).With("Blacks", s.Blacks).With("Clarity", s.Clarity)
                .With("Vibrance", s.Vibrance).With("Saturation", s.Saturation));

        private void RecordSimpleAdjustment(SimpleAdjustmentViewModel options, string title) =>
            RecordAction(new ActionStep { Kind = "Adjustment", Command = options.Kind.ToString(), Title = title }
                .With("V1", options.Slider1Value).With("V2", options.Slider2Value).With("V3", options.Slider3Value));

        private void RecordLevels(LevelsViewModel o) =>
            RecordAction(new ActionStep { Kind = "Levels", Command = "Levels", Title = $"Levels ({o.InputBlack}, {N(o.Gamma)}, {o.InputWhite})" }
                .With("InBlack", o.InputBlack).With("InWhite", o.InputWhite).With("Gamma", o.Gamma)
                .With("OutBlack", o.OutputBlack).With("OutWhite", o.OutputWhite));

        private void RecordCurves(CurvesViewModel o) =>
            RecordAction(new ActionStep { Kind = "Curves", Command = "Curves", Title = $"Curves ({o.Points.Count} points)" }
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

                case "Filter":
                {
                    if (layer is null || layer.IsGroup || layer.IsAdjustment) return false;
                    if (!Enum.TryParse<FilterKind>(step.Command, out var kind)) return false;
                    var options = new FilterOptionsViewModel(kind) { Amount = step.Number("Amount"), SecondAmount = step.Number("Second") };
                    canvas.BeginAdjustment(FilterOptionsViewModel.TitleOf(kind));
                    canvas.UpdateAdjustmentPreview(options.Apply);
                    canvas.CommitAdjustment();
                    return true;
                }

                case "CameraRaw":
                {
                    if (layer is null || layer.IsGroup || layer.IsAdjustment) return false;
                    var settings = new CameraRawSettings
                    {
                        Temperature = step.Number("Temperature"), Tint = step.Number("Tint"), Exposure = step.Number("Exposure"),
                        Contrast = step.Number("Contrast"), Highlights = step.Number("Highlights"), Shadows = step.Number("Shadows"),
                        Whites = step.Number("Whites"), Blacks = step.Number("Blacks"), Clarity = step.Number("Clarity"),
                        Vibrance = step.Number("Vibrance"), Saturation = step.Number("Saturation"),
                    };
                    canvas.BeginAdjustment("Camera Raw Filter");
                    canvas.UpdateAdjustmentPreview(source => CameraRawFilter.Apply(source, settings));
                    canvas.CommitAdjustment();
                    return true;
                }

                case "Adjustment":
                {
                    if (layer is null || layer.IsGroup || layer.IsAdjustment) return false;
                    if (!Enum.TryParse<SimpleAdjustmentKind>(step.Command, out var kind)) return false;
                    var options = new SimpleAdjustmentViewModel(kind)
                    {
                        Slider1Value = step.Number("V1"), Slider2Value = step.Number("V2"), Slider3Value = step.Number("V3"),
                    };
                    canvas.BeginAdjustment(step.Title);
                    canvas.UpdateAdjustmentPreview(options.Apply);
                    canvas.CommitAdjustment();
                    return true;
                }

                case "Levels":
                {
                    if (layer is null || layer.IsGroup || layer.IsAdjustment) return false;
                    var lut = LevelsAdjustment.BuildLut((int)step.Number("InBlack"), (int)step.Number("InWhite", 255), step.Number("Gamma", 1),
                        (int)step.Number("OutBlack"), (int)step.Number("OutWhite", 255));
                    canvas.BeginAdjustment("Levels");
                    canvas.UpdateAdjustmentPreview(lut.Apply);
                    canvas.CommitAdjustment();
                    return true;
                }

                case "Curves":
                {
                    if (layer is null || layer.IsGroup || layer.IsAdjustment) return false;
                    var points = ParseCurvePoints(step.Text("Points"));
                    if (points.Count < 2) return false;
                    var lut = CurvesAdjustment.BuildLut(points);
                    canvas.BeginAdjustment("Curves");
                    canvas.UpdateAdjustmentPreview(lut.Apply);
                    canvas.CommitAdjustment();
                    return true;
                }

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
                    return false;
            }
        }
    }
}
