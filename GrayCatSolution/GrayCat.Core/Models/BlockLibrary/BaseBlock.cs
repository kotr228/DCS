namespace GrayCat.Core.Models.BlockLibrary;

public abstract class BaseBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public abstract string BlockType { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract Dictionary<string, object> DefaultProperties { get; }

    public virtual string GenerateReactCode()
    {
        return $"<div className=\"block-{BlockType.ToLower()}\">/* {DisplayName} */</div>";
    }
}