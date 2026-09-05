using System;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// A single open <c>.jolie</c> project: its identity, backing file, and root scene.
    /// </summary>
    /// <remarks>
    /// Concrete rather than implementing <c>JolieCat.Shared.Documents.IDocument</c>, for
    /// the same reason as <see cref="Documents.Scene"/>: that interface's <c>Scene</c> is
    /// typed as <c>IScene</c>, but the only real <c>Scene</c> in this codebase is this
    /// concrete one, so implementing it here would only add casts everywhere the actual
    /// <see cref="Documents.Layer"/> pixel data is needed, for no present benefit.
    /// </remarks>
    public sealed class Document
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; }

        /// <summary>Absolute path on disk, or <c>null</c> for an unsaved document.</summary>
        public string? FilePath { get; set; }

        public Scene Scene { get; }

        /// <summary>True when there are unsaved changes.</summary>
        public bool IsDirty { get; set; }

        public Document(string name, Scene? scene = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Document name cannot be empty.", nameof(name));

            Name = name;
            Scene = scene ?? new Scene($"{name} Scene");
        }
    }
}
