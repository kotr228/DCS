namespace GrayCat.Tests;

using System.Text.Json;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.Core.Serialization;
using GrayCat.Shared.Models;

public class SerializationTests
{
    [Fact]
    public void ProjectFile_RoundTrips_WithEnumAsString()
    {
        var project = TestData.FullProject(ProjectType.NextJs);
        var json = JsonSerializer.Serialize(project, GrayCatJson.Options);

        Assert.Contains("\"Type\": \"NextJs\"", json);
        var loaded = JsonSerializer.Deserialize<ProjectModel>(json, GrayCatJson.Options)!;
        Assert.Equal(ProjectType.NextJs, loaded.Type);
        Assert.Equal(project.Blocks.Count, loaded.Blocks.Count);
    }

    [Fact]
    public void OldServiceMetadataFile_StillLoads()
    {
        // Format written by the previous service: camelCase, type as a string, blocks list.
        const string json = """
        { "projectName": "Old", "author": "A", "type": "React", "createdAt": "2025-01-01T00:00:00Z", "version": "1.0.0", "blocks": [] }
        """;
        var loaded = JsonSerializer.Deserialize<ProjectModel>(json, GrayCatJson.Options)!;
        Assert.Equal("Old", loaded.ProjectName);
        Assert.Equal(ProjectType.React, loaded.Type);
    }

    [Fact]
    public void NumericEnumValues_StillLoad()
    {
        var loaded = JsonSerializer.Deserialize<ProjectModel>("{\"Type\":2}", GrayCatJson.Options)!;
        Assert.Equal(ProjectType.NodeJsApi, loaded.Type);
    }

    [Fact]
    public void LoadedProperties_AreNormalisedForTheEditor()
    {
        var block = TestData.Block("Button", 0, b => b.Properties["fullWidth"] = true);
        var json = JsonSerializer.Serialize(block, GrayCatJson.Options);
        var loaded = JsonSerializer.Deserialize<BlockModel>(json, GrayCatJson.Options)!;

        Assert.IsType<JsonElement>(loaded.Properties["fullWidth"]);
        PropertyValues.NormalizeForEditing(loaded.Properties);
        Assert.Equal(true, loaded.Properties["fullWidth"]);
        Assert.Equal("Click Me", loaded.Properties["text"]);
    }

    [Fact]
    public void ListProperties_AreEditedAsJsonText()
    {
        var block = TestData.Block("Navbar", 0);
        var menu = Assert.IsType<string>(block.Properties["menuItems"]);
        Assert.StartsWith("[", menu);
        Assert.Equal(3, PropertyValues.AsList(menu).Count);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("garbage", false)]
    public void AsBool_HandlesStrings(string input, bool expected) =>
        Assert.Equal(expected, PropertyValues.AsBool(input));

    [Fact]
    public void AsDouble_AcceptsCommaDecimal() =>
        Assert.Equal(0.5, PropertyValues.AsDouble("0,5"));
}
