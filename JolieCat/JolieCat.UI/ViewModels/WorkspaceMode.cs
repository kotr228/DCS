namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Which content the center workspace area shows for one document tab: the
    /// ordinary static-image <see cref="Design"/> canvas, or the dedicated
    /// <see cref="Timeline"/> workspace (see <c>Views.Timeline.TimelineWorkspaceView</c>)
    /// built for a <see cref="Shared.Enums.ProjectType.ClipbarAnimation"/> project's
    /// frame-by-frame work. Purely a per-session UI choice - not persisted in the
    /// <c>.jolie</c> file - but <see cref="DocumentViewModel"/> defaults it from the
    /// document's own <see cref="Shared.Enums.ProjectType"/> on load/creation, so a
    /// Clipbar Animation project always opens straight into <see cref="Timeline"/>
    /// rather than requiring an extra click every time.
    /// </summary>
    public enum WorkspaceMode
    {
        Design,
        Timeline,
    }
}
