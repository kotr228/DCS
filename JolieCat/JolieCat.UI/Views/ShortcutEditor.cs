using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.UI.Media;

namespace JolieCat.UI.Views
{
    /// <summary>One window shortcut: what it does, its built-in key and the key in use.</summary>
    public sealed partial class ShortcutRow : ObservableObject
    {
        public string Id { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Default { get; init; } = string.Empty;
        internal List<KeyBinding> Bindings { get; } = new();

        [ObservableProperty] private string gesture = string.Empty;
        [ObservableProperty] private string problem = string.Empty;
    }

    /// <summary>
    /// Edit &gt; Keyboard Shortcuts: every key binding of the main window, editable. A binding
    /// is identified by its command (and parameter), so the change survives restarts.
    /// </summary>
    public static class ShortcutEditor
    {
        private static readonly KeyGestureConverter Converter = new();

        /// <summary>The id of a key binding: its command's binding path, plus the parameter.</summary>
        internal static string IdOf(KeyBinding binding)
        {
            var path = BindingOperations.GetBinding(binding, InputBinding.CommandProperty)?.Path?.Path ?? binding.Command?.GetType().Name ?? "?";
            return binding.CommandParameter is { } p ? $"{path}|{p}" : path;
        }

        internal static string Text(KeyGesture? g) =>
            g is null ? string.Empty : (string)(Converter.ConvertTo(null, System.Globalization.CultureInfo.InvariantCulture, g, typeof(string)) ?? string.Empty);

        /// <summary>"Ctrl+Shift+K" as a gesture; null for empty or nonsense (with the reason).</summary>
        internal static KeyGesture? Parse(string text, out string problem)
        {
            problem = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                if (Converter.ConvertFrom(null, System.Globalization.CultureInfo.InvariantCulture, text.Trim()) is KeyGesture g) return g;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
            {
            }
            problem = "not a key (try Ctrl+Shift+K, F5, Alt+Ctrl+1)";
            return null;
        }

