namespace JolieCat.Shared.Enums
{
    /// <summary>
    /// What kind of project a <c>.jolie</c> document is - chosen once, when the project
    /// is created, and persisted alongside it (see <c>JolieCat.Core.Serialization.ProjectManifest.ProjectType</c>).
    /// Drives which tools/panels the workspace shows: a <see cref="StandardImage"/>
    /// project is the plain raster/vector editor with none of the below; a
    /// <see cref="SpriteSheet"/> project additionally gets the slicing grid overlay and
    /// panel (see <c>JolieCat.Core.Documents.SpriteSheetGrid</c>); a
    /// <see cref="ClipbarAnimation"/> project gets the dedicated Timeline workspace tab
    /// (see <c>JolieCat.UI.ViewModels.WorkspaceMode</c>) instead of a small bottom-docked
    /// panel every project used to carry regardless of whether it needed one.
    /// </summary>
    public enum ProjectType
    {
        StandardImage,
        SpriteSheet,
        ClipbarAnimation,
    }
}
