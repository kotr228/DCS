using System.IO;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels;

namespace JolieCat.UI.Tests
{
    public class AudioTrackTests
    {
        /// <summary>A one-second 440 Hz tone as a 16-bit mono WAV file.</summary>
        private static string WriteTone(double seconds = 1)
        {
            var path = Path.Combine(Path.GetTempPath(), "jc_tone_" + Guid.NewGuid().ToString("N") + ".wav");
            const int rate = 44100;
            var samples = (int)(rate * seconds);
            using var w = new BinaryWriter(File.Create(path));
            w.Write("RIFF"u8.ToArray()); w.Write(36 + samples * 2); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(samples * 2);
            for (var i = 0; i < samples; i++) w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
            return path;
        }

        private static void Run(Action test)
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

        [Fact]
        public void AddAudio_MakesAClipAsLongAsTheSound_AndCanBeUndone() => Run(() =>
        {
            var tone = WriteTone();
            try
            {
                var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 200, 150, null);
                doc.Timeline.CurrentFrame = 6;
                var track = Task.Run(() => doc.Timeline.AddAudioFileAsync(tone)).GetAwaiter().GetResult();
                Assert.True(track.IsAudio);
                var clip = Assert.Single(track.Clips);
                Assert.True(clip.IsAudio);
                Assert.Equal(6, clip.StartFrame);
                Assert.Equal(24, clip.LengthFrames);                    // 1 s at 24 fps
                var export = Assert.Single(doc.Timeline.AudioForExport());
                Assert.Equal(0.25, export.StartSeconds, 3);

                Assert.False(string.IsNullOrEmpty(clip.Waveform));          // the waveform is drawn

                // Volume: a slider drag is one undo step.
                doc.Timeline.SelectTrackCommand.Execute(track);
                Assert.True(doc.Timeline.IsAudioSelected);
                var steps = doc.Layers.History.UndoNames.Count;
                for (var v = 99; v >= 40; v--) doc.Timeline.SelectedVolume = v;
                Assert.Equal(steps + 1, doc.Layers.History.UndoNames.Count);
                Assert.Equal(0.4, doc.Timeline.AudioForExport()[0].Volume, 3);
                doc.Timeline.SelectedMuted = true;
                Assert.Empty(doc.Timeline.AudioForExport());
                doc.Timeline.SelectedMuted = false;
                doc.Layers.History.Undo();                                    // unmute
                Assert.True(doc.Timeline.AudioTracks.Single().IsMuted);
                doc.Layers.History.Undo();                                    // mute
                doc.Layers.History.Undo();                                    // the volume change
                Assert.Equal(100, doc.Timeline.AudioTracks.Single().Volume);

                doc.Layers.History.Undo();
                Assert.Empty(doc.Timeline.AudioTracks);
                doc.Layers.History.Redo();
                Assert.Single(doc.Timeline.AudioTracks);
            }
            finally
            {
                File.Delete(tone);
            }
        });

        [Fact]
        public void Mp4Export_CarriesTheSound() => Run(() =>
        {
            var tone = WriteTone(2);
            var video = Path.Combine(Path.GetTempPath(), "jc_av_" + Guid.NewGuid().ToString("N") + ".mp4");
            try
            {
                var doc = new DocumentViewModel(new ToolboxViewModel(), "A", ProjectType.ClipbarAnimation, 200, 150, null);
                doc.Timeline.NewFrameCommand.Execute(null);
                doc.Timeline.NewFrameCommand.Execute(null);
                doc.Timeline.FrameClips[1].LengthFrames = 23;           // a 1-second animation
                doc.Timeline.CurrentFrame = 0;
                Task.Run(() => doc.Timeline.AddAudioFileAsync(tone)).GetAwaiter().GetResult();
                doc.Timeline.FrameClips[1].LengthFrames = 23;
                Task.Run(() => MainViewModel.ExportAnimationToAsync(doc, "MP4 Video", 1, video, "A")).GetAwaiter().GetResult();

                var bytes = File.ReadAllBytes(video);
                var text = System.Text.Encoding.ASCII.GetString(bytes);
                Assert.Contains("mp4a", text);                         // an AAC sound track is in the file
                Assert.Contains("avc1", text);                         // and the H.264 picture
            }
            finally
            {
                File.Delete(tone);
                File.Delete(video);
            }
        });
    }
}
