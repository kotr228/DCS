using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace JolieCat.UI.Media
{
    /// <summary>Pictures for Animation &gt; Import Frames: every frame of an animated GIF
    /// (or WebP), or one picture per image file - with how many timeline frames each lasts.</summary>
    public static class FrameDecoder
    {
        /// <param name="paths">One animated file, or several still images (taken in name order).</param>
        public static List<(SKBitmap Picture, int Frames)> Decode(IReadOnlyList<string> paths, double frameRate)
        {
            var result = new List<(SKBitmap, int)>();
            try
            {
                foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"Can't read {Path.GetFileName(path)}.");
                    if (codec.FrameCount <= 1)
                    {
                        result.Add((SKBitmap.Decode(path) ?? throw new InvalidDataException($"Can't read {Path.GetFileName(path)}."), 1));
                        continue;
                    }
                    result.AddRange(DecodeAnimated(codec, frameRate));
                }
                return result;
            }
            catch
            {
                foreach (var (picture, _) in result) picture.Dispose();
                throw;
            }
        }

        /// <summary>An animated GIF's frames, each built on the one it depends on (GIF
        /// frames often only hold what changed), with durations in timeline frames.</summary>
        private static IEnumerable<(SKBitmap, int)> DecodeAnimated(SKCodec codec, double frameRate)
        {
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var frames = new SKBitmap[codec.FrameCount];
            for (var i = 0; i < codec.FrameCount; i++)
            {
                var bitmap = new SKBitmap(info);
                var required = codec.FrameInfo[i].RequiredFrame;
                if (required >= 0 && required < i) frames[required].CopyTo(bitmap);
                else bitmap.Erase(SKColors.Transparent);
                codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, required));
                frames[i] = bitmap;
            }
            for (var i = 0; i < frames.Length; i++)
            {
                var ms = codec.FrameInfo[i].Duration;
                var length = Math.Max(1, (int)Math.Round((ms <= 0 ? 100 : ms) * Math.Max(1, frameRate) / 1000.0));
                yield return (frames[i], length);
            }
        }
    }
}
