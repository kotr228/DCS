using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Actions;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One action in the Actions panel.</summary>
    public partial class ActionItemViewModel : ObservableObject
    {
        public ActionItemViewModel(RecordedAction model)
        {
            Model = model;
            name = model.Name;
            foreach (var step in model.Steps) Steps.Add(step);
        }

        public RecordedAction Model { get; }

        public ObservableCollection<ActionStep> Steps { get; } = new();

        [ObservableProperty]
        private string name;

        partial void OnNameChanged(string value) => Model.Name = value;

        public void Add(ActionStep step)
        {
            Model.Steps.Add(step);
            Steps.Add(step);
        }

        public void Remove(ActionStep step)
        {
            Model.Steps.Remove(step);
            Steps.Remove(step);
        }
    }

    /// <summary>
    /// The Actions panel (Photoshop's): record a sequence of commands - filters,
    /// adjustments, Camera Raw, Image Size, rotating/flipping, layer commands - and play
    /// it back on any document. Commands report themselves through <see cref="Record"/>;
    /// <see cref="Player"/> (set by MainViewModel) runs a step. Actions are kept in
    /// %AppData%\JolieCat\actions.json.
    /// </summary>
    public partial class ActionsViewModel : ObservableObject
    {
        private readonly string? _storePath;

        /// <param name="storePath">Where actions are saved; null keeps them in memory only (tests).</param>
        public ActionsViewModel(string? storePath)
        {
            _storePath = storePath;
            if (storePath is not null)
                foreach (var action in ActionLibrary.Load(storePath))
                    Actions.Add(new ActionItemViewModel(action));

            if (Actions.Count == 0 && storePath is not null)
                Actions.Add(new ActionItemViewModel(DefaultAction()));
            SelectedAction = Actions.FirstOrDefault();
        }

        public static string DefaultStorePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JolieCat", "actions.json");

        public ObservableCollection<ActionItemViewModel> Actions { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(DeleteActionCommand), nameof(RecordCommand))]
        private ActionItemViewModel? selectedAction;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteStepCommand))]
        private ActionStep? selectedStep;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(RecordCommand), nameof(StopCommand))]
        private bool isRecording;

        [ObservableProperty]
        private string status = string.Empty;

        /// <summary>Runs one step on the active document; returns false if it couldn't.</summary>
        public Func<ActionStep, bool>? Player { get; set; }

        private bool _playing;

        /// <summary>Called by every recordable command after it has been applied.</summary>
        public void Record(ActionStep step)
        {
            if (!IsRecording || _playing || SelectedAction is null) return;
            SelectedAction.Add(step);
            Save();
        }

        [RelayCommand]
        private void NewAction()
        {
            var action = new ActionItemViewModel(new RecordedAction { Name = $"Action {Actions.Count + 1}" });
            Actions.Add(action);
            SelectedAction = action;
            IsRecording = true;
            Status = "Recording... use filters, adjustments and image/layer commands, then press Stop.";
            Save();
        }

        private bool CanRecord() => SelectedAction is not null && !IsRecording;

        /// <summary>Records more steps at the end of the selected action.</summary>
        [RelayCommand(CanExecute = nameof(CanRecord))]
        private void Record()
        {
            IsRecording = true;
            Status = "Recording...";
        }

        [RelayCommand(CanExecute = nameof(IsRecording))]
        private void Stop()
        {
            IsRecording = false;
            Status = SelectedAction is { } a ? $"{a.Name}: {a.Steps.Count} step(s)." : string.Empty;
            Save();
        }

        private bool CanPlay() => SelectedAction is { Steps.Count: > 0 } && !IsRecording;

        [RelayCommand(CanExecute = nameof(CanPlay))]
        private void Play()
        {
            if (SelectedAction is null || Player is null) return;
            _playing = true;
            var done = 0;
            try
            {
                foreach (var step in SelectedAction.Steps.ToList())
                {
                    if (!Player(step))
                    {
                        Status = $"Stopped at \"{step.Title}\" - it can't run on this document.";
                        return;
                    }
                    done++;
                }
                Status = $"Played {done} step(s).";
            }
            finally
            {
                _playing = false;
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectedAction))]
        private void DeleteAction()
        {
            if (SelectedAction is null) return;
            var index = Actions.IndexOf(SelectedAction);
            Actions.Remove(SelectedAction);
            IsRecording = false;
            SelectedAction = Actions.Count == 0 ? null : Actions[Math.Min(index, Actions.Count - 1)];
            Save();
        }

        private bool HasSelectedAction() => SelectedAction is not null;

        [RelayCommand(CanExecute = nameof(HasSelectedStep))]
        private void DeleteStep()
        {
            if (SelectedAction is null || SelectedStep is null) return;
            SelectedAction.Remove(SelectedStep);
            Save();
            PlayCommand.NotifyCanExecuteChanged();
        }

        private bool HasSelectedStep() => SelectedStep is not null;

        partial void OnSelectedActionChanged(ActionItemViewModel? value)
        {
            if (IsRecording) IsRecording = false;
        }

        public void Save()
        {
            if (_storePath is null) return;
            try
            {
                ActionLibrary.Save(_storePath, Actions.Select(a => a.Model));
            }
            catch (IOException)
            {
                // Saving actions is a convenience; a locked file shouldn't break editing.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>A ready-made example, so the panel isn't empty the first time.</summary>
        private static RecordedAction DefaultAction() => new()
        {
            Name = "Vintage look",
            Steps =
            {
                new ActionStep { Kind = "CameraRaw", Command = "CameraRaw", Title = "Camera Raw (warm, faded)" }
                    .With("Temperature", 40).With("Contrast", -15).With("Blacks", 30).With("Vibrance", -20),
                new ActionStep { Kind = "Filter", Command = nameof(FilterKind.Vignette), Title = "Vignette (40%)" }.With("Amount", 40),
                new ActionStep { Kind = "Filter", Command = nameof(FilterKind.Noise), Title = "Noise (6%)" }.With("Amount", 6),
            },
        };
    }
}
