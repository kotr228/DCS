using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Rendering;
using JolieCat.Core.Serialization;
using JolieCat.UI.Views;
using Microsoft.Win32;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>File &gt; Export &gt; Export Animation: the timeline's frames as an
    /// animated GIF, numbered PNG files, or one sprite sheet image.</summary>
    public partial class MainViewModel
    {
        public static readonly string[] AnimationFormats = { "Animated GIF", "Animated PNG (transparent)", "MP4 Video", "PNG Sequence", "Sprite Sheet (PNG)" };

        [RelayCommand(CanExecute = nameof(IsAnimationDocument))]
        private async System.Threading.Tasks.Task ExportAnimation()
        {
            if (IsSaving || ActiveDocument is not { } document) return;
            if (document.Timeline.Tracks.SelectMany(t => t.Clips).All(c => c.TargetLayer is null))
            {
                MessageBox.Show("There are no animation frames yet: add some with New Frame on the Timeline.", "Export Animation",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var options = new FieldsDialogViewModel("Export Animation",
                $"Exports frames 0 to {document.Timeline.PlaybackEnd:0} at {document.Timeline.FrameRate:0} fps. A GIF loops forever.");
            var format = options.Add(new ChoiceDialogField("Format", AnimationFormats));
            var scale = options.Add(new SliderDialogField("Size", 10, 400, 100, "%"));
            if (!FieldsDialog.Show(options)) return;

            var name = Path.GetFileNameWithoutExtension(document.Title);
            string? target;
            if (format.Selected == "PNG Sequence")
            {
                var folder = new OpenFolderDialog { Title = "Choose where to save the frames" };
                if (folder.ShowDialog(Application.Current.MainWindow) != true) return;
                target = folder.FolderName;
            }
            else
            {
                var (extension, filter) = format.Selected switch
                {
                    "Animated GIF" => (".gif", "GIF animation|*.gif"),
                    "MP4 Video" => (".mp4", "MP4 video|*.mp4"),
                    "Animated PNG (transparent)" => (".png", "Animated PNG|*.png;*.apng"),
                    _ => ("_sheet.png", "PNG image|*.png"),
                };
                var save = new SaveFileDialog { Title = "Export Animation", FileName = name + extension, Filter = filter };
                if (save.ShowDialog(Application.Current.MainWindow) != true) return;
                target = save.FileName;
            }

            try
            {
                var written = await ExportAnimationToAsync(document, format.Selected, scale.Value / 100, target, name);
                MessageBox.Show($"Exported {written} frame(s).", "Export Animation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't export the animation:\n{ex.Message}", "Export Animation", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Writes the animation; returns how many distinct pictures were written.
        /// <paramref name="target"/> is a file (GIF, sprite sheet) or a folder (PNG sequence).</summary>
        internal static int ExportAnimationTo(DocumentViewModel document, string format, double scale, string target, string baseName) =>
            format == "MP4 Video"
                ? throw new InvalidOperationException("Use ExportAnimationToAsync for video.")
                : ExportAnimationToAsync(document, format, scale, target, baseName).GetAwaiter().GetResult();

        /// <summary>Writes the animation (videos are encoded asynchronously); returns how
        /// many distinct pictures were written.</summary>
        internal static async System.Threading.Tasks.Task<int> ExportAnimationToAsync(DocumentViewModel document, string format, double scale, string target, string baseName)
        {
            var layers = document.Layers;
            int w = Math.Max(1, (int)Math.Round(layers.DocumentWidth * scale));
            int h = Math.Max(1, (int)Math.Round(layers.DocumentHeight * scale));

            SKBitmap Render()
            {
                using var flat = SceneCompositor.Flatten(layers.Scene, layers.DocumentWidth, layers.DocumentHeight);
                if (w == flat.Width && h == flat.Height) return flat.Copy();
                return flat.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            var frames = document.Timeline.RenderFrames(Render);
            try
            {
                var frameMs = 1000.0 / Math.Max(1, document.Timeline.FrameRate);
                switch (format)
                {
                    case "MP4 Video":
                        await Media.VideoExporter.WriteMp4Async(target, frames, document.Timeline.FrameRate, document.Timeline.AudioForExport());
                        break;

                    case "Animated PNG (transparent)":
                    {
                        var apngOrder = Enumerable.Range(0, frames.Count).ToList();
                        if (document.Timeline.LoopMode == JolieCat.UI.ViewModels.Timeline.PlaybackLoop.PingPong && frames.Count > 2)
                            apngOrder.AddRange(Enumerable.Range(1, frames.Count - 2).Reverse());
                        using var apng = File.Create(target);
                        ApngEncoder.Write(apng, apngOrder.Select(i => frames[i].Picture).ToList(),
                            apngOrder.Select(i => (int)Math.Round(frames[i].Frames * frameMs)).ToList(),
                            plays: document.Timeline.LoopMode == JolieCat.UI.ViewModels.Timeline.PlaybackLoop.Once ? 1 : 0);
                        break;
                    }

                    case "Animated GIF":
                    {
                        // Ping-pong plays back to front after front to back (ends not doubled).
                        var order = Enumerable.Range(0, frames.Count).ToList();
                        if (document.Timeline.LoopMode == JolieCat.UI.ViewModels.Timeline.PlaybackLoop.PingPong && frames.Count > 2)
                            order.AddRange(Enumerable.Range(1, frames.Count - 2).Reverse());
                        using var file = File.Create(target);
                        GifEncoder.Write(file, order.Select(i => frames[i].Picture).ToList(),
                            order.Select(i => (int)Math.Round(frames[i].Frames * frameMs)).ToList(),
                            loop: document.Timeline.LoopMode != JolieCat.UI.ViewModels.Timeline.PlaybackLoop.Once);
                        break;
                    }

                    case "PNG Sequence":
                    {
                        // One file per timeline frame (held frames repeat), numbered from 000.
                        Directory.CreateDirectory(target);
                        var index = 0;
                        foreach (var (picture, count) in frames)
                        {
                            using var image = SKImage.FromBitmap(picture);
                            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                            var bytes = data.ToArray();
                            for (var k = 0; k < count; k++)
                                File.WriteAllBytes(Path.Combine(target, $"{baseName}_{index++:000}.png"), bytes);
                        }
                        break;
                    }

                    default:
                    {
                        // Distinct pictures in a near-square grid, row by row.
                        var columns = (int)Math.Ceiling(Math.Sqrt(frames.Count));
                        var rows = (int)Math.Ceiling(frames.Count / (double)columns);
                        using var sheet = new SKBitmap(columns * w, rows * h, SKColorType.Rgba8888, SKAlphaType.Premul);
                        using (var canvas = new SKCanvas(sheet))
                        {
                            canvas.Clear(SKColors.Transparent);
                            for (var i = 0; i < frames.Count; i++)
                                canvas.DrawBitmap(frames[i].Picture, i % columns * w, i / columns * h);
                        }
                        using var image = SKImage.FromBitmap(sheet);
                        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                        File.WriteAllBytes(target, data.ToArray());

                        // Where each frame is and how long it shows, for game engines.
                        var json = new
                        {
                            image = Path.GetFileName(target),
                            frameWidth = w,
                            frameHeight = h,
                            columns,
                            rows,
                            fps = document.Timeline.FrameRate,
                            frames = frames.Select((f, i) => new
                            {
                                x = i % columns * w,
                                y = i / columns * h,
                                w,
                                h,
                                durationMs = (int)Math.Round(f.Frames * frameMs),
                            }).ToList(),
                        };
                        File.WriteAllText(Path.ChangeExtension(target, ".json"),
                            System.Text.Json.JsonSerializer.Serialize(json, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        break;
                    }
                }
                return frames.Count;
            }
            finally
            {
                foreach (var (picture, _) in frames) picture.Dispose();
                layers.RequestRepaint();
            }
        }
    }
}
