using JolieCat.UI.Media;

namespace JolieCat.UI.Tests
{
    public class RecentFilesTests
    {
        [Fact]
        public void Promote_MovesToFrontWithoutDuplicates()
        {
            var list = RecentFilesStore.Promote(new[] { @"C:\a.jolie", @"C:\b.psd", @"C:\c.png" }, @"c:\B.PSD");

            Assert.Equal(new[] { @"c:\B.PSD", @"C:\a.jolie", @"C:\c.png" }, list);
        }

        [Fact]
        public void Promote_CapsTheList()
        {
            var many = Enumerable.Range(0, 15).Select(i => $@"C:\{i}.png");

            var list = RecentFilesStore.Promote(many, @"C:\new.png");

            Assert.Equal(RecentFilesStore.MaxEntries, list.Count);
            Assert.Equal(@"C:\new.png", list[0]);
        }
    }
}
