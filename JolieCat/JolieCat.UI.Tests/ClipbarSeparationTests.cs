using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ClipbarSeparationTests
    {
        [Fact]
        public void AnimationIsForClipbarProjects_AndAnyDocumentCanBeConverted()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var main = new MainViewModel();                 // opens a Standard Image document
                    var source = main.ActiveDocument!;
                    using (var red = new SKPaint { Color = SKColors.Red })
                        source.Layers.ActiveLayer!.Model.Canvas.DrawRect(0, 0, 50, 50, red);

                    Assert.False(main.IsAnimationDocument);
                    Assert.False(main.AnimationCommand.CanExecute("NewFrame"));
                    Assert.False(main.ToggleTimelineCommand.CanExecute(null));
                    main.SetWorkspaceModeCommand.Execute(WorkspaceMode.Timeline);
                    Assert.False(source.IsTimelineMode);           // no Timeline outside Clipbar projects
                    Assert.True(main.ConvertToClipbarAnimationCommand.CanExecute(null));

                    main.ConvertToClipbarAnimationCommand.Execute(null);
                    var copy = main.ActiveDocument!;
                    Assert.NotSame(source, copy);
                    Assert.Equal(ProjectType.ClipbarAnimation, copy.Layers.ProjectType);
                    Assert.True(copy.IsTimelineMode);
                    Assert.Equal(SKColors.Red, copy.Layers.Scene.Layers[0].Bitmap.GetPixel(10, 10));
                    Assert.Equal(ProjectType.StandardImage, source.Layers.ProjectType);   // the original is untouched

                    Assert.True(main.AnimationCommand.CanExecute("NewFrame"));
                    main.AnimationCommand.Execute("NewFrame");
                    Assert.Single(copy.Timeline.FrameClips);
                    Assert.False(main.ConvertToClipbarAnimationCommand.CanExecute(null));
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new Exception(error.Message, error);
        }
    }
}
