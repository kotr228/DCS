using System;
using System.Collections.Generic;

namespace JolieCat.Shared.Documents
{
    /// <summary>
    /// An ordered stack of layers that compose a single canvas.
    /// </summary>
    public interface IScene
    {
        Guid Id { get; }

        string Name { get; set; }

        /// <summary>Layers ordered back-to-front (index 0 renders first, i.e. is furthest back).</summary>
        IReadOnlyList<ILayer> Layers { get; }

        void AddLayer(ILayer layer);

        bool RemoveLayer(ILayer layer);
    }
}
