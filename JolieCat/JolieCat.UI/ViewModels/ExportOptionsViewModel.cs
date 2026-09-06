using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;
using JolieCat.Core.Export;

namespace JolieCat.UI.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.ExportDialog"/>: what to export (the flattened composite,
    /// or a single layer) and how (format, and quality for the lossy formats). Purely a
    /// options-collection view model - the dialog itself only sets <c>DialogResult</c>;
    /// the actual export (prompting for a destination file, flattening, and writing)
    /// happens in <see cref="MainViewModel.ExportImageAsync"/>, matching how
    /// <see cref="MainViewModel.SaveProjectAsync"/>/<c>OpenProject</c> own their own
    /// file dialogs rather than delegating that to a separate view model.
    /// </summary>
    public partial class ExportOptionsViewModel : ObservableObject
    {
        /// <summary>One exportable thing: either the whole flattened composite
        /// (<see cref="Layer"/> null) or a single named layer.</summary>
        public sealed record ExportTarget(string Label, Layer? Layer)
        {
            public override string ToString() => Label;
        }

        public static IReadOnlyList<ImageExportFormat> AllFormats { get; } =
            Enum.GetValues<ImageExportFormat>();

        public ObservableCollection<ExportTarget> Targets { get; } = new();

        [ObservableProperty]
        private ExportTarget selectedTarget;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsQualityEnabled))]
        [NotifyPropertyChangedFor(nameof(FileExtension))]
        private ImageExportFormat format = ImageExportFormat.Png;

        /// <summary>1-100; only meaningful for the lossy formats (see
        /// <see cref="IsQualityEnabled"/>) - PNG is always lossless.</summary>
        [ObservableProperty]
        private int quality = 90;

        /// <summary>False for PNG (always lossless, nothing for a quality slider to
        /// control) - the dialog disables its quality slider based on this.</summary>
        public bool IsQualityEnabled => Format != ImageExportFormat.Png;

        /// <summary>The file extension (including the leading dot) matching
        /// <see cref="Format"/> - used to default the destination file dialog's name/
        /// filter to whatever format is actually selected.</summary>
        public string FileExtension => Format switch
        {
            ImageExportFormat.Png => ".png",
            ImageExportFormat.Jpeg => ".jpg",
            ImageExportFormat.WebP => ".webp",
            _ => ".png",
        };

        /// <summary>Builds the target list from <paramref name="scene"/>'s current
        /// layers - front-to-back (topmost first), matching the Layers panel's own
        /// order - with "Flattened Composite" always first and always selected by
        /// default, since exporting the whole finished image is the common case.</summary>
        public ExportOptionsViewModel(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            Targets.Add(new ExportTarget("Flattened Composite", null));
            foreach (var layer in scene.Layers.Reverse())
                Targets.Add(new ExportTarget(layer.Name, layer));

            selectedTarget = Targets[0];
        }
    }
}
