namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// Makes a <see cref="Node"/> the ROOT of a skeleton - present (non-null, on
    /// <see cref="Node.Armature"/>) only for a node authored as one, the same "optional
    /// data, not a subclass" shape <see cref="Node.Camera"/>/<see cref="Node.Light"/>/
    /// <see cref="Node.Curve"/> already use. Deliberately a near-empty marker: the
    /// actual skeleton is just an ordinary <see cref="Node"/> subtree hanging off this
    /// node (each bone a <see cref="Node"/> with its own <see cref="BoneData"/>,
    /// parented via the SAME <see cref="Node.AddChild"/>/<see cref="Node.Parent"/>
    /// mechanism every other node hierarchy already uses) - Forward Kinematics falls
    /// straight out of <see cref="Node.GetWorldTransform"/>'s own existing parent-chain
    /// composition, with no separate "skeleton" data structure needed at all. This
    /// class exists only so "Add Armature" has something distinct from a plain empty
    /// pivot node to create, and so the Properties Inspector/Scene Outliner can
    /// recognize an armature root as such.
    /// </summary>
    public sealed class ArmatureData
    {
        /// <summary>A complete, independent copy - trivial today (no fields), but kept
        /// for the same reason every other optional-data type here has one: so
        /// <see cref="Node.Clone"/> never needs a special case for "this optional data
        /// type happens to have nothing to copy yet".</summary>
        public ArmatureData Clone() => new();
    }
}
