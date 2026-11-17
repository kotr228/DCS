using System.Linq;
using JolieCat.Core.Documents;
using JolieCat.Core.History;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>Layers for animation frames (Timeline &gt; New Frame / Duplicate Frame /
    /// Delete Frame). These return their undo step instead of recording it, so the
    /// Timeline can record it together with its own change as one step.</summary>
    public partial class LayersViewModel
    {
        /// <summary>Adds a frame layer on top - empty, or a copy of <paramref name="copyOf"/> -
        /// and makes it active.</summary>
        public (Layer Layer, IEditCommand Undo) AddFrameLayer(string name, Layer? copyOf, System.Action<Layer>? paint = null)
        {
            Layer? added = null;
            var command = CaptureStructuralChange(() =>
            {
                added = Scene.AddLayer(name, DocumentWidth, DocumentHeight);
                if (copyOf is not null) added.Bitmap.Pixels = copyOf.Bitmap.Pixels;
                paint?.Invoke(added);
                Scene.ActiveLayer = added;
            });
            return (added!, command);
        }

        /// <summary>A frame that is a group (several layers): a new group with one empty
        /// "Drawing" layer, or a copy of <paramref name="copyOf"/> with all its layers.
        /// The drawing layer (or the copy's same-named layer) becomes active.</summary>
        public (Layer Group, IEditCommand Undo) AddFrameGroup(string name, Layer? copyOf, string? activeName = null)
        {
            Layer? group = null;
            var command = CaptureStructuralChange(() =>
            {
                if (copyOf is not null && Scene.DuplicateLayer(copyOf) is { } copy)
                {
                    group = copy;
                    group.Name = name;
                    var (start, end) = Scene.BlockOf(group);
                    var members = Scene.Layers.Skip(start).Take(end - start).ToList();
                    foreach (var m in members) m.Name = m.Name.EndsWith(" copy", System.StringComparison.Ordinal) ? m.Name[..^5] : m.Name;
                    Scene.ActiveLayer = members.FirstOrDefault(m => m.Name == activeName) ?? members.LastOrDefault(m => !m.IsGroup) ?? group;
                }
                else
                {
                    var drawing = Scene.AddLayer("Drawing", DocumentWidth, DocumentHeight);
                    group = Scene.GroupLayer(drawing, name);
                    Scene.ActiveLayer = drawing;
                }
            });
            return (group!, command);
        }

        /// <summary>Turns frame layers into frame groups (each layer, renamed "Drawing",
        /// inside a group named like the frame). Returns the groups, in the same order.</summary>
        public (System.Collections.Generic.List<Layer> Groups, IEditCommand Undo) WrapFramesInGroups(System.Collections.Generic.IReadOnlyList<Layer> frames)
        {
            var groups = new System.Collections.Generic.List<Layer>();
            var command = CaptureStructuralChange(() =>
            {
                foreach (var layer in frames)
                {
                    var group = Scene.GroupLayer(layer, layer.Name);
                    layer.Name = "Drawing";
                    layer.IsVisible = true; // the group shows and hides now
                    group.IsVisible = layer.IsVisible;
                    groups.Add(group);
                }
            });
            return (groups, command);
        }

        /// <summary>Removes a frame's layer.</summary>
        public IEditCommand RemoveFrameLayer(Layer layer) => CaptureStructuralChange(() => Scene.RemoveLayer(layer));

        /// <summary>Makes <paramref name="layer"/> the active layer (the frame being drawn on).</summary>
        public void ActivateFrameLayer(Layer layer)
        {
            if (Items.FirstOrDefault(item => ReferenceEquals(item.Model, layer)) is { } row) ActiveLayer = row;
        }
    }
}
