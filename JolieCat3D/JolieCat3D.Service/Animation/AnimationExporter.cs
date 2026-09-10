using System.Numerics;
using System.Text.Json;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// Saves/loads a whole <see cref="AnimationTimeline"/> (every <see cref="AnimationTrack"/>'s
    /// transform keyframes, and every <see cref="TextureAnimationTrack"/>'s frame
    /// sequence) as a single JSON file - <see cref="AnimationExportData"/>'s own
    /// standard, human-readable, tool-agnostic shape ("JolieCat3D Animation Export",
    /// conventionally a ".j3danim.json" file, though nothing here enforces that
    /// extension). This is JolieCat3D's own native interchange format: unlike
    /// <c>Interop.JolieAnimationExporter</c> (which maps into JolieCat 2D's own
    /// <c>TimelineTrackData</c> shape for that specific tool), this format is designed
    /// only to round-trip a <see cref="AnimationTimeline"/> back into JolieCat3D itself,
    /// or any other 3D tool willing to read the same plain JSON shape - full transform
    /// curves (position/rotation/scale per keyframe, with interpolation mode), not just
    /// keyframe TIMES the way the JolieCat-compatible export is limited to.
    /// </summary>
    public static class AnimationExporter
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>Writes every track currently on <paramref name="timeline"/> to
        /// <paramref name="filePath"/> as <see cref="AnimationExportData"/> JSON - a
        /// track with zero keyframes (e.g. a node <see cref="AnimationTimeline.GetOrCreateTrack"/>
        /// created but nothing was ever recorded on) is still included, empty, so a
        /// later <see cref="ImportJson"/> doesn't need to guess whether that was
        /// deliberate or an omission.</summary>
        public static void ExportJson(AnimationTimeline timeline, string filePath)
        {
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var data = new AnimationExportData
            {
                FrameRate = timeline.FrameRate,
                TotalFrames = timeline.TotalFrames,
            };

            foreach (var track in timeline.Tracks)
            {
                var trackData = new AnimationTrackExportData { NodePath = GetNodePath(track.Target) };
                foreach (var keyframe in track.Keyframes)
                {
                    trackData.Keyframes.Add(new KeyframeExportData
                    {
                        Time = keyframe.Time,
                        PositionX = keyframe.Position.X,
                        PositionY = keyframe.Position.Y,
                        PositionZ = keyframe.Position.Z,
                        RotationX = keyframe.Rotation.X,
                        RotationY = keyframe.Rotation.Y,
                        RotationZ = keyframe.Rotation.Z,
                        RotationW = keyframe.Rotation.W,
                        ScaleX = keyframe.Scale.X,
                        ScaleY = keyframe.Scale.Y,
                        ScaleZ = keyframe.Scale.Z,
                        Interpolation = keyframe.Interpolation.ToString(),
                    });
                }

                data.Tracks.Add(trackData);
            }

            foreach (var textureTrack in timeline.TextureTracks)
            {
                var trackData = new TextureTrackExportData { MaterialName = textureTrack.Target.Name };
                foreach (var keyframe in textureTrack.Keyframes)
                {
                    trackData.Keyframes.Add(new TextureKeyframeExportData
                    {
                        Time = keyframe.Time,
                        TexturePath = keyframe.Frame.TexturePath,
                        OffsetX = keyframe.Frame.Offset.X,
                        OffsetY = keyframe.Frame.Offset.Y,
                        ScaleX = keyframe.Frame.Scale.X,
                        ScaleY = keyframe.Frame.Scale.Y,
                    });
                }

                data.TextureTracks.Add(trackData);
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(stream, data, WriteOptions);
        }

        /// <summary>Reads <paramref name="filePath"/> back and rebuilds every track it
        /// describes onto <paramref name="timeline"/>, resolving each
        /// <see cref="AnimationTrackExportData.NodePath"/>/<see cref="TextureTrackExportData.MaterialName"/>
        /// against <paramref name="scene"/> (see <see cref="FindNodeByPath"/>/
        /// <see cref="FindMaterialByName"/>). A path/name that no longer matches
        /// anything in <paramref name="scene"/> (the scene was re-shaped or re-named
        /// since export) is silently skipped rather than throwing - the rest of the file
        /// still imports, the same "best effort, don't let one bad entry ruin the whole
        /// load" spirit <c>MeshFileService</c>'s own importers already follow. Returns
        /// the number of tracks actually applied (transform + texture combined), so a
        /// caller can tell the user if some entries didn't resolve.</summary>
        /// <exception cref="InvalidDataException"><paramref name="filePath"/> isn't
        /// valid JSON, or deserializes to nothing at all.</exception>
        public static int ImportJson(string filePath, AnimationTimeline timeline, Scene3D scene)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentNullException.ThrowIfNull(scene);

            AnimationExportData? data;
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                data = JsonSerializer.Deserialize<AnimationExportData>(stream);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"'{filePath}' is not a valid animation export file.", ex);
            }

            if (data is null) throw new InvalidDataException($"'{filePath}' is not a valid animation export file (empty).");

            timeline.FrameRate = data.FrameRate;
            timeline.TotalFrames = data.TotalFrames;

            var appliedCount = 0;

            foreach (var trackData in data.Tracks)
            {
                if (FindNodeByPath(scene, trackData.NodePath) is not { } node) continue;

                var track = timeline.GetOrCreateTrack(node);
                foreach (var keyframeData in trackData.Keyframes)
                {
                    var interpolation = Enum.TryParse<InterpolationMode>(keyframeData.Interpolation, out var parsed)
                        ? parsed
                        : InterpolationMode.Linear;

                    track.AddKeyframe(
                        keyframeData.Time,
                        new Vector3(keyframeData.PositionX, keyframeData.PositionY, keyframeData.PositionZ),
                        new Quaternion(keyframeData.RotationX, keyframeData.RotationY, keyframeData.RotationZ, keyframeData.RotationW),
                        new Vector3(keyframeData.ScaleX, keyframeData.ScaleY, keyframeData.ScaleZ),
                        interpolation);
                }

                appliedCount++;
            }

            foreach (var trackData in data.TextureTracks)
            {
                if (FindMaterialByName(scene, trackData.MaterialName) is not { } material) continue;

                var track = timeline.GetOrCreateTextureTrack(material);
                foreach (var keyframeData in trackData.Keyframes)
                {
                    track.AddFrame(keyframeData.Time, new TextureFrame(
                        keyframeData.TexturePath,
                        new Vector2(keyframeData.OffsetX, keyframeData.OffsetY),
                        new Vector2(keyframeData.ScaleX, keyframeData.ScaleY)));
                }

                appliedCount++;
            }

            return appliedCount;
        }

        /// <summary>This node's own name, then its parent's, and so on up to its root,
        /// slash-joined in root-to-node order - see <see cref="AnimationTrackExportData.NodePath"/>'s
        /// own remarks.</summary>
        private static string GetNodePath(Node node)
        {
            var segments = new List<string>();
            for (var current = node; current is not null; current = current.Parent)
                segments.Add(current.Name);

            segments.Reverse();
            return string.Join('/', segments);
        }

        /// <summary>The inverse of <see cref="GetNodePath"/>: walks <paramref name="scene"/>'s
        /// own root nodes for a name matching the path's first segment, then descends
        /// through <see cref="Node.Children"/> matching each subsequent segment in turn -
        /// null the moment any segment fails to match, rather than partially resolving.
        /// Where a name repeats among siblings, the first match wins (see
        /// <see cref="AnimationTrackExportData.NodePath"/>'s own disclosed
        /// ambiguity).</summary>
        private static Node? FindNodeByPath(Scene3D scene, string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;

            var segments = nodePath.Split('/');
            var candidates = scene.RootNodes;
            Node? current = null;

            foreach (var segment in segments)
            {
                current = candidates.FirstOrDefault(n => n.Name == segment);
                if (current is null) return null;
                candidates = current.Children;
            }

            return current;
        }

        /// <summary>The first material, across every node's own mesh in
        /// <paramref name="scene"/>, whose <see cref="Material.Name"/> matches - null if
        /// none does. See <see cref="TextureTrackExportData.MaterialName"/>'s own
        /// disclosed "first match wins" ambiguity for materials that share a name.</summary>
        private static Material? FindMaterialByName(Scene3D scene, string materialName) =>
            scene.Traverse().Select(n => n.Mesh?.Material).FirstOrDefault(m => m is not null && m.Name == materialName);
    }
}
