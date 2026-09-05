using System;
using System.Collections.Generic;

namespace JolieCat.Shared.Dtos
{
    /// <summary>
    /// Serialization-friendly snapshot of a scene and its layers.
    /// </summary>
    public sealed class SceneDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<LayerDto> Layers { get; set; } = new();
    }
}
