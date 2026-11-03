using System.IO;

namespace JolieCat.UI.Tests
{
    public class ResourceDictionaryTests
    {
        /// <summary>The tool-options templates are merged before the theme colors, so a
        /// StaticResource there can't find them and crashes the app as soon as that tool
        /// is picked (this happened with Red Eye and Puppet Warp). DynamicResource works.</summary>
        [Fact]
        public void ToolOptionsTemplates_UseOnlyDynamicResources()
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "JolieCat.UI", "Resources"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            var xaml = File.ReadAllText(Path.Combine(dir!, "JolieCat.UI", "Resources", "ToolOptionsTemplates.xaml"));
            Assert.DoesNotContain("StaticResource", xaml);
        }
    }
}
