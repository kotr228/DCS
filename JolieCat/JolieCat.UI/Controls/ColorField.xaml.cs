using System.Windows;
using System.Windows.Controls;
using JolieCat.UI.Views;

namespace JolieCat.UI.Controls
{
    /// <summary>A "#RRGGBB" text box with a swatch that opens the Color Picker dialog.</summary>
    public partial class ColorField : UserControl
    {
        public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
            nameof(Hex), typeof(string), typeof(ColorField),
            new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ColorField()
        {
            InitializeComponent();
        }

        public string Hex
        {
            get => (string)GetValue(HexProperty);
            set => SetValue(HexProperty, value);
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (ColorPickerDialog.Pick(Hex, Window.GetWindow(this)) is { } picked)
                Hex = picked;
        }
    }
}
