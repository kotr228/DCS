using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace JolieCat.UI.Localization
{
    /// <summary>
    /// Translates the interface as it appears: every element, when it's loaded, has its
    /// own written-in texts (menu headers, button and check box captions, labels, tooltips,
    /// window titles) swapped for the current language. Texts that come from bindings are
    /// left alone (those go through <see cref="TranslateConverter"/> where needed). The
    /// English originals are remembered, so switching language - back to English too - just
    /// re-translates what's on screen.
    /// </summary>
    public static class LocalizationAgent
    {
        /// <summary>Per element and property: the English original and the text last put there.</summary>
        private sealed class Remembered { public string English = ""; public string Shown = ""; }

        private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Remembered>> Originals = new();
        private static bool _attached;

        /// <summary>Starts translating every element as it loads (call once at startup).</summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;
            // Loaded only reaches elements with a handler of their own, so the first layout
            // (SizeChanged, which every element shown gets) is where texts are translated.
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler((s, _) => { if (s is DependencyObject d) Translate(d); }), handledEventsToo: true);
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent,
                new RoutedEventHandler((s, _) => { if (s is ToolTip t) Translate(t); }), handledEventsToo: true);
            Loc.Changed += RetranslateOpenWindows;
        }

        /// <summary>The written-in text properties worth translating on <paramref name="d"/>.</summary>
        private static IEnumerable<DependencyProperty> TextProperties(DependencyObject d)
        {
            yield return FrameworkElement.ToolTipProperty;
            switch (d)
            {
                case HeaderedItemsControl: yield return HeaderedItemsControl.HeaderProperty; break;
                case HeaderedContentControl: yield return HeaderedContentControl.HeaderProperty; yield return ContentControl.ContentProperty; break;
                case ContentControl: yield return ContentControl.ContentProperty; break;
                case TextBlock: yield return TextBlock.TextProperty; break;
            }
            if (d is Window) yield return Window.TitleProperty;
            if (d is Controls.LabeledSlider) yield return Controls.LabeledSlider.LabelProperty;
        }

        /// <summary>Translates <paramref name="d"/>'s own written-in texts.</summary>
        public static void Translate(DependencyObject d)
        {
            if (d is TextBox) return; // what the user types is never touched
            foreach (var property in TextProperties(d))
            {
                // Written in the XAML (directly or in a template), not bound or styled.
                var source = DependencyPropertyHelper.GetValueSource(d, property);
                if (source.IsExpression || source.IsAnimated || source.IsCoerced
                    || source.BaseValueSource is not (BaseValueSource.Local or BaseValueSource.ParentTemplate)) continue;
                var local = d.GetValue(property);
                if (local is not (string or ToolTip)) continue;
                var remembered = Originals.GetOrCreateValue(d);
                if (local is string text)
                {
                    // New, or changed by the program since we last translated it: that's the original now.
                    if (!remembered.TryGetValue(property, out var r) || text != r.Shown)
                        remembered[property] = r = new Remembered { English = text, Shown = text };
                    var translated = Loc.T(r.English);
                    r.Shown = translated;
                    if (text != translated) d.SetValue(property, translated);
                }
                else if (local is ToolTip tip && tip.Content is string tipText && property == FrameworkElement.ToolTipProperty)
                {
                    if (!remembered.TryGetValue(property, out var r) || tipText != r.Shown)
                        remembered[property] = r = new Remembered { English = tipText, Shown = tipText };
                    r.Shown = Loc.T(r.English);
                    tip.Content = r.Shown;
                }
            }
        }

        /// <summary>Bindings that go through <see cref="TranslateConverter"/> read their text again.</summary>
        private static void RefreshTranslatedBindings(DependencyObject d)
        {
            foreach (var property in new[] { FrameworkElement.ToolTipProperty, TextBlock.TextProperty, HeaderedContentControl.HeaderProperty,
                         ContentControl.ContentProperty, Window.TitleProperty, Controls.LabeledSlider.LabelProperty })
                if (BindingOperations.GetBindingExpression(d, property) is { ParentBinding.Converter: TranslateConverter } expression)
                    expression.UpdateTarget();
        }

        /// <summary>Re-translates everything in the open windows (after a language switch).</summary>
        private static void RetranslateOpenWindows()
        {
            if (Application.Current is null) return;
            foreach (Window window in Application.Current.Windows) TranslateTree(window);
        }

        private static void TranslateTree(DependencyObject root)
        {
            var seen = new HashSet<DependencyObject>();
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (!seen.Add(d)) continue;
                Translate(d);
                RefreshTranslatedBindings(d);
                if (d is FrameworkElement { ContextMenu: { } menu }) stack.Push(menu);
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                    if (child is DependencyObject c) stack.Push(c);
                if (d is Visual or System.Windows.Media.Media3D.Visual3D)
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
        }
    }

    /// <summary>Translates a bound text (a tool's name, a dialog's label) into the current language.</summary>
    public sealed class TranslateConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string s ? Loc.T(s) : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
    }
}
