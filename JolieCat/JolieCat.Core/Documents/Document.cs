using System;
using JolieCat.Shared.Documents;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Default <see cref="IDocument"/> implementation: a project's identity plus its root scene.
    /// </summary>
    public sealed class Document : IDocument
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; }

        public string? FilePath { get; set; }

        public IScene Scene { get; }

        public bool IsDirty { get; set; }

        public Document(string name, IScene? scene = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Document name cannot be empty.", nameof(name));

            Name = name;
            Scene = scene ?? new Scene($"{name} Scene");
        }
    }
}
