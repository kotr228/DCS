using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    /// <summary>Which width:height ratio the Crop tool's rectangle is constrained to
    /// while dragging its handles. <see cref="Original"/> locks to the document's own
    /// current aspect ratio (computed by <c>CanvasViewModel</c>, which knows the
    /// document size this view model doesn't) rather than a fixed number.</summary>
    public enum CropAspectRatioMode
    {
        Free,
        Original,
        Square,
        Widescreen,
        FourByThree,
    }

    /// <summary>Options shown for the Crop tool: aspect ratio lock and a straighten/
    /// rotation angle (applied to the whole document around its center at commit
    /// time - see <c>Documents.Scene.CropLayers</c>).</summary>
    public partial class CropToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<CropAspectRatioMode> AllAspectRatios { get; } = Enum.GetValues<CropAspectRatioMode>();

        [ObservableProperty]
        private CropAspectRatioMode aspectRatio = CropAspectRatioMode.Free;

        /// <summary>Degrees to straighten the image by at commit - positive rotates
        /// the crop rectangle's contents clockwise back to level, matching a tilted-
        /// horizon photo's own correction convention. 0 means no rotation.</summary>
        [ObservableProperty]
        private double rotationDegrees;

        /// <summary>The fixed width:height ratio for every mode except
        /// <see cref="CropAspectRatioMode.Free"/> (unconstrained) and
        /// <see cref="CropAspectRatioMode.Original"/> (the document's own ratio,
        /// which only the caller knows) - both of which return null.</summary>
        public double? GetFixedRatio() => AspectRatio switch
        {
            CropAspectRatioMode.Square => 1.0,
            CropAspectRatioMode.Widescreen => 16.0 / 9.0,
            CropAspectRatioMode.FourByThree => 4.0 / 3.0,
            _ => null,
        };
    }
}
