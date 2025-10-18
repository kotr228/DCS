namespace GrayCat.UI.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

/// <summary>
/// Легка тінь блока під час перетягування (замість переміщення самого блока)
/// </summary>
public class DragAdorner : Adorner
{
    private readonly ContentPresenter _contentPresenter;
    private Point _position;
    private readonly double _opacity;

    public DragAdorner(UIElement adornedElement, UIElement content, double opacity = 0.7)
        : base(adornedElement)
    {
        _contentPresenter = new ContentPresenter
        {
            Content = content,
            Opacity = opacity
        };
        _opacity = opacity;
    }

    public Point Position
    {
        get => _position;
        set
        {
            _position = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _contentPresenter.Measure(constraint);
        return _contentPresenter.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _contentPresenter.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Visual GetVisualChild(int index)
    {
        return _contentPresenter;
    }

    protected override int VisualChildrenCount => 1;

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        var result = new GeneralTransformGroup();
        result.Children.Add(base.GetDesiredTransform(transform));
        result.Children.Add(new TranslateTransform(_position.X, _position.Y));
        return result;
    }
}