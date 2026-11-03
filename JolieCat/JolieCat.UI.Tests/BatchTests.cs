using System.IO;
using JolieCat.Core.Actions;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class BatchTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "JolieCatBatch_" + Guid.NewGuid().ToString("N"));

        public BatchTests() => Directory.CreateDirectory(Path.Combine(_root, "in", "sub"));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

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

        private void WritePng(string relativePath, int width, int height, SKColor color)
        {
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(color);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(_root, "in", relativePath), data.ToArray());
        }

        private BatchViewModel Batch(params ActionStep[] steps)
        {
            var action = new RecordedAction { Name = "Test" };
            action.Steps.AddRange(steps);
            return new BatchViewModel(new[] { action })
            {
                SourceFolder = Path.Combine(_root, "in"),
                OutputFolder = Path.Combine(_root, "out"),
            };
        }

        [Fact]
        public void ProcessFile_PlaysTheAction_AndSavesTheResult() => Sta(() =>
        {
            WritePng("a.png", 100, 50, new SKColor(100, 100, 100));
            var batch = Batch(
                new ActionStep { Kind = "Filter", Command = "Invert", Title = "Invert" },
                new ActionStep { Kind = "ImageSize", Title = "Size" }.With("Width", 40).With("Height", 40).With("Proportional", 1));
            batch.Format = BatchFormat.Png;
            batch.Suffix = "_done";

            Assert.Null(batch.ProcessFile(Path.Combine(_root, "in", "a.png")));

            using var result = SKBitmap.Decode(Path.Combine(_root, "out", "a_done.png"));
            Assert.Equal(40, result.Width);
            Assert.Equal(20, result.Height);           // aspect ratio kept
            Assert.InRange(result.GetPixel(20, 10).Red, 150, 160); // inverted 100 -> 155
        });

        [Fact]
        public void PsdFiles_AreProcessed_WithTheirLayers() => Sta(() =>
        {
            using (var scene = new JolieCat.Core.Documents.Scene("P"))
            {
                scene.AddLayer("Bg", 30, 20).Canvas.Clear(SKColors.White);
                var top = scene.AddLayer("Top", 30, 20);
                using var red = new SKPaint { Color = SKColors.Red };
                top.Canvas.DrawRect(0, 0, 10, 20, red);
                JolieCat.Core.Formats.PsdCodec.Write(Path.Combine(_root, "in", "layered.psd"), scene, 30, 20);
            }
            var batch = Batch(new ActionStep { Kind = "Layers", Command = "FlipCanvasHorizontal", Title = "Flip" });
            batch.Format = BatchFormat.Png;

            Assert.Contains(batch.FindFiles(), f => f.EndsWith("layered.psd", StringComparison.Ordinal));
            Assert.Null(batch.ProcessFile(Path.Combine(_root, "in", "layered.psd")));

            using var result = SKBitmap.Decode(Path.Combine(_root, "out", "layered.png"));
            Assert.Equal(SKColors.Red, result.GetPixel(25, 10));   // flipped: red is now on the right
            Assert.Equal(SKColors.White, result.GetPixel(2, 10));
        });

        [Fact]
        public void FindFiles_OnlyImages_AndSubfoldersOnRequest() => Sta(() =>
        {
            WritePng("a.png", 4, 4, SKColors.Red);
            WritePng(Path.Combine("sub", "b.png"), 4, 4, SKColors.Red);
            File.WriteAllText(Path.Combine(_root, "in", "notes.txt"), "hi");
            var batch = Batch();

            Assert.Single(batch.FindFiles());
            batch.IncludeSubfolders = true;
            Assert.Equal(2, batch.FindFiles().Count);

            // Subfolder files keep their folder under the output.
            batch.Format = BatchFormat.Jpeg;
            var output = batch.OutputPathFor(Path.Combine(_root, "in", "sub", "b.png"));
            Assert.Equal(Path.Combine(_root, "out", "sub", "b.jpg"), output);
        });

        [Fact]
        public void AStepThatCantRun_ReportsTheFile_AndSavesNothing() => Sta(() =>
        {
            WritePng("a.png", 10, 10, SKColors.Red);
            var batch = Batch(new ActionStep { Kind = "Mystery", Title = "Mystery step" });

            var error = batch.ProcessFile(Path.Combine(_root, "in", "a.png"));

            Assert.Contains("Mystery step", error);
            Assert.False(Directory.Exists(Path.Combine(_root, "out")) && Directory.EnumerateFiles(Path.Combine(_root, "out")).Any());
        });

        [Fact]
        public void LevelsAndCurvesSteps_Replay() => Sta(() =>
        {
            var document = new DocumentViewModel(new ToolboxViewModel(), "T");
            var layer = document.Layers.ActiveLayer!.Model;
            layer.Canvas.Clear(new SKColor(100, 100, 100));

            // Levels: input white 200 brightens 100 to ~128.
            Assert.True(MainViewModel.PlayActionStep(new ActionStep { Kind = "Levels" }.With("InWhite", 200).With("Gamma", 1), document));
            Assert.InRange(layer.Bitmap.GetPixel(3, 3).Red, 125, 130);

            // Curves: an inverted line.
            Assert.True(MainViewModel.PlayActionStep(new ActionStep { Kind = "Curves" }.With("Points", "0:255;255:0"), document));
            Assert.InRange(layer.Bitmap.GetPixel(3, 3).Red, 125, 130);
            Assert.Equal(new (double, double)[] { (0, 255), (255, 0) }, MainViewModel.ParseCurvePoints("0:255;255:0;bad"));
        });
    }
}
