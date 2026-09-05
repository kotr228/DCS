using System;
using System.Collections.Generic;
using JolieCat.Shared.Documents;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Default <see cref="IScene"/> implementation: an ordered list of layers.
    /// </summary>
    public sealed class Scene : IScene
    {
        private readonly List<ILayer> _layers = new();

        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; }

        public IReadOnlyList<ILayer> Layers => _layers;

        public Scene(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scene name cannot be empty.", nameof(name));

            Name = name;
        }

        public void AddLayer(ILayer layer)
        {
            ArgumentNullException.ThrowIfNull(layer);
            _layers.Add(layer);
        }

        public bool RemoveLayer(ILayer layer) => _layers.Remove(layer);
    }
}
