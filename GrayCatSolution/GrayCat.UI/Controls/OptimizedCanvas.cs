namespace GrayCat.UI.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Оптимізований Canvas з кешуванням для покращення продуктивності
/// </summary>
public class OptimizedCanvas : Canvas
{
    private bool _isInvalidated = false;

    static OptimizedCanvas()
    {
        // Увімкнути кешування для покращення продуктивності
        CacheModeProperty.OverrideMetadata(
            typeof(OptimizedCanvas),
            new FrameworkPropertyMetadata(new BitmapCache { RenderAtScale = 1.0 }));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Рендеримо тільки якщо є зміни
        if (_isInvalidated || Children.Count == 0)
        {
            base.OnRender(dc);
            _isInvalidated = false;
        }
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        _isInvalidated = true;
    }

    /// <summary>
    /// Оновити тільки позицію елемента без повного rerenderа
    /// </summary>
    public void UpdateChildPosition(UIElement child, double x, double y)
    {
        SetLeft(child, x);
        SetTop(child, y);

        // Оновлюємо тільки цей елемент
        child.InvalidateVisual();
    }

    /// <summary>
    /// Батч-оновлення позицій (для множинного вибору в майбутньому)
    /// </summary>
    public void BeginBatchUpdate()
    {
        // Відключаємо автоматичне оновлення
        IsHitTestVisible = false;
    }

    public void EndBatchUpdate()
    {
        IsHitTestVisible = true;
        _isInvalidated = true;
        InvalidateVisual();
    }
}
