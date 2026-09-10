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
        /// <paramref name="filePath"/> as <see cref="AnimationExportData"/> JSON (see
        /// <see cref="BuildExportData"/> for the actual conversion) - a track with zero
        /// keyframes (e.g. a node <see cref="AnimationTimeline.GetOrCreateTrack"/>
        /// created but nothing was ever recorded on) is still included, empty, so a
        /// later <see cref="ImportJson"/> doesn't need to guess whether that was
        /// deliberate or an omission.</summary>
        public static void ExportJson(AnimationTimeline timeline, string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var data = BuildExportData(timeline);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(stream, data, WriteOptions);
        }

        /// <summary>The pure in-memory half of <see cref="ExportJson"/> - converts
        /// <paramref name="timeline"/> into a plain <see cref="AnimationExportData"/>
        /// tree with no file I/O of its own, so a caller that already has its OWN JSON
        /// document to embed this into (<c>Project.Jolie3DProjectSerializer</c>, which
        /// nests this as one property of a whole project file, rather than writing a
        /// second, separate file just for the animation) can reuse the exact same
        /// node-path/material-name conversion <see cref="ExportJson"/> itself uses,
        /// instead of duplicating it.</summary>
        public static AnimationExportData BuildExportData(AnimationTimeline timeline)
        {
            ArgumentNullException.ThrowIfNull(timeline);

            var data = new AnimationExportData
            {
                FrameRate = timeline.FrameRate,
                TotalFrames = timeline.TotalFrames,
            };

            foreach (var track in timeline.Tracks)
            {
                var trackData = new AnimationTrackExportData { NodePath = NodePathResolver.GetPath(track.Target) };
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

            return data;
        }

        /// <summary>Reads <paramref name="filePath"/> back and rebuilds every track it
        /// describes onto <paramref name="timeline"/>, resolving each
        /// <see cref="AnimationTrackExportData.NodePath"/>/<see cref="TextureTrackExportData.MaterialName"/>
        /// against <paramref name="scene"/> (see <see cref="NodePathResolver.FindByPath"/>/
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

            return ApplyExportData(data, timeline, scene);
        }

        /// <summary>The pure in-memory half of <see cref="ImportJson"/> - applies an
        /// already-deserialized <see cref="AnimationExportData"/> onto
        /// <paramref name="timeline"/>, resolving it against <paramref name="scene"/>
        /// (see <see cref="ImportJson"/>'s own remarks on node-path/material-name
        /// resolution and its "best effort" skip-don't-throw behavior). Exists
        /// separately from <see cref="ImportJson"/> for the same reason
        /// <see cref="BuildExportData"/> exists separately from <see cref="ExportJson"/>:
        /// <c>Project.Jolie3DProjectSerializer</c> already has its own deserialized
        /// <see cref="AnimationExportData"/> (nested inside a whole project file's own
        /// JSON) with no separate animation file to read.</summary>
        public static int ApplyExportData(AnimationExportData data, AnimationTimeline timeline, Scene3D scene)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(timeline);
            ArgumentNullException.ThrowIfNull(scene);

            timeline.FrameRate = data.FrameRate;
            timeline.TotalFrames = data.TotalFrames;

            var appliedCount = 0;

            foreach (var trackData in data.Tracks)
            {
                if (NodePathResolver.FindByPath(scene, trackData.NodePath) is not { } node) continue;

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

        /// <summary>The first material, across every node's own mesh in
        /// <paramref name="scene"/>, whose <see cref="Material.Name"/> matches - null if
        /// none does. See <see cref="TextureTrackExportData.MaterialName"/>'s own
        /// disclosed "first match wins" ambiguity for materials that share a name.</summary>
        private static Material? FindMaterialByName(Scene3D scene, string materialName) =>
            scene.Traverse().Select(n => n.Mesh?.Material).FirstOrDefault(m => m is not null && m.Name == materialName);
    }
}
