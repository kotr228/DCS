using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.UI.ViewModels.Layers;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Frame-by-frame animation: a "Frames" track whose clips each show one layer.
    /// New Frame / Duplicate Frame / Delete Frame edit it together with the layers,
    /// onion skin shows the neighboring frames faintly, and <see cref="RenderFrames"/>
    /// gives every frame's picture for export.
    /// </summary>
    public partial class TimelineViewModel
    {
        public const string FramesTrackName = "Frames";

        /// <summary>The document's layers (set by the document) - frames are layers.</summary>
        public LayersViewModel? FrameLayers { get; set; }

        /// <summary>Where playback loops: the end of the last clip, or Total Frames if there are none.</summary>
        public double PlaybackEnd
        {
            get
            {
                var clipsEnd = Tracks.Where(t => !t.IsMarkers).SelectMany(t => t.Clips).Select(c => c.StartFrame + c.LengthFrames).DefaultIfEmpty(0).Max();
                var keysEnd = Tracks.Where(t => t.IsAnimated).SelectMany(t => t.Keyframes).Select(k => k.Frame + 1).DefaultIfEmpty(0).Max();
                var end = Math.Max(clipsEnd, keysEnd);
                return end > 0 ? Math.Min(end, Math.Max(1, TotalFrames)) : Math.Max(1, TotalFrames);
            }
        }

        private TimelineTrackViewModel? FramesTrack => Tracks.FirstOrDefault(t => t.Name == FramesTrackName);

        /// <summary>The frame clips (those showing a layer), in time order.</summary>
        public IReadOnlyList<TimelineClipViewModel> FrameClips =>
            FramesTrack?.Clips.Where(c => c.TargetLayer is not null).OrderBy(c => c.StartFrame).ToList()
            ?? (IReadOnlyList<TimelineClipViewModel>)Array.Empty<TimelineClipViewModel>();

        /// <summary>The frame clip under the playhead, if any.</summary>
        public TimelineClipViewModel? CurrentFrameClip
        {
            get
            {
                var f = Math.Floor(CurrentFrame);
                return FrameClips.FirstOrDefault(c => f >= c.StartFrame && f < c.StartFrame + c.LengthFrames);
            }
        }

        /// <summary>Adds a blank frame after the current one.</summary>
        [RelayCommand]
        private void NewFrame() => AddFrame(duplicate: false);

        /// <summary>Adds a copy of the current frame after it.</summary>
        [RelayCommand]
        private void DuplicateFrame() => AddFrame(duplicate: true);

        private void AddFrame(bool duplicate)
        {
            if (FrameLayers is not { } layers) return;
            Pause();
            var before = Capture();

            var track = FramesTrack;
            if (track is null)
            {
                track = new TimelineTrackViewModel(this, FramesTrackName);
                Tracks.Insert(0, track);
            }

            // The first time: a layer already being drawn on becomes frame 1 - but not the
            // bottom (background) layer, which stays visible behind every frame.
            var firstTime = track.Clips.All(c => c.TargetLayer is null);
            if (firstTime && layers.ActiveLayer?.Model is { IsGroup: false, IsAdjustment: false } first
                && !ReferenceEquals(first, layers.Scene.Layers.FirstOrDefault()))
            {
                var firstClip = track.AddClip(first.Name, 0, 1);
                firstClip.TargetLayer = first;
            }

            var current = CurrentFrameClip ?? FrameClips.LastOrDefault();
            var insertAt = current is null ? 0 : current.StartFrame + current.LengthFrames;
            if (current is null && duplicate) duplicate = false; // nothing to copy yet
            foreach (var clip in Tracks.SelectMany(t => t.Clips).Where(c => c.StartFrame >= insertAt).ToList())
                clip.StartFrame += 1;

            var name = UniqueFrameName(layers.Scene);
            // Frames that are groups (several layers each) get a new group; plain ones a layer.
            var (layer, layerUndo) = current?.TargetLayer is { IsGroup: true } currentGroup
                ? layers.AddFrameGroup(name, duplicate ? currentGroup : null, layers.ActiveLayer?.Model.Name)
                : layers.AddFrameLayer(name, duplicate ? current?.TargetLayer : null);
            var added = track.AddClip(name, insertAt, 1);
            added.TargetLayer = layer;

            TotalFrames = Math.Max(TotalFrames, insertAt + 1);
            CurrentFrame = insertAt;
            UpdateFrameLayerVisibility();
            Record(before, duplicate ? "Duplicate Frame" : "New Frame", layerUndo);
            if (!layer.IsGroup) layers.ActivateFrameLayer(layer);
            else if (layers.Scene.ActiveLayer is { } inside) layers.ActivateFrameLayer(inside);
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Adds pictures as frames after the current one (each held for its number of
        /// frames), fitted into the canvas. One undo step.
        /// </summary>
        public void ImportFrames(IReadOnlyList<(SkiaSharp.SKBitmap Picture, int Frames)> pictures, string baseName)
        {
            if (FrameLayers is not { } layers || pictures.Count == 0) return;
            Pause();
            var before = Capture();
            var undos = new List<Core.History.IEditCommand>();

            var track = FramesTrack;
            if (track is null)
            {
                track = new TimelineTrackViewModel(this, FramesTrackName);
                Tracks.Insert(0, track);
            }
            var current = CurrentFrameClip ?? FrameClips.LastOrDefault();
            var insertAt = current is null ? 0 : current.StartFrame + current.LengthFrames;
            var total = pictures.Sum(p => Math.Max(1, p.Frames));
            foreach (var clip in Tracks.SelectMany(t => t.Clips).Where(c => c.StartFrame >= insertAt).ToList())
                clip.StartFrame += total;

            int w = layers.DocumentWidth, h = layers.DocumentHeight;
            Core.Documents.Layer? last = null;
            for (var i = 0; i < pictures.Count; i++)
            {
                var (picture, frames) = pictures[i];
                var name = UniqueFrameName(layers.Scene);
                // Fitted into the canvas, keeping its shape, centered.
                var scale = Math.Min(1f, Math.Min((float)w / picture.Width, (float)h / picture.Height));
                var dw = picture.Width * scale;
                var dh = picture.Height * scale;
                var (layer, undo) = layers.AddFrameLayer(name, null, target =>
                {
                    using var image = SkiaSharp.SKImage.FromBitmap(picture);
                    target.Canvas.DrawImage(image, SkiaSharp.SKRect.Create((w - dw) / 2, (h - dh) / 2, dw, dh),
                        new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear));
                });
                undos.Add(undo);
                var added = track.AddClip(name, insertAt, Math.Max(1, frames));
                added.TargetLayer = layer;
                insertAt += Math.Max(1, frames);
                last = layer;
            }

            TotalFrames = Math.Max(TotalFrames, PlaybackEnd);
            CurrentFrame = insertAt - Math.Max(1, pictures[^1].Frames);
            UpdateFrameLayerVisibility();
            Record(before, $"Import Frames ({pictures.Count})", new Core.History.CompositeEditCommand(undos));
            if (last is not null) layers.ActivateFrameLayer(last);
            layers.NotifyContentChanged();
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Animation &gt; Make Frames Multi-layer: every frame becomes a group, so
        /// a frame can have several layers (lines over colors...). One undo step.</summary>
        [RelayCommand]
        private void MakeFramesMultiLayer()
        {
            if (FrameLayers is not { } layers) return;
            var plain = FrameClips.Where(c => c.TargetLayer is { IsGroup: false }).ToList();
            if (plain.Count == 0) return;
            Pause();
            var before = Capture();
            var (groups, undo) = layers.WrapFramesInGroups(plain.Select(c => c.TargetLayer!).ToList());
            for (var i = 0; i < plain.Count; i++)
            {
                plain[i].Name = groups[i].Name;
                plain[i].TargetLayer = groups[i];
            }
            UpdateFrameLayerVisibility();
            Record(before, "Make Frames Multi-layer", undo);
            FrameChanged?.Invoke(this, EventArgs.Empty);
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The drawing layers of a frame: the layer itself, or everything inside its group.</summary>
        internal IEnumerable<Core.Documents.Layer> LayersOfFrame(Core.Documents.Layer frame)
        {
            if (!frame.IsGroup || FrameLayers is not { } layers) return new[] { frame };
            var (start, end) = layers.Scene.BlockOf(frame);
            return layers.Scene.Layers.Skip(start).Take(end - start).Where(l => !l.IsGroup && l.IsVisible).ToList();
        }

        /// <summary>Deletes the current frame (its clip and its layer); later frames move up.</summary>
        [RelayCommand]
        private void DeleteFrame()
        {
            if (FrameLayers is not { } layers || CurrentFrameClip is not { } clip || FrameClips.Count <= 1) return;
            Pause();
            var before = Capture();
            var start = clip.StartFrame;
            var length = clip.LengthFrames;
            FramesTrack!.Clips.Remove(clip);
            foreach (var later in Tracks.SelectMany(t => t.Clips).Where(c => c.StartFrame > start).ToList())
                later.StartFrame -= length;
            var layerUndo = clip.TargetLayer is { } layer ? layers.RemoveFrameLayer(layer) : null;

            CurrentFrame = Math.Max(0, Math.Min(start, PlaybackEnd - 1));
            UpdateFrameLayerVisibility();
            Record(before, "Delete Frame", layerUndo);
            if (CurrentFrameClip?.TargetLayer is { } now) layers.ActivateFrameLayer(now);
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string UniqueFrameName(Scene scene)
        {
            for (var n = 1; ; n++)
            {
                var name = $"Frame {n:000}";
                if (scene.Layers.All(l => l.Name != name)) return name;
            }
        }

        // ----- Onion skin -----

        [ObservableProperty]
        private bool onionSkin;

        // Boxed values for the onion skin frame-count picker (bound by value).
        public static readonly double OnionOne = 1, OnionTwo = 2, OnionThree = 3;

        /// <summary>How many frames before and after are shown (1..3).</summary>
        [ObservableProperty]
        private double onionSkinFrames = 1;

        /// <summary>How strongly the nearest neighbor shows (5..80 %).</summary>
        [ObservableProperty]
        private double onionSkinOpacity = 35;

        partial void OnOnionSkinChanged(bool value) => FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        partial void OnOnionSkinFramesChanged(double value) => FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        partial void OnOnionSkinOpacityChanged(double value) => FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>The neighboring frames to draw faintly over the canvas: their layer,
        /// how opaque (fading with distance), and whether they come before the current one.</summary>
        public IReadOnlyList<(Layer Layer, float Opacity, bool Before)> OnionSkinLayers()
        {
            var result = new List<(Layer, float, bool)>();
            if (!OnionSkin || IsPlaying) return result;
            var frames = FrameClips.ToList();
            var current = CurrentFrameClip;
            var index = current is null ? -1 : frames.IndexOf(current);
            if (index < 0) return result;
            var count = (int)Math.Clamp(Math.Round(OnionSkinFrames), 1, 3);
            var strength = (float)Math.Clamp(OnionSkinOpacity, 5, 80) / 100f;
            for (var d = 1; d <= count; d++)
            {
                var opacity = strength / d;
                if (index - d >= 0 && frames[index - d].TargetLayer is { } before)
                    foreach (var l in LayersOfFrame(before)) result.Add((l, opacity, true));
                if (index + d < frames.Count && frames[index + d].TargetLayer is { } after)
                    foreach (var l in LayersOfFrame(after)) result.Add((l, opacity, false));
            }
            return result;
        }

        // ----- Export -----

        /// <summary>
        /// Every distinct picture of the animation from frame 0 to <see cref="PlaybackEnd"/>,
        /// with how many frames each lasts (unchanged frames are merged), drawn by
        /// <paramref name="render"/> at each frame. The playhead is put back afterwards.
        /// </summary>
        public List<(T Picture, int Frames)> RenderFrames<T>(Func<T> render)
        {
            Pause();
            var saved = CurrentFrame;
            var result = new List<(T, int)>();
            string? lastKey = null;
            _exporting = true;
            try
            {
                // The work area (In / Out), or the whole animation.
                var end = (int)Math.Ceiling(PlayEnd);
                for (var f = (int)PlayStart; f < end; f++)
                {
                    CurrentFrame = f;
                    // The same clips showing as on the frame before: the same picture, just longer.
                    var key = string.Join(",", Tracks.SelectMany(t => t.Clips)
                        .Where(c => c.TargetLayer is not null && f >= c.StartFrame && f < c.StartFrame + c.LengthFrames)
                        .Select(c => c.GetHashCode()))
                        + "|" + string.Join(",", Tracks.Where(t => t.IsAnimated && t.Keyframes.Count > 0)
                            .Select(t => (PoseAt(t, f) with { Frame = 0 }).GetHashCode()));
                    if (key == lastKey && result.Count > 0)
                    {
                        result[^1] = (result[^1].Item1, result[^1].Item2 + 1);
                        continue;
                    }
                    lastKey = key;
                    result.Add((render(), 1));
                }
            }
            finally
            {
                _exporting = false;
                CurrentFrame = saved;
                ApplyAnimation();
            }
            return result;
        }
    }
}
