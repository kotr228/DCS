using JolieCat.Core.Animation;

namespace JolieCat.Tests
{
    public class WavePeaksTests
    {
        private static MemoryStream Wav(short[] samples, int channels = 1, int rate = 8000)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write("RIFF"u8.ToArray()); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8.ToArray());
            w.Write("LIST"u8.ToArray()); w.Write(4); w.Write("INFO"u8.ToArray());          // an extra chunk to skip
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
            w.Write(rate * 2 * channels); w.Write((short)(2 * channels)); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(samples.Length * 2);
            foreach (var s in samples) w.Write(s);
            ms.Position = 0;
            return ms;
        }

        [Fact]
        public void Read_GivesLoudAndQuietSlices_AndTheLength()
        {
            var samples = new short[8000];
            for (var i = 0; i < 4000; i++) samples[i] = (short)(i % 2 == 0 ? 16000 : -16000); // loud first half
            using var wav = Wav(samples);
            var (peaks, seconds) = WavePeaks.Read(wav, 4);
            Assert.Equal(1.0, seconds, 3);
            Assert.InRange(peaks[0], 0.48, 0.5);
            Assert.InRange(peaks[1], 0.48, 0.5);
            Assert.Equal(0, peaks[2]);
            Assert.Equal(0, peaks[3]);

            var outline = WavePeaks.Outline(peaks);
            Assert.StartsWith("M0,0.5", outline);
            Assert.EndsWith("Z", outline);
        }

        [Fact]
        public void Read_HandlesStereo()
        {
            var samples = new short[] { 0, 32000, 0, -32000 }; // left silent, right loud
            using var wav = Wav(samples, channels: 2);
            var (peaks, _) = WavePeaks.Read(wav, 1);
            Assert.InRange(peaks[0], 0.97, 1.0);
        }
    }
}
