namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// The plain-data JSON shape <see cref="AnimationExporter"/> reads/writes - JolieCat3D's
    /// own native animation interchange format (not tied to any particular file
    /// extension; <see cref="AnimationExporter"/>'s own remarks call it ".j3danim.json"
    /// by convention). Every field here is plain data (strings/numbers/lists), the same
    /// "readable, no hard-coded enum ints, no engine types" shape
    /// <c>JolieCat3D.Service.Interop.JolieProjectManifestInfo</c> already uses for the
    /// same reason: a hand-inspectable file another tool (or a future version of this
    /// one) can read without sharing a single line of code with this one.
    /// </summary>
    public sealed class AnimationExportData
    {
        /// <summary>Bumped only if a future change to this shape would break an older
        /// reader - <see cref="AnimationExporter.ImportJson"/> is the one place that
        /// would ever need to branch on it, and doesn't yet, since this is the format's
        /// first version.</summary>
        public int FormatVersion { get; set; } = 1;

        public double FrameRate { get; set; }

        public int TotalFrames { get; set; }

        public List<AnimationTrackExportData> Tracks { get; set; } = new();

        public List<TextureTrackExportData> TextureTracks { get; set; } = new();
    }

    /// <summary>One <see cref="AnimationTrack"/>'s worth of keyframes, plus
    /// <see cref="NodePath"/> - the exported stand-in for the <c>Core.Scene.Node</c>
    /// reference itself (a plain data file obviously can't hold a live object
    /// reference), resolved back to a real node on import the same way it was built on
    /// export (see <see cref="AnimationExporter"/>'s own remarks on both directions).</summary>
    public sealed class AnimationTrackExportData
    {
        /// <summary>This node's own name, then its parent's, its parent's parent's, and
        /// so on up to (and including) whichever root node the chain started from,
        /// slash-joined in root-to-node order (e.g. "Rig/Arm/Hand") - enough to find the
        /// same node again in a scene with the same hierarchy shape. Two sibling nodes
        /// sharing a name resolve to whichever one <see cref="Core.Scene.Node.Children"/>
        /// lists first - a known, disclosed ambiguity (the same one every other
        /// name-based lookup in this codebase already accepts - see e.g.
        /// <c>Interop.JolieProjectReader.ExtractLayerTextureByName</c>) rather than
        /// inventing a stable ID system no other part of the scene graph has.</summary>
        public string NodePath { get; set; } = string.Empty;

        public List<KeyframeExportData> Keyframes { get; set; } = new();
    }

    /// <summary>One <see cref="Keyframe"/>, fully flattened to plain numbers - a
    /// <see cref="System.Numerics.Vector3"/>/<see cref="System.Numerics.Quaternion"/>
    /// serializes just fine on its own, but spelling out X/Y/Z/W as their own properties
    /// keeps the JSON readable by a human or a completely unrelated tool without it
    /// needing to know anything about <c>System.Numerics</c>' own field layout.</summary>
    public sealed class KeyframeExportData
    {
        public double Time { get; set; }

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }

        public float ScaleX { get; set; }
        public float ScaleY { get; set; }
        public float ScaleZ { get; set; }

        /// <summary><see cref="InterpolationMode"/>'s own name ("Linear"/"Bezier") - kept
        /// as a string for the same reason <c>JolieCat.Core.Serialization.LayerManifestEntry.Type</c>
        /// is: the file stays readable and doesn't hard-code the enum's underlying
        /// integer values.</summary>
        public string Interpolation { get; set; } = nameof(Animation.InterpolationMode.Linear);
    }

    /// <summary>One <see cref="TextureAnimationTrack"/>'s worth of frames, plus
    /// <see cref="MaterialName"/> - the exported stand-in for the
    /// <c>Core.Materials.Material</c> reference itself, resolved back by name (first
    /// match across every node's mesh material in the target scene) on import, the same
    /// disclosed "first match wins" convention <see cref="AnimationTrackExportData.NodePath"/>
    /// uses for nodes.</summary>
    public sealed class TextureTrackExportData
    {
        public string MaterialName { get; set; } = string.Empty;

        public List<TextureKeyframeExportData> Keyframes { get; set; } = new();
    }

    /// <summary>One <see cref="TextureKeyframe"/>, flattened the same way
    /// <see cref="KeyframeExportData"/> flattens a transform <see cref="Keyframe"/>.</summary>
    public sealed class TextureKeyframeExportData
    {
        public double Time { get; set; }

        public string TexturePath { get; set; } = string.Empty;

        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float ScaleX { get; set; }
        public float ScaleY { get; set; }
    }
}
