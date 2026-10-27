using System.Windows;
using System.Windows.Controls;

namespace JolieCat.UI.Controls
{
    /// <summary>
    /// A label + slider + live numeric readout, reused across the tool option panels
    /// (Size/Hardness/Opacity, Feather/Tolerance, Angle, Stroke Width, etc.) so each of
    /// those views only has to declare which property it's driving.
    /// </summary>
    public partial class LabeledSlider : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(LabeledSlider), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0, (d, _) => ((LabeledSlider)d).UpdateDisplay()));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(100.0, (d, _) => ((LabeledSlider)d).UpdateDisplay()));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0, (d, _) => ((LabeledSlider)d).UpdateDisplay()));

        public static readonly DependencyProperty SuffixProperty =
            DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(LabeledSlider), new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Unit shown after the numeric readout, e.g. "%", "px", "°". Optional.</summary>
        public string Suffix
        {
            get => (string)GetValue(SuffixProperty);
            set => SetValue(SuffixProperty, value);
        }

        private static readonly DependencyPropertyKey DisplayTextKey =
            DependencyProperty.RegisterReadOnly(nameof(DisplayText), typeof(string), typeof(LabeledSlider), new PropertyMetadata("0"));

        /// <summary>The readout: whole numbers for wide ranges (sizes, percentages),
        /// one decimal for narrow ones (0-1 opacity, 0-5 strength) where it matters.</summary>
        public string DisplayText => (string)GetValue(DisplayTextKey.DependencyProperty);

        private void UpdateDisplay()
        {
            var format = Maximum - Minimum <= 10 ? "0.##" : "0";
            SetValue(DisplayTextKey, Value.ToString(format, System.Globalization.CultureInfo.CurrentCulture));
        }

        public LabeledSlider()
        {
            InitializeComponent();
            UpdateDisplay();
        }

        private void ValueBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
            ValueBox.Dispatcher.BeginInvoke(ValueBox.SelectAll);

        private void ValueBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                CommitTypedValue();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                ValueBox.Text = DisplayText;
                ValueBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ValueBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => CommitTypedValue();

        /// <summary>Parses the typed number (current culture or invariant, so both
        /// "1,5" and "1.5" work) and applies it clamped to the slider's range.</summary>
        private void CommitTypedValue()
        {
            var text = ValueBox.Text.Trim().TrimEnd('%', ' ');
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var typed) ||
                double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out typed))
            {
                SetCurrentValue(ValueProperty, System.Math.Clamp(typed, Minimum, Maximum));
            }

            ValueBox.Text = DisplayText;
        }
    }
}
