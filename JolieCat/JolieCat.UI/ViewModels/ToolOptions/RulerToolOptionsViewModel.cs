using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Properties panel for the Ruler tool: what the measured line reads, and
    /// Straighten Layer / Clear. The canvas fills in the readout as the line is drawn.</summary>
    public sealed partial class RulerToolOptionsViewModel : ObservableObject
    {
        [ObservableProperty] private bool hasLine;
        [ObservableProperty] private string lengthText = "-";
        [ObservableProperty] private string angleText = "-";
        [ObservableProperty] private string sizeText = "-";
        [ObservableProperty] private string pointsText = "-";

        /// <summary>Set by the canvas: turns the active layer so the line is level.</summary>
        public IRelayCommand? StraightenCommand { get; set; }

        /// <summary>Set by the canvas: removes the line.</summary>
        public IRelayCommand? ClearCommand { get; set; }

        /// <summary>Shows the line from (x1, y1) to (x2, y2).</summary>
        public void Show(double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var length = System.Math.Sqrt(dx * dx + dy * dy);
            // Angles as Photoshop shows them: counter-clockwise from the right is positive.
            var angle = System.Math.Atan2(-dy, dx) * 180 / System.Math.PI;
            HasLine = length > 0.5;
            LengthText = FormattableStringFactory($"{length:0.0} px");
            AngleText = FormattableStringFactory($"{angle:0.0}°");
            SizeText = FormattableStringFactory($"W {System.Math.Abs(dx):0}  H {System.Math.Abs(dy):0}");
            PointsText = FormattableStringFactory($"({x1:0}, {y1:0}) → ({x2:0}, {y2:0})");
        }

        public void Clear()
        {
            HasLine = false;
            LengthText = AngleText = SizeText = PointsText = "-";
        }

        private static string FormattableStringFactory(System.FormattableString s) => System.FormattableString.Invariant(s);
    }
}
