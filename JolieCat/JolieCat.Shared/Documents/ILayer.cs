using System;
using JolieCat.Shared.Enums;

namespace JolieCat.Shared.Documents
{
    /// <summary>
    /// A single addressable layer within a <see cref="IScene"/>.
    /// </summary>
    public interface ILayer
    {
        Guid Id { get; }

        string Name { get; set; }

        LayerType Type { get; }

        bool IsVisible { get; set; }

        bool IsLocked { get; set; }

        /// <summary>Opacity in the range [0.0, 1.0].</summary>
        double Opacity { get; set; }

        BlendMode BlendMode { get; set; }
    }
}
