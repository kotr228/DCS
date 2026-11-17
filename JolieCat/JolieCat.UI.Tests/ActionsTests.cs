using JolieCat.Core.Actions;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ActionsTests
    {
        private static void Sta(Action test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { test(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }

        private static MainViewModel NewMain(out ActionsViewModel actions)
        {
            var main = new MainViewModel();
            actions = new ActionsViewModel(storePath: null);
            main.UseActionsPanel(actions);
            return main;
        }

        [Fact]
        public void LayerCommands_AreRecorded_OnlyWhileRecording() => Sta(() =>
        {
            var main = NewMain(out var actions);
            main.Layers.Rotate180Command.Execute(null);   // not recording yet

            actions.NewActionCommand.Execute(null);
            main.Layers.AddLayerCommand.Execute(null);
            main.Layers.FlipCanvasHorizontalCommand.Execute(null);
            actions.StopCommand.Execute(null);
            main.Layers.DuplicateLayerCommand.Execute(null); // stopped

            var steps = actions.SelectedAction!.Steps;
            Assert.Equal(new[] { "AddLayer", "FlipCanvasHorizontal" }, steps.Select(s => s.Command));
            Assert.Equal("New Layer", steps[0].Title);
        });

        [Fact]
        public void Playing_RunsEveryStep_OnTheActiveDocument() => Sta(() =>
        {
            var main = NewMain(out var actions);
            var action = new RecordedAction
            {
                Name = "Test",
                Steps =
                {
                    new ActionStep { Kind = "Layers", Command = "AddLayer", Title = "New Layer" },
                    new ActionStep { Kind = "Layers", Command = "Rotate90Clockwise", Title = "Rotate" },
                },
            };
            actions.Actions.Add(new ActionItemViewModel(action));
            actions.SelectedAction = actions.Actions[^1];

            int width = main.Layers.DocumentWidth, height = main.Layers.DocumentHeight, count = main.Layers.Scene.Layers.Count;
            actions.PlayCommand.Execute(null);

            Assert.Equal(count + 1, main.Layers.Scene.Layers.Count);
            Assert.Equal(height, main.Layers.DocumentWidth);
            Assert.Equal(width, main.Layers.DocumentHeight);
            Assert.StartsWith("Played 2", actions.Status);

            // Playing doesn't record into the action being played.
            Assert.Equal(2, action.Steps.Count);
        });

        [Fact]
        public void FilterAndCameraRawSteps_ChangeThePixels() => Sta(() =>
        {
            var main = NewMain(out _);
            var layer = main.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(new SKColor(100, 100, 100));

            Assert.True(main.PlayActionStep(new ActionStep { Kind = "Filter", Command = "Invert" }));
            Assert.Equal(155, layer.Bitmap.GetPixel(5, 5).Red);

            Assert.True(main.PlayActionStep(new ActionStep { Kind = "CameraRaw" }.With("Temperature", 60)));
            var warm = layer.Bitmap.GetPixel(5, 5);
            Assert.True(warm.Red > warm.Blue + 20, $"{warm}");

            // Each step is its own undo step.
            main.UndoCommand.Execute(null);
            Assert.Equal(155, layer.Bitmap.GetPixel(5, 5).Red);
        });

        [Fact]
        public void ImageSizeStep_KeepsTheAspectRatio_OnOtherImages() => Sta(() =>
        {
            var main = NewMain(out _);
            var w = main.Layers.DocumentWidth;
            var h = main.Layers.DocumentHeight;

            Assert.True(main.PlayActionStep(new ActionStep { Kind = "ImageSize" }.With("Width", 200).With("Height", 999).With("Proportional", 1)));
            Assert.Equal(200, main.Layers.DocumentWidth);
            Assert.Equal((int)Math.Round(200.0 * h / w), main.Layers.DocumentHeight);
        });

        [Fact]
        public void UnknownSteps_StopPlayback_WithAMessage() => Sta(() =>
        {
            var main = NewMain(out var actions);
            actions.Actions.Add(new ActionItemViewModel(new RecordedAction
            {
                Steps = { new ActionStep { Kind = "Teleport", Title = "Teleport" }, new ActionStep { Kind = "Layers", Command = "AddLayer" } },
            }));
            actions.SelectedAction = actions.Actions[^1];
            var count = main.Layers.Scene.Layers.Count;

            actions.PlayCommand.Execute(null);

            Assert.Contains("Teleport", actions.Status);
            Assert.Equal(count, main.Layers.Scene.Layers.Count);
        });

        [Fact]
        public void Library_RoundTripsThroughJson()
        {
            var actions = new[]
            {
                new RecordedAction { Name = "A", Steps = { new ActionStep { Kind = "Filter", Command = "GaussianBlur", Title = "Blur" }.With("Amount", 2.5) } },
            };
            var back = ActionLibrary.Deserialize(ActionLibrary.Serialize(actions));

            Assert.Single(back);
            Assert.Equal("A", back[0].Name);
            Assert.Equal(2.5, back[0].Steps[0].Number("Amount"));
            Assert.Empty(ActionLibrary.Deserialize("not json"));
        }
    }
}
