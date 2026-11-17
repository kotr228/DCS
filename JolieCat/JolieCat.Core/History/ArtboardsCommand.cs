using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;

namespace JolieCat.Core.History
{
    /// <summary>Undoes/redoes a change to the document's artboards (added, removed, renamed).</summary>
    public sealed class ArtboardsCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly IReadOnlyList<Artboard> _before;
        private readonly IReadOnlyList<Artboard> _after;

        public ArtboardsCommand(Scene scene, IEnumerable<Artboard> before, IEnumerable<Artboard> after)
        {
            _scene = scene;
            _before = before.ToList();
            _after = after.ToList();
        }

        public void Undo() => _scene.RestoreArtboards(_before);

        public void Redo() => _scene.RestoreArtboards(_after);
    }
}
