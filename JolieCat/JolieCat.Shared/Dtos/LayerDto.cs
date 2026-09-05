using System;
using JolieCat.Shared.Enums;

namespace JolieCat.Shared.Dtos
{
    /// <summary>
    /// Serialization-friendly snapshot of a layer, used for <c>.jolie</c> project
    /// files and for IPC between <c>JolieCat.UI</c> and <c>JolieCat.Service</c>.
    /// </summary>
    public sealed class LayerDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public LayerType Type { get; set; } = LayerType.Raster;

        public bool IsVisible { get; set; } = true;

        public bool IsLocked { get; set; }

        public double Opacity { get; set; } = 1.0;

        public BlendMode BlendMode { get; set; } = BlendMode.Normal;
    }
}
