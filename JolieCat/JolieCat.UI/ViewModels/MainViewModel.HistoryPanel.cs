using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One row of the History panel.</summary>
    public sealed partial class HistoryStepViewModel : ObservableObject
    {
        public HistoryStepViewModel(int undoCount, string name, bool isUndone, bool isCurrent)
        {
            UndoCount = undoCount;
            Name = name;
            IsUndone = isUndone;
            IsCurrent = isCurrent;
        }

        /// <summary>How many steps are applied when this row is the current state.</summary>
        public int UndoCount { get; }

        public string Name { get; }

        /// <summary>A step after the current state (shown dimmed; redo brings it back).</summary>
        public bool IsUndone { get; }

        public bool IsCurrent { get; }
    }

    /// <summary>
    /// The History panel: the document's starting state followed by every undoable
    /// step, then the undone (redoable) steps dimmed. Clicking a row jumps there.
    /// </summary>
    public partial class MainViewModel
    {
        public ObservableCollection<HistoryStepViewModel> HistorySteps { get; } = new();

        private void RefreshHistorySteps()
        {
            HistorySteps.Clear();
            var history = ActiveDocument?.Layers.History;
            if (history is null) return;

            var undo = history.UndoNames;
            var redo = history.RedoNames;
            var current = undo.Count;

            // Row 0 is the state before the oldest kept step.
            HistorySteps.Add(new HistoryStepViewModel(0, history.DroppedCount > 0 ? "(older steps dropped)" : "Open", false, current == 0));
            for (var i = 0; i < undo.Count; i++)
                HistorySteps.Add(new HistoryStepViewModel(i + 1, undo[i], false, current == i + 1));
            for (var i = 0; i < redo.Count; i++)
                HistorySteps.Add(new HistoryStepViewModel(current + i + 1, redo[i], true, false));
        }

        /// <summary>Clicking a History row: undo or redo until that state is current.</summary>
        [RelayCommand]
        private void JumpToHistoryStep(HistoryStepViewModel? step)
        {
            if (step is null || IsSaving || ActiveDocument is not { } document) return;
            if (step.IsCurrent) return;

            document.Layers.History.JumpTo(step.UndoCount);
        }
    }
}
