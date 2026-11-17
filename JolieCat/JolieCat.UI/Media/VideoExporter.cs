using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// Writes an animation as an MP4 (H.264) video with Windows' own video encoder
    /// (Media Foundation, through <see cref="MediaComposition"/>) - nothing extra to
    /// install. Each distinct picture becomes a still clip lasting its frames.
    /// </summary>
    public static class VideoExporter
    {
        private const int MinWidth = 160;
        private const int MinHeight = 120;

        /// <param name="pictures">The pictures and how many frames each lasts; all the same size.</param>
        /// <param name="audio">Sounds to add: file, start (seconds into the video) and volume (0..1).</param>
        public static async Task WriteMp4Async(string path, IReadOnlyList<(SKBitmap Picture, int Frames)> pictures, double frameRate,
            IReadOnlyList<(string Path, double StartSeconds, double Volume)>? audio = null)
        {
            if (pictures.Count == 0) throw new ArgumentException("No frames.", nameof(pictures));

            // Windows' encoder won't make videos smaller than 160 x 120: small animations
            // (sprites) are enlarged by a whole factor, pixel-sharp. H.264 needs even sizes.
            int sourceW = pictures[0].Picture.Width, sourceH = pictures[0].Picture.Height;
            var factor = Math.Max(1, (int)Math.Ceiling(Math.Max(MinWidth / (double)sourceW, MinHeight / (double)sourceH)));
            var width = (sourceW * factor) & ~1;
            var height = (sourceH * factor) & ~1;

            var temp = Path.Combine(Path.GetTempPath(), "JolieCatVideo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var composition = new MediaComposition();
                var fps = Math.Max(1, frameRate);
                for (var i = 0; i < pictures.Count; i++)
                {
                    // A video has no transparency: the picture goes on white.
                    var file = Path.Combine(temp, $"{i:00000}.png");
                    using (var frame = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
                    {
                        using (var canvas = new SKCanvas(frame))
                        {
                            canvas.Clear(SKColors.White);
                            using var picture = SKImage.FromBitmap(pictures[i].Picture);
                            canvas.DrawImage(picture, new SKRect(0, 0, sourceW * factor, sourceH * factor),
                                factor == 1 ? default : new SKSamplingOptions(SKFilterMode.Nearest));
                        }
                        using var image = SKImage.FromBitmap(frame);
                        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                        await File.WriteAllBytesAsync(file, data.ToArray());
                    }

                    var storageFile = await StorageFile.GetFileFromPathAsync(file);
                    var duration = TimeSpan.FromSeconds(pictures[i].Frames / fps);
                    composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(storageFile, duration));
                }

                var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
                profile.Video.Width = (uint)width;
                profile.Video.Height = (uint)height;
                profile.Video.FrameRate.Numerator = (uint)Math.Round(fps);
                profile.Video.FrameRate.Denominator = 1;
                profile.Video.Bitrate = (uint)Math.Clamp(width * height * fps * 0.15, 500_000, 40_000_000);

                // Sound: each file starts at its time and is cut off where the video ends.
                var videoSeconds = pictures.Sum(p => p.Frames) / fps;
                foreach (var (soundPath, start, volume) in audio ?? Array.Empty<(string, double, double)>())
                {
                    if (start >= videoSeconds) continue;
                    var soundFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(soundPath));
                    var track = await BackgroundAudioTrack.CreateFromFileAsync(soundFile);
                    track.Delay = TimeSpan.FromSeconds(start);
                    track.Volume = volume;
                    var tooLong = track.OriginalDuration.TotalSeconds - (videoSeconds - start);
                    if (tooLong > 0) track.TrimTimeFromEnd = TimeSpan.FromSeconds(tooLong);
                    composition.BackgroundAudioTracks.Add(track);
                }
                if (composition.BackgroundAudioTracks.Count == 0) profile.Audio = null;

                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var output = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
                var result = await composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile);
                if (result != TranscodeFailureReason.None)
                    throw new InvalidOperationException($"The video couldn't be written ({result}).");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { /* temporary files only */ }
            }
        }
    }
}
