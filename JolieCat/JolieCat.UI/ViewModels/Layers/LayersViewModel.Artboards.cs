using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>Artboards: named areas of the document, each exported as its own picture.</summary>
    public partial class LayersViewModel
    {
        public IReadOnlyList<Artboard> Artboards => Scene.Artboards;

        /// <summary>A name not used yet: "Artboard 1", "Artboard 2", ...</summary>
        public string NextArtboardName()
        {
            var n = 1;
            while (Scene.Artboards.Any(a => a.Name == $"Artboard {n}")) n++;
            return $"Artboard {n}";
        }

        private void ChangeArtboards(Action change, string stepName)
        {
            var before = Scene.Artboards.ToList();
            change();
            if (before.SequenceEqual(Scene.Artboards)) return;
            History.Push(new ArtboardsCommand(Scene, before, Scene.Artboards), stepName);
            NotifyContentChanged();
        }

        /// <summary>Adds an artboard (cut to the canvas; a used name gets a number). False if it
        /// has no area on the canvas.</summary>
        public bool AddArtboard(string name, SKRectI bounds)
        {
            bounds.Intersect(new SKRectI(0, 0, DocumentWidth, DocumentHeight));
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;
            name = string.IsNullOrWhiteSpace(name) ? NextArtboardName() : name.Trim();
            var unique = name;
            for (var n = 2; Scene.Artboards.Any(a => a.Name == unique); n++) unique = $"{name} {n}";
            ChangeArtboards(() => Scene.Artboards.Add(new Artboard(unique, bounds)), "New Artboard");
            return true;
        }

        public void RemoveArtboard(string name) =>
            ChangeArtboards(() => Scene.Artboards.RemoveAll(a => a.Name == name), "Delete Artboard");

        public void ClearArtboards() => ChangeArtboards(() => Scene.Artboards.Clear(), "Clear Artboards");

        /// <summary>The visible image inside each artboard, by name, in order.</summary>
        public List<(string Name, SKBitmap Picture)> RenderArtboards()
        {
            var result = new List<(string, SKBitmap)>();
            if (Scene.Artboards.Count == 0) return result;
            using var flat = Core.Rendering.SceneCompositor.Flatten(Scene, DocumentWidth, DocumentHeight);
            foreach (var a in Scene.Artboards)
                result.Add((a.Name, SpriteSheetTools.Cell(flat, a.Bounds)));
            return result;
        }
    }
}
