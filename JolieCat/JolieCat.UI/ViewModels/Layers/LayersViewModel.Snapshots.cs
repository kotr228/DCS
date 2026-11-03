using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.History;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>A saved state of the whole document (History panel > New Snapshot).</summary>
    public sealed partial class HistorySnapshot : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        /// <summary>Whether the History Brush paints from this state.</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private bool isHistoryBrushSource;

        internal HistorySnapshot(string name, IReadOnlyList<LayerSnapshot> layers, int activeIndex, IReadOnlyList<Guide> guides)
        {
            Name = name;
            Layers = layers;
            ActiveIndex = activeIndex;
            Guides = guides;
            Created = DateTime.Now;
        }

        public string Name { get; }

        public DateTime Created { get; }

        public string Description => $"{Name}  ·  {Created:HH:mm}";

        internal IReadOnlyList<LayerSnapshot> Layers { get; }

        internal int ActiveIndex { get; }

        internal IReadOnlyList<Guide> Guides { get; }
    }

    /// <summary>
    /// History snapshots (Photoshop's): save the whole document's state, and later
    /// jump back to it - however many steps ago - as one undoable step. Snapshots live
    /// for the session (they aren't saved in the file).
    /// </summary>
    public partial class LayersViewModel
    {
        public ObservableCollection<HistorySnapshot> Snapshots { get; } = new();

        /// <summary>The state the History Brush paints from (the opening state unless
        /// another snapshot is picked).</summary>
        public HistorySnapshot? HistoryBrushSource => Snapshots.FirstOrDefault(s => s.IsHistoryBrushSource);

        /// <summary>Adds the document's opening state as the first snapshot (and the
        /// History Brush's source). Called once, when the document is opened or created.</summary>
        public void CaptureOpeningState()
        {
            if (Snapshots.Count > 0) return;
            Snapshots.Add(new HistorySnapshot("Opening state", Scene.CaptureLayers(), IndexOfLayer(Scene.ActiveLayer), Scene.Guides.ToList())
            {
                IsHistoryBrushSource = true,
            });
        }

        /// <summary>History panel: paint from <paramref name="snapshot"/> with the History Brush.</summary>
        [RelayCommand]
        private void SetHistoryBrushSource(HistorySnapshot? snapshot)
        {
            if (snapshot is null) return;
            foreach (var s in Snapshots) s.IsHistoryBrushSource = ReferenceEquals(s, snapshot);
        }

        [RelayCommand]
        private void NewSnapshot()
        {
            var n = Snapshots.Count(s => s.Name.StartsWith("Snapshot ", StringComparison.Ordinal)) + 1;
            Snapshots.Add(new HistorySnapshot($"Snapshot {n}", Scene.CaptureLayers(), IndexOfLayer(Scene.ActiveLayer), Scene.Guides.ToList()));
        }

        /// <summary>Puts the document back the way it was in <paramref name="snapshot"/>.</summary>
        [RelayCommand]
        private void RestoreSnapshot(HistorySnapshot? snapshot)
        {
            if (snapshot is null) return;
            ExecuteStructuralChange(() =>
            {
                Scene.RestoreLayers(snapshot.Layers, snapshot.ActiveIndex);
                Scene.RestoreGuides(snapshot.Guides);
                Scene.Selection.Clear();
            });
        }

        [RelayCommand]
        private void DeleteSnapshot(HistorySnapshot? snapshot)
        {
            if (snapshot is null) return;
            var wasSource = snapshot.IsHistoryBrushSource;
            Snapshots.Remove(snapshot);
            if (wasSource && Snapshots.Count > 0) Snapshots[0].IsHistoryBrushSource = true;
        }
    }
}
