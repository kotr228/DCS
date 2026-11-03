using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Effects;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One row of the Smart Filters window.</summary>
    public partial class SmartFilterItemViewModel : ObservableObject
    {
        private readonly Action<SmartFilterItemViewModel, bool> _toggled;
        private bool _loading = true;

        public SmartFilterItemViewModel(int index, SmartFilter filter, Action<SmartFilterItemViewModel, bool> toggled)
        {
            Index = index;
            Title = filter.Title;
            _toggled = toggled;
            enabled = filter.Enabled;
            _loading = false;
        }

        public int Index { get; }

        public string Title { get; }

        [ObservableProperty]
        private bool enabled;

        partial void OnEnabledChanged(bool value)
        {
            if (!_loading) _toggled(this, value);
        }
    }

    /// <summary>
    /// Layer > Smart Filters: the active layer's filters, top = applied first. Show/hide,
    /// reorder, delete, edit a filter's settings (reopens its dialog), or rasterize the
    /// layer. Every change applies at once and is one undo step.
    /// </summary>
    public partial class SmartFiltersViewModel : ObservableObject
    {
        private readonly Core.Documents.Layer _layer;
        private readonly CanvasViewModel _canvas;
        private readonly Func<Core.Documents.Layer, int, bool> _edit;

        /// <param name="edit">Reopens filter <c>index</c>'s dialog; true if it changed.</param>
        public SmartFiltersViewModel(Core.Documents.Layer layer, CanvasViewModel canvas, Func<Core.Documents.Layer, int, bool> edit)
        {
            _layer = layer;
            _canvas = canvas;
            _edit = edit;
            Reload();
        }

        public ObservableCollection<SmartFilterItemViewModel> Filters { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
        private SmartFilterItemViewModel? selected;

        public string LayerName => _layer.Name;

        public bool IsEmpty => Filters.Count == 0;

        /// <summary>Raised when the layer was rasterized (the window then closes).</summary>
        public event Action? Rasterized;

        private void Reload(int? select = null)
        {
            Filters.Clear();
            var list = _layer.SmartFilters;
            for (var i = 0; i < list.Count; i++)
                Filters.Add(new SmartFilterItemViewModel(i, list[i], OnToggled));
            Selected = select is int s && s >= 0 && s < Filters.Count ? Filters[s] : Filters.FirstOrDefault();
            OnPropertyChanged(nameof(IsEmpty));
        }

        private void OnToggled(SmartFilterItemViewModel item, bool enabled)
        {
            var list = _layer.SmartFilters.ToList();
            list[item.Index] = list[item.Index] with { Enabled = enabled };
            _canvas.ChangeSmartFilters(_layer, list, enabled ? $"Show {item.Title}" : $"Hide {item.Title}");
        }

        private bool HasSelection() => Selected is not null;

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Edit()
        {
            if (Selected is not { } item) return;
            if (_edit(_layer, item.Index)) Reload(item.Index);
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Delete()
        {
            if (Selected is not { } item) return;
            var list = _layer.SmartFilters.ToList();
            list.RemoveAt(item.Index);
            _canvas.ChangeSmartFilters(_layer, list, $"Delete {item.Title}");
            Reload(Math.Min(item.Index, list.Count - 1));
        }

        private bool CanMoveUp() => Selected is { Index: > 0 };

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUp() => Move(-1);

        private bool CanMoveDown() => Selected is { } s && s.Index < Filters.Count - 1;

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown() => Move(+1);

        private void Move(int by)
        {
            if (Selected is not { } item) return;
            var list = _layer.SmartFilters.ToList();
            var target = item.Index + by;
            (list[item.Index], list[target]) = (list[target], list[item.Index]);
            _canvas.ChangeSmartFilters(_layer, list, "Reorder Smart Filters");
            Reload(target);
        }

        [RelayCommand]
        private void Rasterize()
        {
            _canvas.Layers.RasterizeSmartFiltersCommand.Execute(null);
            Rasterized?.Invoke();
        }
    }
}
