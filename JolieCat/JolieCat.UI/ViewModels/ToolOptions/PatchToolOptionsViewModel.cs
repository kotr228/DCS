using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.ToolOptions
{
    public enum PatchMode
    {
        /// <summary>The selected area is rebuilt from where the selection is dragged to.</summary>
        Source,

        /// <summary>The selected content is copied to where the selection is dragged to.</summary>
        Destination,
    }

    public enum ContentAwareMoveMode
    {
        /// <summary>The content moves; the place it left is filled from its surroundings.</summary>
        Move,

        /// <summary>The content is copied; the original stays (Photoshop's "Extend").</summary>
        Extend,
    }

    /// <summary>Patch tool settings.</summary>
    public partial class PatchToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<PatchMode> AllModes { get; } = Enum.GetValues<PatchMode>();

        [ObservableProperty]
        private PatchMode mode = PatchMode.Source;
    }

    /// <summary>Content-Aware Move tool settings.</summary>
    public partial class ContentAwareMoveToolOptionsViewModel : ObservableObject
    {
        public static IReadOnlyList<ContentAwareMoveMode> AllModes { get; } = Enum.GetValues<ContentAwareMoveMode>();

        [ObservableProperty]
        private ContentAwareMoveMode mode = ContentAwareMoveMode.Move;
    }
}
