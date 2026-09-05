using System;

namespace JolieCat.Shared.Documents
{
    /// <summary>
    /// A single open <c>.jolie</c> project: its identity, backing file, and root scene.
    /// </summary>
    public interface IDocument
    {
        Guid Id { get; }

        string Name { get; set; }

        /// <summary>Absolute path on disk, or <c>null</c> for an unsaved document.</summary>
        string? FilePath { get; set; }

        IScene Scene { get; }

        /// <summary>True when there are unsaved changes.</summary>
        bool IsDirty { get; set; }
    }
}
