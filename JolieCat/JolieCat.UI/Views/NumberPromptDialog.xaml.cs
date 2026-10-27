using System.Globalization;
using System.Windows;

namespace JolieCat.UI.Views
{
    /// <summary>Asks for a single number within a range (Select &gt; Modify and similar).</summary>
    public partial class NumberPromptDialog : Window
    {
        private readonly double _min;
        private readonly double _max;

        public double Value { get; private set; }

        public NumberPromptDialog(string title, string label, double value, double min, double max, string suffix = "")
        {
            InitializeComponent();
            Title = title;
            LabelText.Text = label;
            SuffixText.Text = suffix;
            _min = min;
            _max = max;
            ValueBox.Text = value.ToString(CultureInfo.CurrentCulture);
            Loaded += (_, _) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var text = ValueBox.Text.Trim();
            if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                 double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
                value >= _min && value <= _max)
            {
                Value = value;
                DialogResult = true;
                return;
            }

            ErrorText.Text = $"Enter a number from {_min} to {_max}.";
        }
    }
}
