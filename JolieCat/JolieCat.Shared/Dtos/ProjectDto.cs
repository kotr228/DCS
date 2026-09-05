using System;

namespace JolieCat.Shared.Dtos
{
    /// <summary>
    /// Root object persisted to a <c>.jolie</c> project file by <c>JolieCat.Service</c>.
    /// </summary>
    public sealed class ProjectDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Absolute path on disk, or <c>null</c> for an unsaved project.</summary>
        public string? FilePath { get; set; }

        public SceneDto Scene { get; set; } = new();

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset ModifiedAtUtc { get; set; }
    }
}
