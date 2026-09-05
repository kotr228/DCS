using System;
using JolieCat.Shared.Documents;
using JolieCat.Shared.Enums;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Default <see cref="ILayer"/> implementation backing the in-memory scene graph.
    /// </summary>
    public sealed class Layer : ILayer
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get; set; }

        public LayerType Type { get; }

        public bool IsVisible { get; set; } = true;

        public bool IsLocked { get; set; }

        private double _opacity = 1.0;

        public double Opacity
        {
            get => _opacity;
            set => _opacity = Math.Clamp(value, 0.0, 1.0);
        }

        public BlendMode BlendMode { get; set; } = BlendMode.Normal;

        public Layer(string name, LayerType type = LayerType.Raster)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Layer name cannot be empty.", nameof(name));

            Name = name;
            Type = type;
        }
    }
}
