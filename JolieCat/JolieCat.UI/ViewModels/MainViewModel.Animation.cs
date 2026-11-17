using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Shared.Enums;

namespace JolieCat.UI.ViewModels
{
    /// <summary>The Animation menu. Animation belongs to Clipbar Animation projects (their
    /// own project type); for any other document the menu offers to open a copy of it
    /// as one. Each command opens the Timeline first, so its result is right there.</summary>
    public partial class MainViewModel
    {
        /// <summary>Whether the active document is a Clipbar Animation project.</summary>
        public bool IsAnimationDocument => ActiveDocument?.Layers.ProjectType == ProjectType.ClipbarAnimation;

        public bool CanConvertToAnimation => ActiveDocument is not null && !IsAnimationDocument;

        private void RefreshAnimationCommands()
        {
            OnPropertyChanged(nameof(IsAnimationDocument));
            OnPropertyChanged(nameof(CanConvertToAnimation));
            ToggleTimelineCommand.NotifyCanExecuteChanged();
            AnimationCommand.NotifyCanExecuteChanged();
            ImportFramesCommand.NotifyCanExecuteChanged();
            ExportAnimationCommand.NotifyCanExecuteChanged();
            ConvertToClipbarAnimationCommand.NotifyCanExecuteChanged();
            AddMarkerCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Animation &gt; Show Timeline (Shift+Ctrl+T): shows or hides the Timeline.</summary>
        [RelayCommand(CanExecute = nameof(IsAnimationDocument))]
        private void ToggleTimeline()
        {
            if (ActiveDocument is not { } document || !IsAnimationDocument) return;
            document.WorkspaceMode = document.IsTimelineMode ? WorkspaceMode.Design : WorkspaceMode.Timeline;
        }

        /// <summary>Animation &gt; Convert to Clipbar Animation: opens a copy of the active
        /// document as a new Clipbar Animation project (the original stays as it is).</summary>
        [RelayCommand(CanExecute = nameof(CanConvertToAnimation))]
        private void ConvertToClipbarAnimation()
        {
            if (IsSaving || ActiveDocument is not { } source || IsAnimationDocument) return;
            var title = $"{System.IO.Path.GetFileNameWithoutExtension(source.Title)} (Clipbar)";
            var scene = new Scene(title);
            scene.RestoreLayers(source.Layers.Scene.CaptureLayers(), 0);
            scene.ActiveLayer = scene.Layers.Count > 0 ? scene.Layers[^1] : null;

            var document = new DocumentViewModel(Toolbox, title, ProjectType.ClipbarAnimation);
            RegisterDocument(document);
            document.Layers.LoadScene(scene, title, ProjectType.ClipbarAnimation);
            Documents.Add(document);
            ActiveDocument = document;
            Canvas.ResetView();
        }

        /// <summary>Animation &gt; Import Frames: an animated GIF, or several pictures, as frames.</summary>
        [RelayCommand(CanExecute = nameof(IsAnimationDocument))]
        private void ImportFrames()
        {
            if (IsSaving || ActiveDocument is not { } document) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Frames: an animated GIF, or several pictures (in name order)",
                Filter = "Images and animations|*.gif;*.webp;*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return;

            System.Collections.Generic.List<(SkiaSharp.SKBitmap Picture, int Frames)>? pictures = null;
            try
            {
                pictures = Media.FrameDecoder.Decode(dialog.FileNames, document.Timeline.FrameRate);
                document.WorkspaceMode = WorkspaceMode.Timeline;
                document.Timeline.ImportFrames(pictures, System.IO.Path.GetFileNameWithoutExtension(dialog.FileNames[0]));
            }
            catch (System.Exception ex)
            {
                JolieCat.UI.MessageBox.Show($"Couldn't import the frames:\n{ex.Message}", "Import Frames",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                if (pictures is not null) foreach (var (picture, _) in pictures) picture.Dispose();
            }
        }

        /// <summary>Animation &gt; Add Marker: a named spot at the playhead.</summary>
        [RelayCommand(CanExecute = nameof(IsAnimationDocument))]
        private void AddMarker()
        {
            if (ActiveDocument is not { } document) return;
            var options = new FieldsDialogViewModel("Add Marker", "A named spot on the timeline - a beat, a step, a word - to line the animation up with.");
            var label = options.Add(new TextDialogField("Name", ""));
            if (!Views.FieldsDialog.Show(options)) return;
            document.WorkspaceMode = WorkspaceMode.Timeline;
            document.Timeline.AddMarker(label.Text);
        }

        /// <summary>Runs a Timeline command by name, opening the Timeline first.</summary>
        [RelayCommand(CanExecute = nameof(CanAnimate))]
        private void Animation(string which)
        {
            if (ActiveDocument is not { } document || !IsAnimationDocument) return;
            document.WorkspaceMode = WorkspaceMode.Timeline;
            var timeline = document.Timeline;
            switch (which)
            {
                case "NewFrame": timeline.NewFrameCommand.Execute(null); break;
                case "DuplicateFrame": timeline.DuplicateFrameCommand.Execute(null); break;
                case "DeleteFrame": timeline.DeleteFrameCommand.Execute(null); break;
                case "OnionSkin": timeline.OnionSkin = !timeline.OnionSkin; break;
                case "AnimateLayer": timeline.AnimateActiveLayerCommand.Execute(null); break;
                case "AddCamera": timeline.AddCameraCommand.Execute(null); break;
                case "ReverseKeys": timeline.ReverseKeysCommand.Execute(null); break;
                case "StretchKeys":
                {
                    var options = new FieldsDialogViewModel("Stretch Keyframes",
                        "Spreads the selected keys (or all keys of the selected layer) out in time, or squeezes them together, from the first one. 200% plays twice as slow.");
                    var percent = options.Add(new SliderDialogField("Length", 10, 500, 200, "%"));
                    if (Views.FieldsDialog.Show(options)) timeline.StretchKeys(percent.Value);
                    break;
                }
                case "MoveAlongPath":
                {
                    if (Canvas.PenPath is not { } pen)
                    {
                        JolieCat.UI.MessageBox.Show("Draw the path with the Pen tool first, then pick the layer's track and choose Move Along Path.",
                            "Move Along Path", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        break;
                    }
                    var options = new FieldsDialogViewModel("Move Along Path",
                        "The selected layer's middle travels along the Pen path from the playhead, at an even speed (a key every frame).");
                    var frames = options.Add(new SliderDialogField("Frames", 2, 600, Math.Max(2, Math.Round(timeline.FrameRate * 2))));
                    var orient = options.Add(new ChoiceDialogField("Turn to Follow the Path", new[] { "No", "Yes" }));
                    if (!Views.FieldsDialog.Show(options)) break;
                    using var curve = pen.ToSKPath();
                    if (!timeline.MoveAlongPath(curve, (int)Math.Round(frames.Value), orient.Selected == "Yes"))
                        JolieCat.UI.MessageBox.Show("Pick a layer's track first (Animate Layer).", "Move Along Path",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    else Canvas.CancelPenPath();
                    break;
                }
                case "MultiLayerFrames": timeline.MakeFramesMultiLayerCommand.Execute(null); break;
                case "NextMarker": timeline.NextMarkerCommand.Execute(null); break;
                case "PreviousMarker": timeline.PreviousMarkerCommand.Execute(null); break;
                case "SetIn": timeline.SetInPointCommand.Execute(null); break;
                case "SetOut": timeline.SetOutPointCommand.Execute(null); break;
                case "ClearWorkArea": timeline.ClearWorkAreaCommand.Execute(null); break;
                case "AddAudio": timeline.AddAudioCommand.Execute(null); break;
                case "Play": timeline.TogglePlaybackCommand.Execute(null); break;
                case "NextFrame": timeline.StepForwardCommand.Execute(null); break;
                case "PreviousFrame": timeline.StepBackwardCommand.Execute(null); break;
            }
        }

        private bool CanAnimate(string? which) => IsAnimationDocument;
    }
}
