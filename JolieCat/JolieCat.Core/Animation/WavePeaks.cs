using System;
using System.IO;
using System.Text;

namespace JolieCat.Core.Animation
{
    /// <summary>
    /// Reads a PCM WAV file (8/16/24/32-bit integer or 32-bit float, any number of
    /// channels) into peak levels: the loudest sample (0..1) in each of a number of
    /// equal slices - what an audio clip's waveform shows.
    /// </summary>
    public static class WavePeaks
    {
        /// <summary>Peak level per slice, and the sound's length in seconds.</summary>
        public static (double[] Peaks, double Seconds) Read(Stream wav, int slices)
        {
            using var r = new BinaryReader(wav, Encoding.ASCII, leaveOpen: true);
            if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file.");
            r.ReadInt32();
            if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file.");

            int format = 0, channels = 0, rate = 0, bits = 0;
            while (wav.Position + 8 <= wav.Length)
            {
                var id = new string(r.ReadChars(4));
                var size = r.ReadInt32();
                var next = wav.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    format = r.ReadInt16();
                    channels = r.ReadInt16();
                    rate = r.ReadInt32();
                    r.ReadInt32(); r.ReadInt16();
                    bits = r.ReadInt16();
                    if (format == unchecked((short)0xFFFE) && size >= 26) // extensible: the real format follows
                    {
                        r.ReadInt16(); r.ReadInt16(); r.ReadInt32();
                        format = r.ReadInt16();
                    }
                }
                else if (id == "data")
                {
                    if (channels <= 0 || rate <= 0 || bits <= 0) throw new InvalidDataException("The WAV file has no format.");
                    var bytesPerSample = bits / 8;
                    var frameBytes = bytesPerSample * channels;
                    var frames = Math.Max(0, Math.Min(size, (int)(wav.Length - wav.Position)) / frameBytes);
                    var data = r.ReadBytes(frames * frameBytes);
                    var peaks = new double[Math.Max(1, slices)];
                    for (var f = 0; f < frames; f++)
                    {
                        var slice = (int)((long)f * peaks.Length / Math.Max(1, frames));
                        for (var c = 0; c < channels; c++)
                        {
                            var v = Math.Abs(Sample(data, (f * channels + c) * bytesPerSample, bits, format == 3));
                            if (v > peaks[slice]) peaks[slice] = v;
                        }
                    }
                    return (peaks, frames / (double)rate);
                }
                wav.Position = next;
            }
            throw new InvalidDataException("The WAV file has no sound data.");
        }

        private static double Sample(byte[] d, int i, int bits, bool isFloat) => bits switch
        {
            8 => (d[i] - 128) / 128.0,
            16 => BitConverter.ToInt16(d, i) / 32768.0,
            24 => ((d[i] | (d[i + 1] << 8) | ((sbyte)d[i + 2] << 16))) / 8388608.0,
            32 when isFloat => BitConverter.ToSingle(d, i),
            32 => BitConverter.ToInt32(d, i) / 2147483648.0,
            _ => 0,
        };

        /// <summary>A closed outline (in a 0..slices x 0..1 box) of the peaks mirrored
        /// around the middle, as path data for drawing.</summary>
        public static string Outline(double[] peaks)
        {
            var sb = new StringBuilder("M0,0.5 ");
            for (var i = 0; i < peaks.Length; i++)
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"L{i + 0.5:0.##},{0.5 - Math.Min(1, peaks[i]) * 0.48:0.###} ");
            for (var i = peaks.Length - 1; i >= 0; i--)
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"L{i + 0.5:0.##},{0.5 + Math.Min(1, peaks[i]) * 0.48:0.###} ");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"L{peaks.Length},0.5 Z");
            return sb.ToString();
        }
    }
}
