using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Views
{
    /// <summary>Edit &gt; Transform &gt; Distort / Perspective: four draggable corners.</summary>
    public partial class CornerPinDialog : Window
    {
        private const double HandleSize = 14;
        private readonly CornerPinViewModel _model;
        private readonly Thumb[] _handles = new Thumb[4];

        public CornerPinDialog(CornerPinViewModel model)
        {
            InitializeComponent();
            DataContext = _model = model;
            var names = new[] { "Top-left corner", "Top-right corner", "Bottom-right corner", "Bottom-left corner" };
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var template = new ControlTemplate(typeof(Thumb));
                var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
                dot.SetValue(System.Windows.Shapes.Shape.FillProperty, new SolidColorBrush(Color.FromArgb(200, 90, 200, 250)));
                dot.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
                dot.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
                template.VisualTree = dot;
                var thumb = new Thumb { Width = HandleSize, Height = HandleSize, Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = names[i], Template = template };
                System.Windows.Automation.AutomationProperties.SetName(thumb, names[i]);
                thumb.DragDelta += (_, e) => _model.DragCorner(index, e.HorizontalChange, e.VerticalChange);
                _handles[i] = thumb;
                Handles.Children.Add(thumb);
            }
            _model.CornersChanged += (_, _) => PlaceHandles();
            PlaceHandles();
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

        private void Reset_Click(object sender, RoutedEventArgs e) => _model.Reset();

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
