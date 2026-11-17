using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One saved path in the Paths panel.</summary>
    public sealed class PathItem
    {
        public string Name { get; init; } = string.Empty;
        public BitmapSource? Thumbnail { get; init; }
    }

    /// <summary>The Paths panel: named Pen paths kept with the document.</summary>
    public partial class DocumentPanelsViewModel
    {
        [ObservableProperty] private bool showPaths;

        partial void OnShowPathsChanged(bool value) { if (value) { _pathsKey = null; Refresh(); } }

        public ObservableCollection<PathItem> Paths { get; } = new();

        private string? _pathsKey;

        /// <summary>Rebuilds the list when the saved paths changed.</summary>
        private void UpdatePaths(DocumentViewModel doc)
        {
            if (!ShowPaths) return;
            var saved = doc.Layers.Scene.SavedPaths;
            var key = doc.GetHashCode() + "\n" + string.Join("\n", saved.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value));
            if (key == _pathsKey) return;
            _pathsKey = key;
            Paths.Clear();
            foreach (var (name, text) in saved.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                Paths.Add(new PathItem { Name = name, Thumbnail = PathThumb(text, doc.Layers.DocumentWidth, doc.Layers.DocumentHeight) });
        }

        private static BitmapSource? PathThumb(string text, int width, int height)
        {
            if (VectorPath.FromText(text) is not { } vector) return null;
            var scale = Math.Min(40f / Math.Max(1, width), 40f / Math.Max(1, height));
            using var picture = new SKBitmap(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(picture))
            using (var path = vector.ToSKPath())
            using (var line = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f / scale })
            {
                canvas.Clear(new SKColor(0x2E, 0x29, 0x29));
                canvas.Scale(scale);
                canvas.DrawPath(path, line);
            }
            return Timeline.TimelineViewModel.FastThumbnail(picture, 40);
        }

        /// <summary>Saves the Pen tool's working path as "Path 1", "Path 2", ...</summary>
        [RelayCommand]
        private void SaveCurrentPath()
        {
            if (_document() is not { } doc) return;
            var n = 1;
            while (doc.Layers.Scene.SavedPaths.ContainsKey($"Path {n}")) n++;
            if (!doc.Canvas.SavePenPath($"Path {n}"))
            {
                JolieCat.UI.MessageBox.Show("Draw a path with the Pen tool first (two points or more).", "Save Path",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            Refresh();
        }

        /// <summary>Click: the saved path becomes the Pen tool's working path.</summary>
        [RelayCommand]
        private void LoadPath(PathItem? item)
        {
            if (item is null || _document() is not { } doc) return;
            doc.Canvas.LoadPenPath(item.Name);
        }

        [RelayCommand]
        private void DeletePath(PathItem? item)
        {
            if (item is null || _document() is not { } doc) return;
            if (doc.Layers.Scene.SavedPaths.Remove(item.Name))
            {
                doc.Layers.History.MarkModified();
                Refresh();
            }
        }
    }
}
