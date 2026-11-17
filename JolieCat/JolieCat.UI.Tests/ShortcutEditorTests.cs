using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using JolieCat.UI.Media;
using JolieCat.UI.Views;

namespace JolieCat.UI.Tests
{
    public class ShortcutEditorTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        public sealed class Commands
        {
            public int Saved, Zoomed;
            public IRelayCommand SaveCommand { get; }
            public IRelayCommand ZoomCommand { get; }
            public Commands() { SaveCommand = new RelayCommand(() => Saved++); ZoomCommand = new RelayCommand(() => Zoomed++); }
        }

        private static (Window Window, Menu Menu) Build()
        {
            var window = new Window { DataContext = new Commands() };
            KeyBinding Bind(Key key, ModifierKeys mods, string path)
            {
                var kb = new KeyBinding { Key = key, Modifiers = mods };
                BindingOperations.SetBinding(kb, InputBinding.CommandProperty, new Binding(path) { Source = window.DataContext });
                return kb;
            }
            window.InputBindings.Add(Bind(Key.S, ModifierKeys.Control, "SaveCommand"));
            window.InputBindings.Add(Bind(Key.OemPlus, ModifierKeys.Control, "ZoomCommand"));
            window.InputBindings.Add(Bind(Key.Add, ModifierKeys.Control, "ZoomCommand"));     // a second key for the same thing
            var menu = new Menu();
            var file = new MenuItem { Header = "_File" };
            var save = new MenuItem { Header = "Save..." };
            BindingOperations.SetBinding(save, MenuItem.CommandProperty, new Binding("SaveCommand") { Source = window.DataContext });
            file.Items.Add(save);
            menu.Items.Add(file);
            return (window, menu);
        }

        [Fact]
        public void Rows_Validation_AndSavedKeys() => Sta(() =>
        {
            var (window, menu) = Build();
            var rows = ShortcutEditor.Rows(window, menu);
            Assert.Equal(2, rows.Count);
            var save = rows.First(r => r.Id == "SaveCommand");
            Assert.Equal("File > Save", save.Action);
            Assert.Equal("Ctrl+S", save.Gesture);
            Assert.Equal(2, rows.First(r => r.Id == "ZoomCommand").Bindings.Count);

            save.Gesture = "Ctrl+Shift+Q+Z";
            Assert.False(ShortcutEditor.Validate(rows));
            Assert.NotEmpty(save.Problem);
            save.Gesture = rows.First(r => r.Id == "ZoomCommand").Gesture;      // a clash
            Assert.False(ShortcutEditor.Validate(rows));
            Assert.Contains("also used", save.Problem + rows.First(r => r.Id == "ZoomCommand").Problem);
            save.Gesture = "Alt+F9";
            Assert.True(ShortcutEditor.Validate(rows));

            var file = Path.Combine(Path.GetTempPath(), $"keys-{Guid.NewGuid():N}.json");
            var old = ShortcutStore.FilePath;
            ShortcutStore.FilePath = file;
            try
            {
                ShortcutStore.Save(new Dictionary<string, string> { ["SaveCommand"] = "Alt+F9", ["ZoomCommand"] = "" });
                var (fresh, freshMenu) = Build();
                ShortcutEditor.ApplySaved(fresh, freshMenu);
                var bindings = fresh.InputBindings.OfType<KeyBinding>().ToList();
                Assert.Equal(Key.F9, bindings[0].Key);
                Assert.Equal(ModifierKeys.Alt, bindings[0].Modifiers);
                Assert.Equal(Key.None, bindings[1].Key);                       // cleared
                Assert.Equal(Key.None, bindings[2].Key);
                Assert.Equal("Alt+F9", ((MenuItem)((MenuItem)freshMenu.Items[0]).Items[0]).InputGestureText);
            }
            finally
            {
                ShortcutStore.FilePath = old;
                File.Delete(file);
            }
        });
    }
}
