using System.Text;
using JolieCat.Core.Documents;
using JolieCat.Core.Serialization;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class ApngAndAudioEmbedTests
    {
        private static List<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
        {
            var list = new List<(string, byte[], uint)>();
            var i = 8;
            while (i + 8 <= png.Length)
            {
                var length = (png[i] << 24) | (png[i + 1] << 16) | (png[i + 2] << 8) | png[i + 3];
                var type = Encoding.ASCII.GetString(png, i + 4, 4);
                var data = png.AsSpan(i + 8, length).ToArray();
                var crc = (uint)((png[i + 8 + length] << 24) | (png[i + 9 + length] << 16) | (png[i + 10 + length] << 8) | png[i + 11 + length]);
                list.Add((type, data, crc));
                i += 12 + length;
            }
            return list;
        }

        [Fact]
        public void Crc32_MatchesThePngStandard() =>
            Assert.Equal(0xAE426082u, ApngEncoder.Crc32(Encoding.ASCII.GetBytes("IEND"), ReadOnlySpan<byte>.Empty));

        [Fact]
        public void Apng_HasValidFrames_AndKeepsTransparency()
        {
            var frames = new[] { SKColors.Red, SKColors.Transparent, SKColors.Blue }.Select(c =>
            {
                var b = new SKBitmap(20, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
                b.Erase(c);
                return b;
            }).ToList();
            using var ms = new MemoryStream();
            ApngEncoder.Write(ms, frames, new[] { 100, 250, 100 });
            var bytes = ms.ToArray();
            foreach (var f in frames) f.Dispose();

            var chunks = Chunks(bytes);
            Assert.All(chunks, c => Assert.Equal(c.Crc, ApngEncoder.Crc32(Encoding.ASCII.GetBytes(c.Type), c.Data)));
            Assert.Equal("IHDR", chunks[0].Type);
            Assert.Equal("acTL", chunks[1].Type);
            Assert.Equal(3, chunks[1].Data[3]);                           // 3 frames
            Assert.Equal(3, chunks.Count(c => c.Type == "fcTL"));
            Assert.Equal(2, chunks.Count(c => c.Type == "fdAT"));
            Assert.Equal("IEND", chunks[^1].Type);

            // Sequence numbers of fcTL and fdAT run 0, 1, 2, ...
            var sequence = chunks.Where(c => c.Type is "fcTL" or "fdAT")
                .Select(c => (c.Data[0] << 24) | (c.Data[1] << 16) | (c.Data[2] << 8) | c.Data[3]).ToList();
            Assert.Equal(Enumerable.Range(0, sequence.Count), sequence);
            var secondDelay = (chunks.Where(c => c.Type == "fcTL").ElementAt(1).Data[20] << 8) | chunks.Where(c => c.Type == "fcTL").ElementAt(1).Data[21];
            Assert.Equal(250, secondDelay);

            using var first = SKBitmap.Decode(bytes);                      // an ordinary viewer shows frame 1
            Assert.Equal(SKColors.Red, first.GetPixel(5, 5));
        }

        [Fact]
        public void Sound_IsStoredInTheProject_AndComesBackWithoutTheOriginal()
        {
            var folder = Path.Combine(Path.GetTempPath(), "jc_embed_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var oldCache = ProjectSerializer.AudioCacheFolder;
            ProjectSerializer.AudioCacheFolder = Path.Combine(folder, "cache");
            try
            {
                var sound = Path.Combine(folder, "beat.wav");
                var soundBytes = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
                File.WriteAllBytes(sound, soundBytes);
                var scene = new Scene("p");
                scene.AddLayer("bg", 8, 8);
                var project = Path.Combine(folder, "p.jolie");
                ProjectSerializer.Save(project, scene,
                    new[] { new TimelineTrackData { Name = "Audio: beat", AudioPath = sound, Clips = { new TimelineClipData { Name = "beat", LengthFrames = 10 } } } },
                    240, 24, ProjectType.ClipbarAnimation);
                File.Delete(sound);                                       // the original is gone

                var loaded = ProjectSerializer.Load(project);
                var track = Assert.Single(loaded.TimelineTracks);
                Assert.NotNull(track.AudioPath);
                Assert.True(File.Exists(track.AudioPath));
                Assert.Equal(soundBytes, File.ReadAllBytes(track.AudioPath!));
                Assert.EndsWith(".wav", track.AudioPath);

                var again = ProjectSerializer.Load(project);              // same content: same cached file
                Assert.Equal(track.AudioPath, again.TimelineTracks[0].AudioPath);
            }
            finally
            {
                ProjectSerializer.AudioCacheFolder = oldCache;
                Directory.Delete(folder, true);
            }
        }
    }
}
