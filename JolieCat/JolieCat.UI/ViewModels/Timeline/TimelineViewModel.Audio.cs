using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Sound: an audio track plays a WAV/MP3/M4A file from where its clip starts. It
    /// plays along with the timeline and goes into MP4 exports. The file is linked,
    /// not copied into the project.
    /// </summary>
    public partial class TimelineViewModel
    {
        private readonly Dictionary<TimelineTrackViewModel, MediaPlayer> _players = new();

        public IEnumerable<TimelineTrackViewModel> AudioTracks => Tracks.Where(t => t.IsAudio);

        [RelayCommand]
        private async Task AddAudio()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add Audio",
                Filter = "Sound files|*.wav;*.mp3;*.m4a;*.wma;*.aac|All files|*.*",
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                await AddAudioFileAsync(dialog.FileName);
            }
            catch (Exception ex)
            {
                JolieCat.UI.MessageBox.Show($"Couldn't add that sound:\n{ex.Message}", "Add Audio",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>Adds an audio track playing <paramref name="path"/> from the playhead,
        /// its clip as long as the sound. One undo step.</summary>
        public async Task<TimelineTrackViewModel> AddAudioFileAsync(string path)
        {
            var seconds = await AudioDurationAsync(path);
            var before = Capture();
            var start = Math.Floor(CurrentFrame);
            var length = Math.Max(1, Math.Ceiling(seconds * Math.Max(1, FrameRate)));
            var name = Path.GetFileNameWithoutExtension(path);
            var track = new TimelineTrackViewModel(this, $"Audio: {name}") { AudioPath = Path.GetFullPath(path) };
            track.AddClip(name, start, length);
            Tracks.Add(track);
            TotalFrames = Math.Max(TotalFrames, start + length);
            Record(before, "Add Audio");
            await LoadWaveformAsync(track);
            return track;
        }

        // ----- Waveforms -----

        /// <summary>Peaks per second-sliced file, kept for the session (undo rebuilds clips).</summary>
        private static readonly Dictionary<string, (double[] Peaks, double Seconds)> WaveCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gives an audio track's clip its waveform (decoding the sound once).</summary>
        internal async Task LoadWaveformAsync(TimelineTrackViewModel track)
        {
            if (track.AudioPath is not { } path || track.Clips.Count == 0 || !File.Exists(path)) return;
            if (!WaveCache.TryGetValue(path, out var wave))
            {
                wave = await DecodePeaksAsync(path);
                WaveCache[path] = wave;
            }
            // One slice per frame of the clip.
            var clip = track.Clips[0];
            var slices = (int)Math.Max(1, Math.Round(wave.Seconds * Math.Max(1, FrameRate)));
            var peaks = new double[slices];
            for (var i = 0; i < slices; i++)
            {
                var from = (int)((long)i * wave.Peaks.Length / slices);
                var to = (int)((long)(i + 1) * wave.Peaks.Length / slices);
                for (var k = from; k < Math.Max(from + 1, to) && k < wave.Peaks.Length; k++) peaks[i] = Math.Max(peaks[i], wave.Peaks[k]);
            }
            clip.Waveform = Core.Animation.WavePeaks.Outline(peaks);
        }

        /// <summary>Gives every audio clip its waveform (after loading or undo); errors only lose the picture.</summary>
        internal async void RefreshWaveforms()
        {
            foreach (var track in AudioTracks.ToList())
            {
                try { await LoadWaveformAsync(track); }
                catch { /* the waveform is only a picture */ }
            }
        }

        /// <summary>Any sound file to peak levels, 100 slices a second: Windows converts it
        /// to plain WAV first, which is then read.</summary>
        private static async Task<(double[] Peaks, double Seconds)> DecodePeaksAsync(string path)
        {
            var temp = Path.Combine(Path.GetTempPath(), "JolieCatWave_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var source = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
                var target = await folder.CreateFileAsync(Path.GetFileName(temp), Windows.Storage.CreationCollisionOption.ReplaceExisting);
                var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.Low);
                var transcoder = new Windows.Media.Transcoding.MediaTranscoder();
                var prepared = await transcoder.PrepareFileTranscodeAsync(source, target, profile);
                if (!prepared.CanTranscode) throw new InvalidOperationException($"Can't read the sound ({prepared.FailureReason}).");
                await prepared.TranscodeAsync();

                await using var stream = File.OpenRead(temp);
                var seconds = (await AudioDurationAsync(path));
                var slices = (int)Math.Max(1, Math.Ceiling(seconds * 100));
                var (peaks, _) = Core.Animation.WavePeaks.Read(stream, slices);
                return (peaks, seconds);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* temporary file */ }
            }
        }

        // ----- Volume and mute (Audio panel) -----

        public bool IsAudioSelected => SelectedTrack?.IsAudio == true;

        public double SelectedVolume
        {
            get => SelectedTrack?.Volume ?? 100;
            set
            {
                if (SelectedTrack is not { IsAudio: true } track || Math.Abs(track.Volume - value) < 0.01) return;
                var before = Capture();
                track.Volume = Math.Clamp(value, 0, 100);
                if (_players.TryGetValue(track, out var player)) player.Volume = track.Volume / 100;
                Record(before, "Change Volume", mergeKey: ("volume", track.AudioPath));
                OnPropertyChanged();
            }
        }

        public bool SelectedMuted
        {
            get => SelectedTrack?.IsMuted ?? false;
            set
            {
                if (SelectedTrack is not { IsAudio: true } track || track.IsMuted == value) return;
                var before = Capture();
                track.IsMuted = value;
                Record(before, value ? "Mute" : "Unmute");
                if (value && _players.TryGetValue(track, out var player)) player.Pause();
                OnPropertyChanged();
            }
        }

        /// <summary>How long a sound file plays, in seconds (read by Windows' media stack).</summary>
        internal static async Task<double> AudioDurationAsync(string path)
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            var track = await Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(file);
            return track.OriginalDuration.TotalSeconds;
        }

        /// <summary>The sounds to put in an exported video: file, when it starts (seconds),
        /// and volume (0..1). Muted tracks are left out.</summary>
        public IReadOnlyList<(string Path, double StartSeconds, double Volume)> AudioForExport()
        {
            var fps = Math.Max(1, FrameRate);
            return AudioTracks
                .Where(t => !t.IsMuted && File.Exists(t.AudioPath) && t.Clips.Count > 0)
                .Select(t => (t.AudioPath!, t.Clips[0].StartFrame / fps, Math.Clamp(t.Volume / 100, 0, 1)))
                .ToList();
        }

        partial void OnIsPlayingChanged(bool value)
        {
            ApplyAnimation(); // the camera shows while playing
            if (value) SyncAudio(restart: true);
            else StopAudio();
        }

        /// <summary>Keeps the sounds in step with the playhead while playing: each starts
        /// when the playhead reaches its clip (or jumps into it at the right point).</summary>
        private void SyncAudio(bool restart)
        {
            if (!IsPlaying) return;
            var fps = Math.Max(1, FrameRate);
            foreach (var track in AudioTracks)
            {
                if (track.IsMuted || track.Clips.Count == 0 || !File.Exists(track.AudioPath)) continue;
                var clip = track.Clips[0];
                var inside = CurrentFrame >= clip.StartFrame && CurrentFrame < clip.StartFrame + clip.LengthFrames;
                var player = PlayerFor(track);
                if (!inside)
                {
                    player.Pause();
                    continue;
                }
                if (restart || CurrentFrame == clip.StartFrame || !_playing.Contains(track))
                {
                    player.Volume = Math.Clamp(track.Volume / 100, 0, 1);
                    player.Position = TimeSpan.FromSeconds((CurrentFrame - clip.StartFrame) / fps);
                    player.Play();
                    _playing.Add(track);
                }
            }
        }

        private readonly HashSet<TimelineTrackViewModel> _playing = new();

        private MediaPlayer PlayerFor(TimelineTrackViewModel track)
        {
            if (!_players.TryGetValue(track, out var player))
            {
                player = new MediaPlayer();
                player.Open(new Uri(track.AudioPath!));
                _players[track] = player;
            }
            return player;
        }

        private void StopAudio()
        {
            foreach (var player in _players.Values) player.Pause();
            _playing.Clear();
        }

        private void CloseAudio()
        {
            foreach (var player in _players.Values) player.Close();
            _players.Clear();
            _playing.Clear();
        }
    }
}
