using JolieCat.UI.Rendering;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    /// <summary>The renderer's layer cache must produce the same picture as drawing
    /// every layer directly.</summary>
    public class RenderCacheTests
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

        /// <param name="zoomedOut">true: fitted below 100% (bilinear filtering);
        /// false: exactly 100% at a whole-pixel offset. In-between zooms with nearest-
        /// neighbour sampling are avoided: there a device pixel can sit exactly between
        /// two source pixels, and float rounding may pick either one in either path.</param>
        private static DocumentViewModel BuildDocument(Shared.Enums.BlendMode topBlend, bool zoomedOut = true)
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "R", Shared.Enums.ProjectType.StandardImage, 400, 300, SKColors.White);
            for (var i = 0; i < 4; i++)
            {
                doc.Layers.AddLayerCommand.Execute(null);
                using var paint = new SKPaint { Color = new SKColor((byte)(60 * i), 120, (byte)(200 - 40 * i), 180), IsAntialias = true };
                doc.Layers.Scene.ActiveLayer!.Canvas.DrawCircle(80 + 60 * i, 150, 90, paint);
            }

            doc.Layers.Scene.Layers[^1].BlendMode = topBlend;
            doc.Layers.ActiveLayer = doc.Layers.Items[2]; // a layer in the middle of the stack
            doc.Canvas.SetViewportSize(500, 400);
            if (zoomedOut)
            {
                doc.Canvas.ZoomOut();
            }
            else
            {
                doc.Canvas.ZoomActualPixels();
                doc.Canvas.PanX = 40;
                doc.Canvas.PanY = 30;
            }

            return doc;
        }

        private static SKColor[] Render(DocumentViewModel doc, int frames)
        {
            var info = new SKImageInfo(500, 400, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(bitmap);
            for (var i = 0; i < frames; i++) CanvasRenderer.Render(canvas, info, doc.Canvas);
            return bitmap.Pixels;
        }

        /// <summary>The middle layer inside a half-transparent group, and active.</summary>
        private static DocumentViewModel BuildGroupedDocument()
        {
            var doc = BuildDocument(Shared.Enums.BlendMode.Normal);
            var member = doc.Layers.ActiveLayer!.Model;
            doc.Layers.GroupLayersCommand.Execute(null);
            doc.Layers.Scene.ActiveLayer!.Opacity = 0.6;
            doc.Layers.ActiveLayer = doc.Layers.Items.First(i => i.Model == member);
            return doc;
        }

        /// <summary>The middle layer clipped to the one below it, and active.</summary>
        private static DocumentViewModel BuildClippedDocument()
        {
            var doc = BuildDocument(Shared.Enums.BlendMode.Normal);
            doc.Layers.ToggleClippingMaskCommand.Execute(null);
            return doc;
        }

        [Fact]
        public void CachedFrame_MatchesDirectFrame_OnAClippedLayer() => Sta(() =>
        {
            var direct = Render(BuildClippedDocument(), frames: 1);
            var cached = Render(BuildClippedDocument(), frames: 3);
            Assert.True(BuildClippedDocument().Layers.ActiveLayer!.Model.IsClipped);

            var maxDiff = 0;
            for (var i = 0; i < direct.Length; i++)
                maxDiff = Math.Max(maxDiff, Math.Max(Math.Abs(direct[i].Red - cached[i].Red), Math.Abs(direct[i].Blue - cached[i].Blue)));
            Assert.True(maxDiff <= 2, $"max difference {maxDiff}");
        });

        [Fact]
        public void CachedFrame_MatchesDirectFrame_InsideAGroup() => Sta(() =>
        {
            var direct = Render(BuildGroupedDocument(), frames: 1);
            var cached = Render(BuildGroupedDocument(), frames: 3);

            var maxDiff = 0;
            for (var i = 0; i < direct.Length; i++)
                maxDiff = Math.Max(maxDiff, Math.Max(Math.Abs(direct[i].Red - cached[i].Red), Math.Abs(direct[i].Blue - cached[i].Blue)));
            Assert.True(maxDiff <= 2, $"max difference {maxDiff}");
        });

        [Theory]
        [InlineData(Shared.Enums.BlendMode.Normal, true)]
        [InlineData(Shared.Enums.BlendMode.Normal, false)]
        [InlineData(Shared.Enums.BlendMode.Multiply, true)]
        [InlineData(Shared.Enums.BlendMode.Multiply, false)]
        public void CachedFrame_MatchesDirectFrame(Shared.Enums.BlendMode topBlend, bool zoomedOut) => Sta(() =>
        {
            var direct = Render(BuildDocument(topBlend, zoomedOut), frames: 1); // first frame never uses the cache
            var cached = Render(BuildDocument(topBlend, zoomedOut), frames: 3);

            var maxDiff = 0;
            var worst = 0;
            for (var i = 0; i < direct.Length; i++)
            {
                var d = Math.Max(Math.Abs(direct[i].Red - cached[i].Red), Math.Max(Math.Abs(direct[i].Green - cached[i].Green), Math.Abs(direct[i].Blue - cached[i].Blue)));
                if (d > maxDiff) { maxDiff = d; worst = i; }
            }

            Assert.True(maxDiff <= 3, $"diff {maxDiff} at ({worst % 500},{worst / 500}): direct {direct[worst]} cached {cached[worst]}");
        });

        [Fact]
        public void AdjustmentLayerAboveActive_CachedMatchesDirect() => Sta(() =>
        {
            DocumentViewModel Build()
            {
                var doc = BuildDocument(Shared.Enums.BlendMode.Normal);
                var active = doc.Layers.ActiveLayer;
                doc.Layers.BeginNewAdjustmentLayer(Core.Adjustments.AdjustmentSettings.Default(Core.Adjustments.AdjustmentKind.Invert));
                doc.Layers.CommitNewAdjustmentLayer();
                doc.Layers.ActiveLayer = active; // paint below the adjustment
                return doc;
            }

            var direct = Render(Build(), frames: 1);
            var cached = Render(Build(), frames: 3);

            var center = 200 * 500 + 250;
            Assert.True(direct[center].Red > 0 || direct[center].Green > 0 || direct[center].Blue > 0);
            var maxDiff = 0;
            for (var i = 0; i < direct.Length; i++)
                maxDiff = Math.Max(maxDiff, Math.Abs(direct[i].Red - cached[i].Red));
            Assert.InRange(maxDiff, 0, 3);
        });

        [Fact]
        public void InvertAdjustment_ShowsOnCanvas() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "R", Shared.Enums.ProjectType.StandardImage, 400, 300, SKColors.White);
            doc.Canvas.SetViewportSize(500, 400);
            doc.Canvas.ZoomOut();
            var before = Render(doc, 2);

            doc.Layers.BeginNewAdjustmentLayer(Core.Adjustments.AdjustmentSettings.Default(Core.Adjustments.AdjustmentKind.Invert));
            doc.Layers.CommitNewAdjustmentLayer();
            var after = Render(doc, 2);

            var center = 200 * 500 + 250;
            Assert.Equal(255, before[center].Red);  // white document
            Assert.True(after[center].Red < 5);     // inverted to black
        });

        [Fact]
        public void RotatedView_RendersTheDocumentTurned() => Sta(() =>
        {
            var doc = new DocumentViewModel(new ToolboxViewModel(), "R", Shared.Enums.ProjectType.StandardImage, 400, 100, SKColors.White);
            doc.Canvas.SetViewportSize(500, 400);
            var flat = Render(doc, 2);
            doc.Canvas.ViewRotation = 90;
            var turned = Render(doc, 2);

            // A wide, short document: at 90 degrees the area above the center turns white,
            // where the unrotated view shows the desk.
            var aboveCenter = 80 * 500 + 250;
            Assert.NotEqual(SKColors.White, flat[aboveCenter]);
            Assert.Equal(SKColors.White, turned[aboveCenter]);
        });

        [Fact]
        public void Cache_SeesUndoOnAnotherLayer() => Sta(() =>
        {
            var doc = BuildDocument(Shared.Enums.BlendMode.Normal);
            var before = Render(doc, 2);

            // Hide a layer below the active one: the cached frame must change.
            doc.Layers.Items[^1].IsVisible = false; // the background
            var after = Render(doc, 2);

            Assert.NotEqual(before[200 * 500 + 250], after[200 * 500 + 250]);
        });
    }
}
