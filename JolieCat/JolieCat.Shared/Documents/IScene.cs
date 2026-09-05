using System;
using System.Collections.Generic;

namespace JolieCat.Shared.Documents
{
    /// <summary>
    /// An ordered stack of layers that compose a single canvas.
    /// </summary>
    /// <remarks>
    /// Not currently implemented by <c>JolieCat.Core.Documents.Scene</c> - see that
    /// class's remarks for why. Kept here as the contract a second implementation (e.g.
    /// a serialized-project loader) would conform to.
    /// </remarks>
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
