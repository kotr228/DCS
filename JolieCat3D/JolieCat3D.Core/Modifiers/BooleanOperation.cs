namespace JolieCat3D.Core.Modifiers
{
    /// <summary>Which Constructive Solid Geometry combination <see cref="BooleanModifier"/>
    /// performs between its own mesh and <see cref="BooleanModifier.Target"/>'s.</summary>
    public enum BooleanOperation
    {
        /// <summary>Everything enclosed by EITHER solid - the merged combination of both,
        /// with the overlapping interior removed.</summary>
        Union,

        /// <summary>Everything enclosed by this modifier's own mesh that is NOT also
        /// enclosed by <see cref="BooleanModifier.Target"/>'s - the standard modeling-tool
        /// "cut a hole/notch using another object's shape" operation.</summary>
        Difference,

        /// <summary>Only what's enclosed by BOTH solids at once - the overlapping volume
        /// alone, everything else discarded.</summary>
        Intersection,
    }
}
