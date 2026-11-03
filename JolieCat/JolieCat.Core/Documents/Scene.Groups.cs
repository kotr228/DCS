using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Rendering;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>Where a dragged layer lands relative to the layer it's dropped on.</summary>
    public enum LayerDropPosition
    {
        Above,
        Below,
        Into,
    }

    /// <summary>
    /// Layer groups. The layer list stays flat (back to front); a group is a
    /// <see cref="LayerType.Group"/> layer with its members - layers whose
    /// <see cref="Layer.ParentId"/> points at it, and their members in turn - forming
    /// one unbroken block directly below it. Every operation here keeps that shape,
    /// and <see cref="NormalizeGroups"/> repairs anything that breaks it.
    /// </summary>
    public sealed partial class Scene
    {
        public Layer? FindById(Guid id)
        {
            foreach (var layer in _layers)
                if (layer.Id == id) return layer;
            return null;
        }

        public int IndexOf(Layer layer) => _layers.IndexOf(layer);

        /// <summary>The group <paramref name="layer"/> is directly in, or null.</summary>
        public Layer? ParentOf(Layer layer) =>
            layer.ParentId is { } id && FindById(id) is { IsGroup: true } parent ? parent : null;

        /// <summary>0 at the top level, 1 inside one group, and so on.</summary>
        public int Depth(Layer layer)
        {
            var depth = 0;
            for (var parent = ParentOf(layer); parent is not null && depth < 64; parent = ParentOf(parent))
                depth++;
            return depth;
        }

        public bool IsDescendantOf(Layer layer, Layer group)
        {
            var guard = 0;
            for (var parent = ParentOf(layer); parent is not null && guard++ < 64; parent = ParentOf(parent))
                if (ReferenceEquals(parent, group)) return true;
            return false;
        }

        /// <summary>Visible itself and inside no hidden group.</summary>
        public bool IsEffectivelyVisible(Layer layer)
        {
            if (!layer.IsVisible) return false;
            var guard = 0;
            for (var parent = ParentOf(layer); parent is not null && guard++ < 64; parent = ParentOf(parent))
                if (!parent.IsVisible) return false;
            return true;
        }

        /// <summary>Walking up from <paramref name="layer"/>, the layer (itself or an
        /// enclosing group) that sits directly in <paramref name="parentId"/>, or null
        /// if <paramref name="layer"/> isn't inside it.</summary>
        public Layer? MemberOf(Layer layer, Guid? parentId)
        {
            var current = layer;
            for (var guard = 0; guard < 64; guard++)
            {
                var parent = ParentOf(current);
                if (parent?.Id == parentId) return current;
                if (parent is null) return null;
                current = parent;
            }
            return null;
        }

        /// <summary>Index range [Start, End] taken by <paramref name="layer"/> and, for a
        /// group, all of its members (End is the layer's own index).</summary>
        public (int Start, int End) BlockOf(Layer layer)
        {
            var end = _layers.IndexOf(layer);
            if (end < 0 || !layer.IsGroup) return (end, end);

            var start = end;
            while (start > 0 && IsDescendantOf(_layers[start - 1], layer)) start--;
            return (start, end);
        }

        /// <summary>The members directly inside <paramref name="group"/> (front to back
        /// order not guaranteed - list order, back to front).</summary>
        public IEnumerable<Layer> ChildrenOf(Layer group) => _layers.Where(l => l.ParentId == group.Id && ReferenceEquals(ParentOf(l), group));

        // ================= Operations =================

        /// <summary>A new, empty group directly above <paramref name="above"/>'s block
        /// (in the same group as it), or at the top if null. Becomes active.</summary>
        public Layer NewGroup(Layer? above, int width, int height, string name)
        {
            var group = new Layer(name, width, height, LayerType.Group);
            if (above is not null && _layers.Contains(above))
            {
                group.ParentId = above.ParentId;
                _layers.Insert(BlockOf(above).End + 1, group);
            }
            else
            {
                _layers.Add(group);
            }

            ActiveLayer = group;
            return group;
        }

        /// <summary>Layer &gt; Group Layers: wraps <paramref name="layer"/> (or a whole
        /// group) in a new group. The new group becomes active.</summary>
        public Layer GroupLayer(Layer layer, string name)
        {
            var group = new Layer(name, layer.Bitmap.Width, layer.Bitmap.Height, LayerType.Group)
            {
                ParentId = layer.ParentId,
            };
            _layers.Insert(BlockOf(layer).End + 1, group);
            layer.ParentId = group.Id;
            ActiveLayer = group;
            return group;
        }

        /// <summary>Layer &gt; Ungroup: the group's members move up one level, the group
        /// itself goes away. Its first member becomes active.</summary>
        public bool Ungroup(Layer group)
        {
            if (!group.IsGroup || !_layers.Contains(group)) return false;

            var children = ChildrenOf(group).ToList();
            foreach (var child in children) child.ParentId = group.ParentId;

            var index = _layers.IndexOf(group);
            _layers.RemoveAt(index);
            ActiveLayer = children.Count > 0 ? children[^1] : (_layers.Count > 0 ? _layers[Math.Min(index, _layers.Count - 1)] : null);
            group.Dispose();
            return true;
        }

        /// <summary>Takes the block [start, end] out of the list.</summary>
        private List<Layer> CutBlock(int start, int end)
        {
            var block = _layers.GetRange(start, end - start + 1);
            _layers.RemoveRange(start, end - start + 1);
            return block;
        }

        /// <summary>Moves <paramref name="layer"/> (with its members, for a group) one
        /// step toward the front. At the top of its group it leaves the group; reaching a
        /// group above it, it enters that group at the bottom.</summary>
        private bool MoveUnitUp(Layer layer)
        {
            var (start, end) = BlockOf(layer);
            if (start < 0 || end >= _layers.Count - 1) return false;

            var next = _layers[end + 1];
            var parent = ParentOf(layer);

            if (parent is not null && ReferenceEquals(next, parent))
            {
                // Leave the group: go just above it.
                var block = CutBlock(start, end);
                layer.ParentId = parent.ParentId;
                _layers.InsertRange(_layers.IndexOf(parent) + 1, block);
                return true;
            }

            var sibling = MemberOf(next, layer.ParentId);
            if (sibling is null) return false;

            if (sibling.IsGroup)
            {
                // Enter the group above, as its bottom member - the order doesn't change.
                layer.ParentId = sibling.Id;
                return true;
            }

            var siblingBlock = BlockOf(sibling);
            var moved = CutBlock(start, end);
            _layers.InsertRange(siblingBlock.End - moved.Count + 1, moved);
            return true;
        }

        /// <summary>Mirror of <see cref="MoveUnitUp"/> toward the back.</summary>
        private bool MoveUnitDown(Layer layer)
        {
            var (start, end) = BlockOf(layer);
            if (start <= 0) return false;

            var previous = _layers[start - 1];
            var parent = ParentOf(layer);

            if (parent is not null && !IsDescendantOf(previous, parent))
            {
                // Bottom of the group: leave it, going just below the whole group.
                var block = CutBlock(start, end);
                layer.ParentId = parent.ParentId;
                _layers.InsertRange(BlockOf(parent).Start, block);
                return true;
            }

            var sibling = MemberOf(previous, layer.ParentId);
            if (sibling is null) return false;

            var siblingBlock = BlockOf(sibling);
            var moved = CutBlock(start, end);
            if (sibling.IsGroup)
            {
                // Enter the group below as its top member: just under its header.
                layer.ParentId = sibling.Id;
                _layers.InsertRange(_layers.IndexOf(sibling), moved);
                return true;
            }

            _layers.InsertRange(siblingBlock.Start, moved);
            return true;
        }

        /// <summary>Copies a whole group block with fresh Ids, directly above the original.</summary>
        private Layer DuplicateGroup(Layer group)
        {
            var (start, end) = BlockOf(group);
            var idMap = new Dictionary<Guid, Guid>();
            var copies = new List<Layer>();

            for (var i = start; i <= end; i++)
            {
                var source = _layers[i];
                var copy = CopyLayer(source, ReferenceEquals(source, group) ? $"{source.Name} copy" : source.Name);
                idMap[source.Id] = copy.Id;
                copies.Add(copy);
            }

            for (var k = 0; k < copies.Count; k++)
            {
                var sourceParent = _layers[start + k].ParentId;
                copies[k].ParentId = sourceParent is { } p && idMap.TryGetValue(p, out var mapped) ? mapped : sourceParent;
            }

            _layers.InsertRange(end + 1, copies);
            ActiveLayer = copies[^1];
            return copies[^1];
        }

        /// <summary>A standalone copy of one layer's pixels, mask and settings (new Id).</summary>
        private static Layer CopyLayer(Layer layer, string name)
        {
            var type = layer.IsGroup ? LayerType.Group : layer.IsAdjustment ? LayerType.Adjustment : LayerType.Raster;
            var copy = new Layer(name, layer.Bitmap.Width, layer.Bitmap.Height, type)
            {
                IsVisible = layer.IsVisible,
                Opacity = layer.Opacity,
                BlendMode = layer.BlendMode,
                Adjustment = layer.Adjustment,
                Style = layer.Style,
                ParentId = layer.ParentId,
                IsExpanded = layer.IsExpanded,
                IsClipped = layer.IsClipped,
                Text = layer.Text,
                Shape = layer.Shape,
                Fill = layer.Fill,
            };
            copy.Canvas.DrawBitmap(layer.Bitmap, 0, 0);
            if (layer.SmartFilterSource is { } smartSource)
                copy.SetSmartFilters(smartSource.Copy(), layer.SmartFilters);

            if (layer.Mask is { } mask)
            {
                var copyMask = copy.AddMask();
                copyMask.IsEnabled = mask.IsEnabled;
                copyMask.Canvas.Clear(SKColors.Transparent);
                copyMask.Canvas.DrawBitmap(mask.Bitmap, 0, 0);
                copy.IsMaskActive = layer.IsMaskActive;
            }

            return copy;
        }

        /// <summary>Merge Group: the group's members, composited, become one pixel layer
        /// that takes the group's place (and keeps its opacity and blend mode).</summary>
        public Layer? MergeGroup(Layer group)
        {
            if (!group.IsGroup) return null;
            var (start, end) = BlockOf(group);

            var merged = new Layer(group.Name, group.Bitmap.Width, group.Bitmap.Height, LayerType.Raster, group.Id)
            {
                IsVisible = group.IsVisible,
                Opacity = group.Opacity,
                BlendMode = group.BlendMode,
                ParentId = group.ParentId,
                Style = group.Style, // the group's effects stay live on the merged layer
            };
            using (var canvas = new SKCanvas(merged.Bitmap))
                SceneCompositor.DrawGroupMembers(canvas, this, group, merged.Bitmap.Width, merged.Bitmap.Height, () => SKImage.FromBitmap(merged.Bitmap));

            // The group's mask carries over to the merged layer.
            if (group.Mask is { } groupMask)
            {
                var mask = merged.AddMask();
                mask.IsEnabled = groupMask.IsEnabled;
                mask.Canvas.Clear(SKColors.White);
                mask.Canvas.DrawBitmap(groupMask.Bitmap, 0, 0);
            }

            var removed = CutBlock(start, end);
            _layers.Insert(start, merged);
            foreach (var layer in removed) layer.Dispose();
            ActiveLayer = merged;
            return merged;
        }

        // ================= Clipping masks =================

        /// <summary>The clipping group around index <paramref name="index"/>: the base
        /// layer and every clipped layer stacked on it (same group). Just the index
        /// itself when the layer isn't part of one.</summary>
        public (int Start, int End) ClipRange(int index)
        {
            if (index < 0 || index >= _layers.Count || _layers[index].IsGroup) return (index, index);
            var parent = _layers[index].ParentId;

            var start = index;
            while (_layers[start].IsClipped && start > 0 && _layers[start - 1] is { IsGroup: false } below && below.ParentId == parent)
                start--;

            var end = index;
            while (end + 1 < _layers.Count && _layers[end + 1] is { IsClipped: true, IsGroup: false } above && above.ParentId == parent)
                end++;

            return (start, end);
        }

        /// <summary>The layer a clipped layer is clipped to, or null.</summary>
        public Layer? ClippingBaseOf(Layer layer)
        {
            if (!layer.IsClipped) return null;
            var (start, _) = ClipRange(_layers.IndexOf(layer));
            var baseLayer = start >= 0 ? _layers[start] : null;
            return baseLayer is null || ReferenceEquals(baseLayer, layer) || baseLayer.IsClipped || baseLayer.IsAdjustment ? null : baseLayer;
        }

        // ================= Several layers at once =================

        /// <summary>The given layers reduced to independent units: a layer whose group
        /// is also in the set is dropped (it moves with the group). Back to front.</summary>
        public List<Layer> TopUnits(IEnumerable<Layer> layers)
        {
            var set = layers.Where(l => _layers.Contains(l)).Distinct().ToList();
            return set
                .Where(l => !set.Any(other => !ReferenceEquals(other, l) && other.IsGroup && IsDescendantOf(l, other)))
                .OrderBy(l => _layers.IndexOf(l))
                .ToList();
        }

        /// <summary>Moves <paramref name="layers"/> (each with its members) next to
        /// <paramref name="target"/>: just above or below its block, or into it at the
        /// top when it's a group. Their order among themselves is kept. Returns false
        /// (changing nothing) when the target is one of them or inside one of them.</summary>
        public bool MoveLayers(IEnumerable<Layer> layers, Layer target, LayerDropPosition position)
        {
            var units = TopUnits(layers);
            if (units.Count == 0 || !_layers.Contains(target)) return false;
            if (position == LayerDropPosition.Into && !target.IsGroup) return false;
            foreach (var unit in units)
                if (ReferenceEquals(unit, target) || (unit.IsGroup && IsDescendantOf(target, unit))) return false;

            // Cut from the top down so earlier indices stay valid; keep back-to-front order.
            var blocks = new List<List<Layer>>();
            foreach (var unit in Enumerable.Reverse(units))
            {
                var (start, end) = BlockOf(unit);
                blocks.Insert(0, CutBlock(start, end));
            }
            var moving = blocks.SelectMany(b => b).ToList();

            int insertAt;
            Guid? parent;
            switch (position)
            {
                case LayerDropPosition.Above:
                    insertAt = BlockOf(target).End + 1;
                    parent = target.ParentId;
                    break;
                case LayerDropPosition.Below:
                    insertAt = BlockOf(target).Start;
                    parent = target.ParentId;
                    break;
                default:
                    insertAt = _layers.IndexOf(target);
                    parent = target.Id;
                    break;
            }

            _layers.InsertRange(insertAt, moving);
            foreach (var unit in units) unit.ParentId = parent;
            return true;
        }

        /// <summary>Ctrl+G with several layers selected: one new group, where the
        /// frontmost of them was, holding all of them.</summary>
        public Layer GroupLayers(IEnumerable<Layer> layers, string name)
        {
            var units = TopUnits(layers);
            if (units.Count == 0) throw new ArgumentException("Nothing to group.", nameof(layers));
            if (units.Count == 1) return GroupLayer(units[0], name);

            var front = units[^1];
            var group = new Layer(name, front.Bitmap.Width, front.Bitmap.Height, LayerType.Group) { ParentId = front.ParentId };
            _layers.Insert(BlockOf(front).End + 1, group);
            MoveLayers(units, group, LayerDropPosition.Into);
            ActiveLayer = group;
            return group;
        }

        /// <summary>Ctrl+E with several layers selected: they become one layer (where
        /// the frontmost was) that looks as they did together.</summary>
        public Layer? MergeLayers(IEnumerable<Layer> layers)
        {
            var units = TopUnits(layers);
            if (units.Count < 2) return null;

            var front = units[^1];
            var width = front.Bitmap.Width;
            var height = front.Bitmap.Height;
            var merged = new Layer(front.Name, width, height, LayerType.Raster) { ParentId = front.ParentId };

            using (var canvas = new SKCanvas(merged.Bitmap))
            {
                Func<SKImage> backdrop = () => SKImage.FromBitmap(merged.Bitmap);
                foreach (var unit in units)
                {
                    if (!unit.IsVisible) continue;
                    if (unit.IsGroup)
                    {
                        using var paint = new SKPaint
                        {
                            Color = SKColors.White.WithAlpha((byte)Math.Clamp(unit.Opacity * 255.0, 0, 255)),
                            BlendMode = Layer.ToSkiaBlendMode(unit.BlendMode),
                        };
                        canvas.SaveLayer(new SKRect(0, 0, width, height), paint);
                        SceneCompositor.DrawGroupMembers(canvas, this, unit, width, height, backdrop);
                        if (unit.Mask is { IsEnabled: true } groupMask)
                        {
                            using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() };
                            canvas.DrawBitmap(groupMask.Bitmap, 0, 0, maskPaint);
                        }
                        canvas.Restore();
                    }
                    else
                    {
                        SceneCompositor.DrawLayer(canvas, unit, unit.Bitmap, width, height, default, backdrop);
                    }
                }
            }

            _layers.Insert(BlockOf(front).End + 1, merged);
            foreach (var unit in Enumerable.Reverse(units))
            {
                var (start, end) = BlockOf(unit);
                foreach (var gone in CutBlock(start, end)) gone.Dispose();
            }

            ActiveLayer = merged;
            return merged;
        }

        /// <summary>Deletes several layers (groups with their members).</summary>
        public void RemoveLayers(IEnumerable<Layer> layers)
        {
            foreach (var unit in Enumerable.Reverse(TopUnits(layers)))
                RemoveLayer(unit);
        }

        /// <summary>Repairs the group structure: drops links to missing or non-group
        /// parents and to groups the layer isn't next to, so a member always lies
        /// inside its group's block.</summary>
        public void NormalizeGroups()
        {
            for (var pass = 0; pass < 4; pass++)
            {
                var changed = false;
                for (var i = 0; i < _layers.Count; i++)
                {
                    var layer = _layers[i];
                    if (layer.ParentId is not { } parentId) continue;

                    var parent = FindById(parentId);
                    var parentIndex = parent is null ? -1 : _layers.IndexOf(parent);
                    var ok = parent is { IsGroup: true } && parentIndex > i;
                    if (ok)
                    {
                        // Everything between the layer and its group must be inside the group.
                        for (var j = i + 1; j < parentIndex && ok; j++)
                            if (!IsDescendantOf(_layers[j], parent!)) ok = false;
                    }

                    if (!ok)
                    {
                        layer.ParentId = null;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
        }
    }
}