        /// <summary>The window's key bindings as rows (bindings sharing a command are one row),
        /// named from the menu item that runs the same command where there is one.</summary>
        public static List<ShortcutRow> Rows(Window window, Menu? menu)
        {
            var names = new Dictionary<string, string>();
            if (menu is not null) CollectNames(menu.Items, "", names);
            var rows = new Dictionary<string, ShortcutRow>();
            var defaults = DefaultGestures(window);
            foreach (var kb in window.InputBindings.OfType<KeyBinding>())
            {
                var id = IdOf(kb);
                if (!rows.TryGetValue(id, out var row))
                {
                    var action = names.TryGetValue(id, out var n) ? n : Humanize(id);
                    row = new ShortcutRow { Id = id, Action = action, Default = defaults.TryGetValue(id, out var d) ? d : Text(kb.Gesture as KeyGesture), Gesture = Text(kb.Gesture as KeyGesture) };
                    rows[id] = row;
                }
                row.Bindings.Add(kb);
            }
            return rows.Values.OrderBy(r => r.Action, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CollectNames(ItemCollection items, string prefix, Dictionary<string, string> names)
        {
            foreach (var item in items.OfType<MenuItem>())
            {
                var header = (item.Header as string ?? string.Empty).Replace("_", "").Replace("...", "");
                var full = prefix.Length == 0 ? header : $"{prefix} > {header}";
                var path = BindingOperations.GetBinding(item, MenuItem.CommandProperty)?.Path?.Path;
                if (path is not null)
                {
                    var id = item.CommandParameter is { } p ? $"{path}|{p}" : path;
                    names.TryAdd(id, full);
                }
                CollectNames(item.Items, full, names);
            }
        }

        private static string Humanize(string id)
        {
            var path = id.Split('|')[0];
            var last = path.Split('.').Last();
            if (last.EndsWith("Command", StringComparison.Ordinal)) last = last[..^7];
            var words = System.Text.RegularExpressions.Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ");
            return id.Contains('|') ? $"{words} ({id.Split('|')[1]})" : words;
        }

        private static readonly Dictionary<Window, Dictionary<string, string>> Originals = new();

        /// <summary>The built-in key of every binding (remembered the first time).</summary>
        private static Dictionary<string, string> DefaultGestures(Window window)
        {
            if (Originals.TryGetValue(window, out var known)) return known;
            known = new Dictionary<string, string>();
            foreach (var kb in window.InputBindings.OfType<KeyBinding>())
                known.TryAdd(IdOf(kb), Text(kb.Gesture as KeyGesture));
            Originals[window] = known;
            return known;
        }

        /// <summary>Puts the saved shortcuts into effect (call once the window is built).</summary>
        public static void ApplySaved(Window window, Menu? menu)
        {
            DefaultGestures(window);
            var saved = ShortcutStore.Load();
            if (saved.Count == 0) return;
            foreach (var row in Rows(window, menu))
                if (saved.TryGetValue(row.Id, out var text)) Apply(row, text, menu);
        }

        /// <summary>Sets a row's key: every binding of its command gets it (the first one;
        /// extra built-in alternatives are cleared), and the menu shows it.</summary>
        private static void Apply(ShortcutRow row, string text, Menu? menu)
        {
            var gesture = Parse(text, out _);
            for (var i = 0; i < row.Bindings.Count; i++)
            {
                var kb = row.Bindings[i];
                if (i == 0 && gesture is not null) { kb.Gesture = gesture; }
                else { kb.Key = Key.None; kb.Modifiers = ModifierKeys.None; }
            }
            row.Gesture = Text(gesture);
            if (menu is not null) SetMenuText(menu.Items, row.Id, row.Gesture);
        }

        private static void SetMenuText(ItemCollection items, string id, string text)
        {
            foreach (var item in items.OfType<MenuItem>())
            {
                var path = BindingOperations.GetBinding(item, MenuItem.CommandProperty)?.Path?.Path;
                if (path is not null && (item.CommandParameter is { } p ? $"{path}|{p}" : path) == id) item.InputGestureText = text;
                SetMenuText(item.Items, id, text);
            }
        }

        /// <summary>Shows the editor. Changes apply at OK and are kept for next time.</summary>
        public static void Show(Window owner, Menu? menu)
        {
            var rows = Rows(owner, menu);
            foreach (var r in rows) r.Problem = string.Empty;
            var list = new ObservableCollection<ShortcutRow>(rows);

            var panel = new StackPanel();
            foreach (var row in list)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var box = new TextBox { Width = 150, Text = row.Gesture, ToolTip = $"Built in: {(row.Default.Length == 0 ? "none" : row.Default)}. Type a key like Ctrl+Shift+K, or leave empty for none." };
                System.Windows.Automation.AutomationProperties.SetName(box, row.Action);
                box.SetBinding(TextBox.TextProperty, new Binding(nameof(ShortcutRow.Gesture)) { Source = row, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                DockPanel.SetDock(box, Dock.Right);
                var problem = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, FontSize = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                problem.SetBinding(TextBlock.TextProperty, new Binding(nameof(ShortcutRow.Problem)) { Source = row });
                DockPanel.SetDock(problem, Dock.Right);
                line.Children.Add(box);
                line.Children.Add(problem);
                line.Children.Add(new TextBlock { Text = row.Action, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimaryBrush") });
                panel.Children.Add(line);
            }

            var window = new Window
            {
                Title = "Keyboard Shortcuts",
                Owner = owner,
                Width = 620,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("PanelBackgroundBrush"),
            };
            var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(20, 4, 20, 4), Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0) };
            var reset = new Button { Content = "Reset All", Padding = new Thickness(12, 4, 12, 4), ToolTip = "Every shortcut back to its built-in key" };
            reset.Click += (_, _) => { foreach (var r in list) r.Gesture = r.Default; };
            ok.Click += (_, _) =>
            {
                if (Validate(list)) window.DialogResult = true;
            };
            var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            DockPanel.SetDock(cancel, Dock.Right);
            DockPanel.SetDock(ok, Dock.Right);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            buttons.Children.Add(reset);
            var hint = new TextBlock
            {
                Text = "Type a new key for any command (Ctrl+Shift+K, F5, Alt+Ctrl+1), or clear it for none. Single-letter tool keys (B, V, M...) are set by the tools themselves.",
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8),
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondaryBrush"),
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(hint, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(hint);
            root.Children.Add(buttons);
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = root;
            if (window.ShowDialog() != true) return;

            var saved = new Dictionary<string, string>();
            foreach (var row in list)
            {
                var text = Text(Parse(row.Gesture, out _));
                Apply(row, text, menu);
                if (!string.Equals(text, row.Default, StringComparison.OrdinalIgnoreCase)) saved[row.Id] = text;
            }
            ShortcutStore.Save(saved);
        }

        /// <summary>Marks keys that aren't keys, or are used twice. True when all is fine.</summary>
        internal static bool Validate(IList<ShortcutRow> rows)
        {
            var fine = true;
            var seen = new Dictionary<string, ShortcutRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                row.Problem = string.Empty;
                var gesture = Parse(row.Gesture, out var problem);
                if (problem.Length > 0) { row.Problem = problem; fine = false; continue; }
                var text = Text(gesture);
                if (text.Length == 0) continue;
                if (seen.TryGetValue(text, out var other))
                {
                    row.Problem = $"also used by {other.Action}";
                    fine = false;
                }
                else seen[text] = row;
            }
            return fine;
        }
    }
}
