using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using JolieCat.UI.Localization;

namespace JolieCat.UI.Tests
{
    /// <summary>The interface languages: the dictionaries load, texts switch and switch back.</summary>
    public class LocalizationTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { test(); }
                catch (Exception ex) { error = ex; }
                finally { Loc.SetLanguage("en"); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        [Theory]
        [InlineData("uk")]
        [InlineData("ru")]
        public void Dictionary_LoadsAndCoversTheMenus(string code)
        {
            var all = Loc.All(code);
            Assert.True(all.Count > 1000, $"{code}: {all.Count} entries");
            Assert.All(all, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
            // XAML attributes arrive unescaped: keys hold "&", never "&amp;".
            Assert.DoesNotContain(all.Keys, k => k.Contains("&amp;") || k.Contains("&gt;"));
            foreach (var menu in new[] { "File", "Edit", "Image", "Layer", "Select", "View", "Help", "Cancel", "Layer Style" })
                Assert.True(all.ContainsKey(menu) || all.ContainsKey("_" + menu), $"{code}: {menu}");
        }

        [Fact]
        public void T_UsesFallbacksAndEnglishStaysEnglish() => Sta(() =>
        {
            Loc.SetLanguage("uk");
            Assert.Equal("Скасувати", Loc.T("Cancel"));
            Assert.Equal("_Файл", Loc.T("_File"));
            Assert.Equal("Розмір полотна...", Loc.T("Canvas _Size..."));
            Assert.Equal("Ласо (L)", Loc.T("Lasso (L)"));
            Assert.Equal("No such text 123", Loc.T("No such text 123"));

            Loc.SetLanguage("ru");
            Assert.Equal("Отмена", Loc.T("Cancel"));

            Loc.SetLanguage("en");
            Assert.Equal("Cancel", Loc.T("Cancel"));
        });

        [Fact]
        public void Agent_TranslatesAndRestoresElementTexts() => Sta(() =>
        {
            var button = new Button { Content = "Cancel", ToolTip = "Undo (Ctrl+Z)" };
            var text = new TextBlock { Text = "Opacity" };

            Loc.SetLanguage("uk");
            LocalizationAgent.Translate(button);
            LocalizationAgent.Translate(text);
            Assert.Equal("Скасувати", button.Content);
            Assert.Equal("Скасувати (Ctrl+Z)", button.ToolTip);
            Assert.Equal("Непрозорість", text.Text);

            Loc.SetLanguage("ru");
            LocalizationAgent.Translate(button);
            Assert.Equal("Отмена", button.Content);

            // Text set by the program after translation becomes the new original.
            text.Text = "Brightness";
            LocalizationAgent.Translate(text);
            Assert.Equal("Яркость", text.Text);

            Loc.SetLanguage("en");
            LocalizationAgent.Translate(button);
            LocalizationAgent.Translate(text);
            Assert.Equal("Cancel", button.Content);
            Assert.Equal("Brightness", text.Text);
        });

        [Fact]
        public void ShownWindow_TranslatesWrittenInAndTemplateTexts() => Sta(() =>
        {
            LocalizationAgent.Attach();
            Loc.SetLanguage("uk");
            var window = (Window)XamlReader.Parse(
                "<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Width='300' Height='200' ShowInTaskbar='False' WindowStyle='None'>" +
                "<Window.Resources><DataTemplate x:Key='T'><TextBlock Text='Mode'/></DataTemplate></Window.Resources>" +
                "<StackPanel><Menu><MenuItem Header='_File'/></Menu><Button Content='Save'/>" +
                "<TabControl><TabItem Header='PROPERTIES'/></TabControl><ContentControl Content='x' ContentTemplate='{StaticResource T}'/></StackPanel></Window>");
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            try
            {
                var panel = (StackPanel)window.Content;
                Assert.Equal("_Файл", ((MenuItem)((Menu)panel.Children[0]).Items[0]).Header);
                Assert.Equal("Зберегти", ((Button)panel.Children[1]).Content);
                Assert.Equal("ВЛАСТИВОСТІ", ((TabItem)((TabControl)panel.Children[2]).Items[0]).Header);
                Assert.Equal("Режим", Find<TextBlock>((ContentControl)panel.Children[3])!.Text);

                // Back to English: the remembered original returns (the app re-translates
                // its open windows; there's no Application in tests, so done by hand).
                Loc.SetLanguage("en");
                LocalizationAgent.Translate(panel.Children[1]);
                Assert.Equal("Save", ((Button)panel.Children[1]).Content);
            }
            finally { window.Close(); }
        });

        private static T? Find<T>(DependencyObject d) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is T match) return match;
                if (Find<T>(child) is { } deeper) return deeper;
            }
            return null;
        }
    }
}
