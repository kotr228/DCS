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
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(100.0));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0));

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

        public LabeledSlider()
        {
            InitializeComponent();
        }
    }
}
