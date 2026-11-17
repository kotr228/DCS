using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Sprite sheet helpers: finding the grid of a sheet from the empty space between
    /// its sprites, and packing separate pictures into a new sheet.
    /// </summary>
    public static class SpriteSheetTools
    {
        /// <summary>
        /// Finds how a sheet is divided: the runs of columns and rows that hold something
        /// (not transparent, and not the background color of the top-left pixel when the
        /// sheet is opaque) are the cells; the gaps between them are the padding, the gap
        /// before the first one the margin. Null if no regular grid shows.
        /// </summary>
        public static SpriteSheetGrid? DetectGrid(SKBitmap sheet, int tolerance = 12)
        {
            int w = sheet.Width, h = sheet.Height;
            var px = sheet.Pixels;
            var background = px[0];
            // A sheet with see-through space has its gaps there; only a fully opaque
            // sheet is read against its corner color.
            var opaqueBackground = !Array.Exists(px, c => c.Alpha < 16);

            bool Filled(SKColor c)
            {
                if (c.Alpha < 16) return false;
                if (!opaqueBackground) return true;
                return Math.Abs(c.Red - background.Red) + Math.Abs(c.Green - background.Green) + Math.Abs(c.Blue - background.Blue) > tolerance;
            }

            var columnFilled = new bool[w];
            var rowFilled = new bool[h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (Filled(px[y * w + x])) { columnFilled[x] = true; rowFilled[y] = true; }

            var columns = Runs(columnFilled);
            var rows = Runs(rowFilled);
            if (columns.Count == 0 || rows.Count == 0) return null;

            // Cells are as wide as the widest sprite; spacing from the starts of the runs.
            var (cols, cellW, marginX, padX) = Fit(columns, w);
            var (rws, cellH, marginY, padY) = Fit(rows, h);
            return new SpriteSheetGrid { Columns = cols, Rows = rws, MarginX = marginX, MarginY = marginY, PaddingX = padX, PaddingY = padY };
        }

        /// <summary>Where the filled stretches along one axis start and end.</summary>
        private static List<(int Start, int End)> Runs(bool[] filled)
        {
            var runs = new List<(int, int)>();
            var start = -1;
            for (var i = 0; i <= filled.Length; i++)
            {
                var on = i < filled.Length && filled[i];
                if (on && start < 0) start = i;
                else if (!on && start >= 0) { runs.Add((start, i - 1)); start = -1; }
            }
            return runs;
        }

        /// <summary>An even grid along one axis that holds every run: the cell pitch is the
        /// typical distance between run starts; sprites smaller than their cell sit inside it.</summary>
        private static (int Count, int Cell, int Margin, int Padding) Fit(List<(int Start, int End)> runs, int length)
        {
            if (runs.Count == 1) return (1, length, 0, 0);
            var pitches = runs.Zip(runs.Skip(1), (a, b) => b.Start - a.Start).OrderBy(p => p).ToList();
            var pitch = pitches[pitches.Count / 2];
            var count = (int)Math.Round((length - runs[0].Start + Math.Max(0, pitch - (runs[^1].End - runs[^1].Start + 1))) / (double)pitch);
            count = Math.Clamp(count, runs.Count, 64);
            var widest = runs.Max(r => r.End - r.Start + 1);
            var margin = Math.Max(0, runs[0].Start - Math.Max(0, (pitch - widest) / 2));
            // count cells + (count - 1) paddings fill the length minus both margins,
            // and cell + padding is the pitch: padding = count * pitch - (length - 2 * margin).
            var padding = count * pitch - (length - 2 * margin);
            if (padding < 0)
            {
                margin = Math.Max(0, (length - count * pitch) / 2);
                padding = Math.Max(0, count * pitch - (length - 2 * margin));
            }
            padding = Math.Min(padding, pitch - 1);
            return (count, pitch - padding, margin, padding);
        }

        /// <summary>
        /// Packs pictures into one sheet: equal cells (as big as the largest picture),
        /// in a near-square grid with <paramref name="padding"/> pixels between cells,
        /// each picture centered in its cell. Returns the sheet and its grid.
        /// </summary>
        public static (SKBitmap Sheet, SpriteSheetGrid Grid) Pack(IReadOnlyList<SKBitmap> pictures, int padding = 2, int? columns = null)
        {
            if (pictures.Count == 0) throw new ArgumentException("No pictures.", nameof(pictures));
            var cellW = pictures.Max(p => p.Width);
            var cellH = pictures.Max(p => p.Height);
            var cols = Math.Clamp(columns ?? (int)Math.Ceiling(Math.Sqrt(pictures.Count)), 1, pictures.Count);
            var rows = (int)Math.Ceiling(pictures.Count / (double)cols);
            padding = Math.Max(0, padding);
            var sheet = new SKBitmap(cols * cellW + (cols - 1) * padding, rows * cellH + (rows - 1) * padding, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(sheet))
            {
                canvas.Clear(SKColors.Transparent);
                for (var i = 0; i < pictures.Count; i++)
                {
                    var p = pictures[i];
                    var x = i % cols * (cellW + padding) + (cellW - p.Width) / 2;
                    var y = i / cols * (cellH + padding) + (cellH - p.Height) / 2;
                    canvas.DrawBitmap(p, x, y);
                }
            }
            return (sheet, new SpriteSheetGrid { Columns = cols, Rows = rows, PaddingX = padding, PaddingY = padding });
        }

        /// <summary>The frame-list layouts the exports can write.</summary>
        public enum DataFormat
        {
            /// <summary>TexturePacker JSON "hash" (frames by name): Phaser, PixiJS, Godot / Unity importers.</summary>
            JsonHash,
            /// <summary>TexturePacker JSON "array" (frames in order, each with a filename): Phaser, Construct.</summary>
            JsonArray,
            /// <summary>CSS sprite classes for web pages (one class per frame).</summary>
            Css,
        }

        /// <summary>One frame of an export: where it is in the exported picture, the part of
        /// its cell that holds pixels, its cell's size and place, and its pivot (0..1).</summary>
        private sealed record ExportFrame(string Name, int Row, int Column, SKRectI InImage, SKRectI Used, SKRectI Cell, bool Trimmed, double PivotX, double PivotY);

        /// <summary>
        /// The sheet's frame list in <paramref name="format"/>: each non-empty cell with its
        /// place on the sheet, the part that holds pixels, its pivot (the sheet's, or the
        /// cell's own) and the animations from the row names.
        /// </summary>
        public static string ToEngineJson(SKBitmap sheet, SpriteSheetGrid grid, string imageName, string framePrefix, DataFormat format = DataFormat.JsonHash)
        {
            var frames = new List<ExportFrame>();
            var index = 0;
            for (var row = 0; row < Math.Max(1, grid.Rows); row++)
                for (var column = 0; column < Math.Max(1, grid.Columns); column++)
                {
                    var r = SKRectI.Round(grid.GetCellRect(column, row, sheet.Width, sheet.Height));
                    if (Bounds(sheet, r) is not { } used) continue;
                    var (px, py) = grid.PivotFor(row * Math.Max(1, grid.Columns) + column);
                    frames.Add(new ExportFrame($"{framePrefix}_{index++:D3}", row, column, r, used, r,
                        used.Width != r.Width || used.Height != r.Height, px / 100, py / 100));
                }
            var gridJson = $"\"grid\": {{ \"columns\": {grid.Columns}, \"rows\": {grid.Rows}, \"paddingX\": {grid.PaddingX}, \"paddingY\": {grid.PaddingY}, \"marginX\": {grid.MarginX}, \"marginY\": {grid.MarginY} }}";
            return Write(frames, grid, imageName, sheet.Width, sheet.Height, format, gridJson, untrimmedInImage: true);
        }

        /// <summary>One sprite in a packed atlas: where it came from on the sheet (its whole
        /// cell, and the part holding pixels) and where that part went in the atlas.</summary>
        public sealed record AtlasFrame(int Column, int Row, SKRectI Cell, SKRectI Used, SKPointI At);

        /// <summary>
        /// A tight atlas: every non-empty cell trimmed to its pixels and placed in rows
        /// ("shelves", tallest first) on a sheet about as wide as it is tall, with
        /// <paramref name="padding"/> pixels between them. <paramref name="extrude"/> repeats
        /// each sprite's edge pixels that many times around it (so a game's texture filtering
        /// never picks up a neighbour); <paramref name="powerOfTwo"/> makes the atlas's sides
        /// powers of two (for older engines and GPUs).
        /// </summary>
        public static (SKBitmap Atlas, List<AtlasFrame> Frames) PackAtlas(SKBitmap sheet, SpriteSheetGrid grid, int padding = 1, int extrude = 0, bool powerOfTwo = false)
        {
            padding = Math.Max(0, padding);
            extrude = Math.Clamp(extrude, 0, 8);
            var sprites = new List<(int Column, int Row, SKRectI Cell, SKRectI Used)>();
            for (var row = 0; row < Math.Max(1, grid.Rows); row++)
                for (var column = 0; column < Math.Max(1, grid.Columns); column++)
                {
                    var cell = SKRectI.Round(grid.GetCellRect(column, row, sheet.Width, sheet.Height));
                    if (Bounds(sheet, cell) is { } used) sprites.Add((column, row, cell, used));
                }
            if (sprites.Count == 0) throw new InvalidOperationException("The sheet has no sprites.");

            // Each sprite takes a slot: itself plus its extruded edge on every side.
            int SlotW(int i) => sprites[i].Used.Width + 2 * extrude;
            int SlotH(int i) => sprites[i].Used.Height + 2 * extrude;
            var area = Enumerable.Range(0, sprites.Count).Sum(i => (long)(SlotW(i) + padding) * (SlotH(i) + padding));
            var width = Math.Max(Enumerable.Range(0, sprites.Count).Max(SlotW) + padding, (int)Math.Ceiling(Math.Sqrt(area) * 1.15));
            var order = Enumerable.Range(0, sprites.Count).OrderByDescending(SlotH).ThenByDescending(SlotW).ToList();

            var slots = new SKPointI[sprites.Count];
            int x = 0, y = 0, shelf = 0, usedWidth = 0;
            foreach (var i in order)
            {
                if (x > 0 && x + SlotW(i) > width) { x = 0; y += shelf + padding; shelf = 0; }
                slots[i] = new SKPointI(x, y);
                x += SlotW(i) + padding;
                shelf = Math.Max(shelf, SlotH(i));
                usedWidth = Math.Max(usedWidth, x - padding);
            }

            int atlasW = Math.Max(1, usedWidth), atlasH = Math.Max(1, y + shelf);
            if (powerOfTwo) { atlasW = NextPowerOfTwo(atlasW); atlasH = NextPowerOfTwo(atlasH); }
            var atlas = new SKBitmap(atlasW, atlasH, SKColorType.Rgba8888, SKAlphaType.Premul);
            var frames = new List<AtlasFrame>();
            using (var canvas = new SKCanvas(atlas))
            {
                canvas.Clear(SKColors.Transparent);
                for (var i = 0; i < sprites.Count; i++)
                {
                    var s = sprites[i];
                    var at = new SKPointI(slots[i].X + extrude, slots[i].Y + extrude);
                    var source = SKRect.Create(s.Used.Left, s.Used.Top, s.Used.Width, s.Used.Height);
                    if (extrude > 0)
                    {
                        // The edge rows and columns stretched outward, then the corners.
                        using var image = SKImage.FromBitmap(sheet);
                        var u = s.Used;
                        void Strip(SKRectI from, SKRect to) => canvas.DrawImage(image, SKRect.Create(from.Left, from.Top, from.Width, from.Height), to);
                        Strip(SKRectI.Create(u.Left, u.Top, u.Width, 1), SKRect.Create(at.X, at.Y - extrude, u.Width, extrude));
                        Strip(SKRectI.Create(u.Left, u.Bottom - 1, u.Width, 1), SKRect.Create(at.X, at.Y + u.Height, u.Width, extrude));
                        Strip(SKRectI.Create(u.Left, u.Top, 1, u.Height), SKRect.Create(at.X - extrude, at.Y, extrude, u.Height));
                        Strip(SKRectI.Create(u.Right - 1, u.Top, 1, u.Height), SKRect.Create(at.X + u.Width, at.Y, extrude, u.Height));
                        Strip(SKRectI.Create(u.Left, u.Top, 1, 1), SKRect.Create(at.X - extrude, at.Y - extrude, extrude, extrude));
                        Strip(SKRectI.Create(u.Right - 1, u.Top, 1, 1), SKRect.Create(at.X + u.Width, at.Y - extrude, extrude, extrude));
                        Strip(SKRectI.Create(u.Left, u.Bottom - 1, 1, 1), SKRect.Create(at.X - extrude, at.Y + u.Height, extrude, extrude));
                        Strip(SKRectI.Create(u.Right - 1, u.Bottom - 1, 1, 1), SKRect.Create(at.X + u.Width, at.Y + u.Height, extrude, extrude));
                    }
                    canvas.DrawBitmap(sheet, source, SKRect.Create(at.X, at.Y, s.Used.Width, s.Used.Height));
                    frames.Add(new AtlasFrame(s.Column, s.Row, s.Cell, s.Used, at));
                }
            }
            return (atlas, frames);
        }

        private static int NextPowerOfTwo(int v)
        {
            var p = 1;
            while (p < v) p <<= 1;
            return p;
        }

        /// <summary>A packed atlas's frame list in <paramref name="format"/>: <c>frame</c> is the
        /// trimmed sprite in the atlas, <c>spriteSourceSize</c> where it sits in its original
        /// cell, so engines put it back in the same spot.</summary>
        public static string ToAtlasJson(SKBitmap atlas, IReadOnlyList<AtlasFrame> frames, SpriteSheetGrid grid, string imageName, string framePrefix, DataFormat format = DataFormat.JsonHash)
        {
            var list = new List<ExportFrame>();
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                var (px, py) = grid.PivotFor(f.Row * Math.Max(1, grid.Columns) + f.Column);
                list.Add(new ExportFrame($"{framePrefix}_{i:D3}", f.Row, f.Column, SKRectI.Create(f.At.X, f.At.Y, f.Used.Width, f.Used.Height),
                    f.Used, f.Cell, f.Used.Width != f.Cell.Width || f.Used.Height != f.Cell.Height, px / 100, py / 100));
            }
            return Write(list, grid, imageName, atlas.Width, atlas.Height, format, null, untrimmedInImage: false);
        }

        /// <summary>Writes the frames in the chosen layout. <paramref name="untrimmedInImage"/>:
        /// the picture holds whole cells (grid sheet), so <c>frame</c> is the cell.</summary>
        private static string Write(List<ExportFrame> frames, SpriteSheetGrid grid, string imageName, int width, int height,
            DataFormat format, string? extraMeta, bool untrimmedInImage)
        {
            if (format == DataFormat.Css)
            {
                var css = new System.Text.StringBuilder();
                css.Append($"/* {imageName}: {frames.Count} sprites, made by JolieCat. Use class=\"sprite NAME\". */\n");
                css.Append($".sprite {{ display: inline-block; background-image: url(\"{imageName}\"); background-repeat: no-repeat; }}\n");
                foreach (var f in frames)
                {
                    // The frame as stored: a whole cell on a grid sheet, the trimmed sprite in an atlas.
                    var r = f.InImage;
                    css.Append($".{CssName(f.Name)} {{ width: {r.Width}px; height: {r.Height}px; background-position: {-r.Left}px {-r.Top}px; }}\n");
                }
                return css.ToString();
            }

            var animations = frames.Select(f => (f.Row, f.Name)).ToList();
            string Entry(ExportFrame f, bool withName)
            {
                var frame = f.InImage;
                return (withName ? $"\"filename\": \"{Escape(f.Name)}\", " : string.Empty) +
                    $"\"frame\": {{ \"x\": {frame.Left}, \"y\": {frame.Top}, \"w\": {frame.Width}, \"h\": {frame.Height} }}, " +
                    $"\"rotated\": false, \"trimmed\": {(f.Trimmed ? "true" : "false")}, " +
                    $"\"spriteSourceSize\": {{ \"x\": {f.Used.Left - f.Cell.Left}, \"y\": {f.Used.Top - f.Cell.Top}, \"w\": {f.Used.Width}, \"h\": {f.Used.Height} }}, " +
                    $"\"sourceSize\": {{ \"w\": {f.Cell.Width}, \"h\": {f.Cell.Height} }}, \"pivot\": {{ \"x\": {Num(f.PivotX)}, \"y\": {Num(f.PivotY)} }}, " +
                    $"\"cell\": {{ \"column\": {f.Column}, \"row\": {f.Row} }}";
            }
            var body = format == DataFormat.JsonArray
                ? "  \"frames\": [\n" + string.Join(",\n", frames.Select(f => "    { " + Entry(f, true) + " }")) + "\n  ],\n"
                : "  \"frames\": {\n" + string.Join(",\n", frames.Select(f => $"    \"{Escape(f.Name)}\": {{ " + Entry(f, false) + " }")) + "\n  },\n";
            return "{\n" + body + AnimationsJson(grid, animations) +
                   $"  \"meta\": {{ \"app\": \"JolieCat\", \"version\": \"1.0\", \"image\": \"{Escape(imageName)}\", \"format\": \"RGBA8888\", " +
                   $"\"size\": {{ \"w\": {width}, \"h\": {height} }}, \"scale\": \"1\"" + (extraMeta is null ? string.Empty : ", " + extraMeta) + " }\n}\n";
        }

        /// <summary>A frame name as a CSS class name (letters, digits, - and _).</summary>
        private static string CssName(string name)
        {
            var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
            var s = new string(chars);
            return s.Length > 0 && char.IsDigit(s[0]) ? "s-" + s : s;
        }

        /// <summary>The "animations" block (PixiJS / Phaser): each named row's frames in order.
        /// Empty when no row has a name; rows sharing a name are joined.</summary>
        private static string AnimationsJson(SpriteSheetGrid grid, List<(int Row, string Frame)> frames)
        {
            var named = new List<(string Name, List<string> Frames)>();
            foreach (var (row, frame) in frames)
            {
                if (grid.RowName(row) is not { } name) continue;
                var entry = named.Find(n => n.Name == name);
                if (entry.Frames is null) named.Add(entry = (name, new List<string>()));
                entry.Frames.Add(frame);
            }
            if (named.Count == 0) return string.Empty;
            var lines = named.Select(n => $"    \"{Escape(n.Name)}\": [" + string.Join(", ", n.Frames.Select(f => $"\"{Escape(f)}\"")) + "]");
            return "  \"animations\": {\n" + string.Join(",\n", lines) + "\n  },\n";
        }

        private static string Num(double v) => Math.Round(v, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>The smallest rectangle inside <paramref name="area"/> holding visible pixels; null if none.</summary>
        public static SKRectI? Bounds(SKBitmap sheet, SKRectI area)
        {
            area.Intersect(new SKRectI(0, 0, sheet.Width, sheet.Height));
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            var px = sheet.Pixels;
            for (var y = area.Top; y < area.Bottom; y++)
                for (var x = area.Left; x < area.Right; x++)
                    if (px[y * sheet.Width + x].Alpha > 0)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
            return right < 0 ? null : new SKRectI(left, top, right + 1, bottom + 1);
        }

        /// <summary>Packs rows of pictures (one move per row) into one sheet: equal cells as
        /// big as the largest picture, as many columns as the longest row, each named row's
        /// name kept in <see cref="SpriteSheetGrid.RowNames"/>.</summary>
        public static (SKBitmap Sheet, SpriteSheetGrid Grid) PackRows(IReadOnlyList<(string? Name, List<SKBitmap> Sprites)> rows, int padding = 2)
        {
            var filled = rows.Where(r => r.Sprites.Count > 0).ToList();
            if (filled.Count == 0) throw new ArgumentException("No pictures.", nameof(rows));
            var all = filled.SelectMany(r => r.Sprites).ToList();
            int cellW = all.Max(p => p.Width), cellH = all.Max(p => p.Height);
            var cols = filled.Max(r => r.Sprites.Count);
            padding = Math.Max(0, padding);
            var sheet = new SKBitmap(cols * cellW + (cols - 1) * padding, filled.Count * cellH + (filled.Count - 1) * padding, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(sheet))
            {
                canvas.Clear(SKColors.Transparent);
                for (var r = 0; r < filled.Count; r++)
                    for (var c = 0; c < filled[r].Sprites.Count; c++)
                    {
                        var p = filled[r].Sprites[c];
                        canvas.DrawBitmap(p, c * (cellW + padding) + (cellW - p.Width) / 2, r * (cellH + padding) + (cellH - p.Height) / 2);
                    }
            }
            var names = filled.Select(r => (r.Name ?? string.Empty).Replace(",", " ")).ToList();
            return (sheet, new SpriteSheetGrid
            {
                Columns = cols, Rows = filled.Count, PaddingX = padding, PaddingY = padding,
                RowNames = names.Any(n => n.Length > 0) ? string.Join(", ", names) : string.Empty,
            });
        }

        /// <summary>What <see cref="CellOperation"/> does.</summary>
        public enum CellAction { Swap, Copy, Clear, FlipHorizontal, FlipVertical }

        /// <summary>
        /// A copy of <paramref name="picture"/> (a document-sized layer) with cells changed:
        /// <see cref="CellAction.Swap"/> trades cells <paramref name="a"/> and <paramref name="b"/>,
        /// <see cref="CellAction.Copy"/> puts a copy of <paramref name="a"/> into <paramref name="b"/>,
        /// the others clear or mirror cell <paramref name="a"/>. Cells are numbered from 0,
        /// row by row. Null if a cell number is off the grid.
        /// </summary>
        public static SKBitmap? CellOperation(SKBitmap picture, SpriteSheetGrid grid, CellAction action, int a, int b = -1)
        {
            var count = Math.Max(1, grid.Columns) * Math.Max(1, grid.Rows);
            if (a < 0 || a >= count) return null;
            if (action is CellAction.Swap or CellAction.Copy && (b < 0 || b >= count || b == a)) return null;
            SKRect Rect(int i) => grid.GetCellRect(i % Math.Max(1, grid.Columns), i / Math.Max(1, grid.Columns), picture.Width, picture.Height);

            var result = picture.Copy();
            using var canvas = new SKCanvas(result);
            using var original = SKImage.FromBitmap(picture);
            void Put(int from, int to)
            {
                canvas.Save();
                canvas.ClipRect(Rect(to));
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(original, Rect(from), Rect(to));
                canvas.Restore();
            }
            void Mirror(int i, bool horizontal)
            {
                var r = Rect(i);
                canvas.Save();
                canvas.ClipRect(r);
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(r.MidX, r.MidY);
                canvas.Scale(horizontal ? -1 : 1, horizontal ? 1 : -1);
                canvas.Translate(-r.MidX, -r.MidY);
                canvas.DrawImage(original, r, r);
                canvas.Restore();
            }
            switch (action)
            {
                case CellAction.Swap: Put(a, b); Put(b, a); break;
                case CellAction.Copy: Put(a, b); break;
                case CellAction.Clear:
                    canvas.Save();
                    canvas.ClipRect(Rect(a));
                    canvas.Clear(SKColors.Transparent);
                    canvas.Restore();
                    break;
                case CellAction.FlipHorizontal: Mirror(a, true); break;
                case CellAction.FlipVertical: Mirror(a, false); break;
            }
            return result;
        }

        /// <summary>One cell of <paramref name="sheet"/> cut out (as its own picture).</summary>
        public static SKBitmap Cell(SKBitmap sheet, SKRect rect)
        {
            var r = SKRectI.Round(rect);
            var cell = new SKBitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(cell);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(sheet, -r.Left, -r.Top);
            return cell;
        }
    }
}
