namespace GrayCat.UI.Performance;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

/// <summary>
/// Налаштування рендерингу для максимальної продуктивності
/// </summary>
public static class RenderingOptimizer
{
    /// <summary>
    /// Застосувати оптимізації до елемента
    /// </summary>
    public static void OptimizeElement(UIElement element)
    {
        // Увімкнути апаратне прискорення
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.LowQuality);
        RenderOptions.SetCachingHint(element, CachingHint.Cache);

        // Оптимізація EdgeMode для чітких країв
        RenderOptions.SetEdgeMode(element, EdgeMode.Aliased);

        // Кешування для складних візуальних елементів
        if (element is FrameworkElement fe)
        {
            fe.CacheMode = new BitmapCache
            {
                EnableClearType = false,
                RenderAtScale = 1.0,
                SnapsToDevicePixels = true
            };
        }
    }

    /// <summary>
    /// Оптимізувати для переміщення (менше якість, більше швидкість)
    /// </summary>
    public static void OptimizeForDragging(UIElement element)
    {
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.LowQuality);
        RenderOptions.SetEdgeMode(element, EdgeMode.Aliased);
    }

    /// <summary>
    /// Відновити якість після переміщення
    /// </summary>
    public static void RestoreQuality(UIElement element)
    {
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(element, EdgeMode.Unspecified);
    }

    /// <summary>
    /// Глобальні налаштування для всього додатку
    /// </summary>
    public static void ApplyGlobalOptimizations()
    {
        // Увімкнути апаратне прискорення
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        // Зменшити затримку візуалізації
        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline),
            new FrameworkPropertyMetadata { DefaultValue = 60 });
    }
}