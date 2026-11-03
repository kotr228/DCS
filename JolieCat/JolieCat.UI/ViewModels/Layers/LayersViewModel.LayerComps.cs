using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;
using JolieCat.Shared.Enums;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>A saved arrangement of the layers (Layer Comps): which are visible, and
    /// their opacity, blend mode and layer style. Pixels aren't stored, so comps are
    /// cheap - handy for showing a client several versions of one design.</summary>
    public sealed class LayerComp
    {
        internal sealed record State(bool IsVisible, double Opacity, BlendMode BlendMode, LayerStyle? Style);

        internal LayerComp(string name, IReadOnlyDictionary<Guid, State> states)
        {
            Name = name;
            States = states;
        }

        public string Name { get; }

        internal IReadOnlyDictionary<Guid, State> States { get; }
    }

    public partial class LayersViewModel
    {
        public ObservableCollection<LayerComp> LayerComps { get; } = new();

        [RelayCommand]
        private void NewLayerComp()
        {
            var states = Scene.Layers.ToDictionary(l => l.Id, l => new LayerComp.State(l.IsVisible, l.Opacity, l.BlendMode, l.Style));
            LayerComps.Add(new LayerComp($"Layer Comp {LayerComps.Count + 1}", states));
        }

        /// <summary>Puts the layers back the way <paramref name="comp"/> saved them (one
        /// undo step). Layers added since keep their current look.</summary>
        [RelayCommand]
        private void ApplyLayerComp(LayerComp? comp)
        {
            if (comp is null) return;
            ExecuteStructuralChange(() =>
            {
                foreach (var layer in Scene.Layers)
                {
                    if (!comp.States.TryGetValue(layer.Id, out var state)) continue;
                    layer.IsVisible = state.IsVisible;
                    layer.Opacity = state.Opacity;
                    layer.BlendMode = state.BlendMode;
                    layer.Style = state.Style;
                }
            });
        }

        [RelayCommand]
        private void DeleteLayerComp(LayerComp? comp)
        {
            if (comp is not null) LayerComps.Remove(comp);
        }
    }
}
