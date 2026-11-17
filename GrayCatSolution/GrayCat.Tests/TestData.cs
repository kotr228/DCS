namespace GrayCat.Tests;

using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Shared.Models;

internal static class TestData
{
    public static BlockModel Block(string type, double y, Action<BlockModel>? configure = null)
    {
        var block = new BlockModel { Type = type, PositionX = 40, PositionY = y, Content = BlockDefaults.DefaultContent(type) };
        block.InitializeDefaultProperties();
        configure?.Invoke(block);
        return block;
    }

    /// <summary>A project that uses every block type in the palette.</summary>
    public static ProjectModel FullProject(ProjectType type = ProjectType.React)
    {
        var y = 0.0;
        var project = new ProjectModel { ProjectName = "Demo Site", Author = "Tester", Type = type };
        foreach (var blockType in BlockDefaults.BlockTypes)
        {
            project.Blocks.Add(Block(blockType, y += 120));
        }
        project.Blocks.First(b => b.Type == "Video").Properties["videoUrl"] = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        project.Blocks.First(b => b.Type == "Image").Properties["src"] = "https://picsum.photos/800/400";
        return project;
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "graycat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
