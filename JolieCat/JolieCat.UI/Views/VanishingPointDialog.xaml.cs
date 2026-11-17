using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>Filter &gt; Vanishing Point. The four corner handles are dragged with the mouse.</summary>
    public partial class VanishingPointDialog : Window
    {
        private const double HandleSize = 14;
        private readonly VanishingPointViewModel _model;
        private readonly Thumb[] _handles = new Thumb[4];

        public VanishingPointDialog(VanishingPointViewModel model)
        {
            InitializeComponent();
            DataContext = _model = model;
            var names = new[] { "Top-left corner", "Top-right corner", "Bottom-right corner", "Bottom-left corner" };
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var thumb = new Thumb
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Cursor = System.Windows.Input.Cursors.SizeAll,
                    ToolTip = names[i],
                    Template = HandleTemplate(),
                };
                System.Windows.Automation.AutomationProperties.SetName(thumb, names[i]);
                thumb.DragDelta += (_, e) =>
                {
                    var p = _model.CornerOnPreview(index);
                    _model.MoveCorner(index, p.X + e.HorizontalChange, p.Y + e.VerticalChange);
                };
                _handles[i] = thumb;
                Handles.Children.Add(thumb);
            }
            _model.CornersChanged += (_, _) => PlaceHandles();
            PlaceHandles();
        }

        private static ControlTemplate HandleTemplate()
        {
            var template = new ControlTemplate(typeof(Thumb));
            var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            dot.SetValue(System.Windows.Shapes.Shape.FillProperty, new SolidColorBrush(Color.FromArgb(200, 90, 200, 250)));
            dot.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
            dot.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
            template.VisualTree = dot;
            return template;
        }

        private void PlaceHandles()
        {
            for (var i = 0; i < 4; i++)
            {
                var p = _model.CornerOnPreview(i);
                Canvas.SetLeft(_handles[i], p.X - HandleSize / 2);
                Canvas.SetTop(_handles[i], p.Y - HandleSize / 2);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!_model.HasPicture)
            {
                MessageBox.Show(this, "Pick a picture to lay on the surface first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
