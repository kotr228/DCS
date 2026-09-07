# JolieCat User Guide

JolieCat is a 2D raster/vector graphics and animation editor built on .NET 8, WPF,
and SkiaSharp. It edits layered `.jolie` project files, supports non-destructive
masks and Smart Objects, and — for sprite-based work — a dedicated sprite-sheet
slicing workflow that can graduate into a frame-by-frame animation timeline. See
[`ClipbarWorkflow.md`](ClipbarWorkflow.md) for that pipeline in detail; this guide
covers the general editor: the interface, layers, masks, filters, color
adjustments, copy/paste, and canvas resizing.

## Table of Contents

- [Getting Started](#getting-started)
- [The Interface](#the-interface)
- [Multi-Document Tabs](#multi-document-tabs)
- [The Tools Panel](#the-tools-panel)
- [Layers](#layers)
- [Masks](#masks)
- [Selections & Copy/Paste](#selections--copypaste)
- [Filters](#filters)
- [Color Adjustments](#color-adjustments)
- [Manual Canvas Resizing](#manual-canvas-resizing)
- [Smart Objects](#smart-objects)
- [Import & Export](#import--export)
- [Undo / Redo](#undo--redo)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Project Files](#project-files)

## Getting Started

### Creating a project

Use **File → New** (or `Ctrl+N`, or the **+** button on the document tab strip)
to open the **New Project** dialog. Choose one of three project types:

| Project Type | What it gives you |
|---|---|
| **Standard Image** | The ordinary raster/vector canvas — no extra panels. The right choice for illustrations, textures, and general image editing. |
| **Sprite Sheet** | Adds a configurable slicing grid overlay on the canvas plus a Sprite Sheet Grid panel, for laying out and exporting individual frames from one sheet. |
| **Clipbar Animation** | Opens straight into a dedicated, full-workspace Timeline tab with playback transport controls, for frame-by-frame animation. |

The project type is chosen once, at creation, and is saved with the project.
See [`ClipbarWorkflow.md`](ClipbarWorkflow.md) for the Sprite Sheet and Clipbar
Animation workflow specifically — the rest of this guide applies to all three
project types equally.

### Opening and saving

- **Open** (`Ctrl+O`) prompts for a `.jolie` file.
- **Save** (`Ctrl+S`) writes the active document back to its `.jolie` file (or
  prompts for a location the first time). Saving writes every layer's pixels
  and metadata, plus — for a Clipbar Animation project — the timeline's
  tracks, clips, and keyframes.
- The status bar shows a brief "Saving..." message, then a "✓" confirmation
  once the write completes.

## The Interface

From top to bottom:

- **Title bar** — the app icon, "JolieCat", and the minimize/maximize/close
  window controls. Drag anywhere on it to move the window; double-click to
  maximize or restore.
- **Top toolbar** — panel visibility toggles (**Tools**, **Properties**),
  **Open**/**Save**/**Import...**/**Place Smart Object...**/**Export...**/
  **Resize Canvas...**, and **Undo**/**Redo**.
- **Document tab strip** — one tab per open document (see
  [Multi-Document Tabs](#multi-document-tabs)), plus a **+** button to create
  another.
- **Left panel — Tools** — the tool palette, grouped into collapsible
  categories (Selection, Navigation, Painting, Retouching, Vector & Text,
  Transform). Click a tool tile to select it; the active tool is highlighted.
- **Center — Canvas** (or the **Timeline workspace**, for an active Clipbar
  Animation document — see [`ClipbarWorkflow.md`](ClipbarWorkflow.md)).
  A checkerboard pattern shows through transparent pixels; the area outside
  the document bounds is a plain neutral "desk" background.
- **Right panel — Properties / Layers** — the active tool's own options at
  the top (brush size, selection mode, font, etc. — whatever the current
  tool needs), and the Layers panel below it.
- **Status bar** — the active layer's name, size, and position, plus
  transient save-status messages.

Both the Tools and Properties/Layers panels can be hidden independently via
their toolbar toggle buttons, to reclaim canvas space.

## Multi-Document Tabs

JolieCat can have several documents open at once, each its own independent
tab with its own layer stack, undo/redo history, pan/zoom, and (for a Clipbar
Animation project) its own Timeline:

- **New tab**: the tab strip's **+** button, or `Ctrl+N` — opens the New
  Project dialog again for the new tab.
- **Close tab**: the **✕** on a tab, or `Ctrl+W` — closes whichever tab is
  active. Closing the last remaining tab opens a fresh blank document in its
  place, rather than leaving the workspace empty.
- **Switch tabs**: click a tab, or use the tab strip like any other list.
- Copy/paste works across tabs — see [Selections & Copy/Paste](#selections--copypaste).

## The Tools Panel

Tools are grouped into six categories:

| Category | Tools |
|---|---|
| **Selection** | Rectangular Marquee, Elliptical Marquee, Lasso, Polygonal Lasso, Magnetic Lasso, Quick Selection, Magic Wand |
| **Navigation** | Hand (Pan), Zoom, Canvas Rotate |
| **Painting** | Brush, Pencil, Eraser, Paint Bucket, Gradient, Eyedropper |
| **Retouching** | Clone Stamp, Healing Brush, Blur, Sharpen, Sponge, Dodge, Burn |
| **Vector & Text** | Pen (Path), Path Selection, Direct Selection, Shape, Horizontal Text, Vertical Text |
| **Transform** | Crop, Free Transform, Warp |

Click a category header to expand or collapse it. Selecting a tool updates
the Properties panel above the Layers list to that tool's own options —
brush size and color for a paint tool, selection mode (New/Add/Subtract/
Intersect) for a selection tool, font/size/style for a text tool, and so on.

> **Note:** each tool's tooltip suggests a single-letter shortcut (e.g. "B"
> for Brush), matching the convention other raster editors use. These are
> currently informational only — tool selection is via the Tools panel, not
> a keyboard shortcut.

## Layers

The Layers panel (bottom of the right-hand panel) lists every layer in the
active document, topmost/foreground layer first. Each row shows:

- A **visibility** checkbox — hides/shows the layer without deleting it.
- A **locked** checkbox — prevents further edits to the layer.
- The layer's **name** — double-click it to rename in place (Enter commits,
  Escape cancels).
- A **mask thumbnail**, once the layer has a mask (see [Masks](#masks)).

Above the list, the row of small buttons adds, deletes, reorders, and merges
layers:

- **Add Layer** — adds a new, empty, fully-transparent layer above the
  current selection, and makes it active.
- **Delete Layer** — removes the active layer.
- **Move Up / Move Down** — reorders the active layer in the stack.
- **Merge Down** — composites the active layer onto the one behind it
  (respecting its own opacity and blend mode) and discards it.

Below the list, two controls apply to whichever layer is currently active:

- **Blend Mode** — a dropdown of every supported blend mode (Normal,
  Multiply, Screen, Overlay, Darken, Lighten, Color Dodge, Color Burn, Hard
  Light, Soft Light, Difference, Exclusion).
- **Opacity** — 0–100%.

Every layer-list operation (add/delete/reorder/merge, and mask add/remove)
is recorded as a single undo/redo step.

## Masks

A mask lets you hide part of a layer non-destructively, without erasing its
actual pixels.

- **Add Mask** — attaches a fresh, fully-visible mask to the active layer (a
  no-op if it already has one).
- **Remove Mask** — detaches the active layer's mask entirely.
- Once a layer has a mask, its row grows a **mask thumbnail** button next to
  the name. Click it to toggle which target your paint tools affect: the
  layer's own pixel content, or the mask itself (painting black on the mask
  hides, white reveals — the mask thumbnail's own border highlights when the
  mask is the active paint target).
- A separate **enabled** checkbox lets you temporarily disable a mask
  (compare the layer with and without it) without removing it outright.

## Selections & Copy/Paste

Any Selection tool (Marquee, Lasso, Magic Wand, etc.) defines the active
region other operations respect — paint tools only affect pixels inside it,
and Copy/Paste crop to it:

- **Copy** (`Ctrl+C`) copies the active layer's pixels within the current
  selection into the clipboard — cropped to the selection's bounding box and
  clipped to its actual shape, so a non-rectangular selection (an ellipse, a
  lasso) doesn't bring its corners along. With no active selection, the
  whole layer is copied.
- **Paste** (`Ctrl+V`) pastes the clipboard's content as a brand new,
  topmost layer, centered on the document — regardless of the current
  pan/zoom, and even into a different document tab than it was copied from.

## Filters

**Filters** menu commands open a shared dialog with a **live preview** on the
canvas as you adjust its sliders — nothing is committed until you click
**Apply**; **Cancel** (or closing the dialog any other way) discards the
preview and leaves the layer untouched. Available filters:

- **Gaussian Blur**
- **Box Blur**
- **Sharpen**
- **Noise**

## Color Adjustments

Same live-preview/Apply/Cancel pattern as Filters:

- **Brightness/Contrast** — two sliders.
- **Hue/Saturation** (Lightness included) — three sliders.
- **Levels** — input/output black point, white point, and gamma, plus a
  histogram of the layer's current luminance distribution (computed once
  when the dialog opens, so it always reflects the real pre-adjustment
  content).
- **Curves** — an interactive tone curve: click the graph to add a control
  point, drag a point to move it, right-click a point to remove it. The
  diagonal line is the unadjusted reference; the white curve shows the
  actual input→output mapping the current points define.

All four adjustments apply only to the active layer, and require one to be
selected.

## Manual Canvas Resizing

**Resize Canvas...** in the top toolbar opens a dialog for a new width and
height (in pixels), with an optional **Lock Aspect Ratio** checkbox. Existing
content — every layer's pixels *and* its mask — stays anchored at the
top-left corner: cropped if you shrink the canvas, padded with transparency
if you grow it, with no shift in either direction. This is recorded as a
single undo/redo entry.

## Smart Objects

**Place Smart Object...** places an image file as its own layer type that
stays linked to the original file: scaling or rotating it later always
re-samples from that source rather than degrading a raster copy, exactly
like a non-destructive Smart Object layer in other editors. Double-clicking
a Smart Object layer opens its embedded content in its own document tab for
editing.

## Import & Export

- **Import...** adds one or more image files (PNG/JPG/BMP/WebP) as new
  layers — or simply drag image files onto the canvas.
- **Export...** writes either the flattened composite or a single selected
  layer (masked, if it has one) to PNG, JPEG, or WebP. A **Quality** slider
  applies to the two lossy formats; it's disabled (not hidden) when PNG is
  selected, since PNG is always lossless.

## Undo / Redo

Every structural change (layer add/delete/reorder/merge, mask add/remove,
canvas resize) and every paint/adjustment operation is recorded on a single
undo/redo history stack per document:

- **Undo** — `Ctrl+Z`
- **Redo** — `Ctrl+Y` or `Ctrl+Shift+Z`

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New document (opens the New Project dialog) |
| `Ctrl+O` | Open a `.jolie` project |
| `Ctrl+S` | Save the active project |
| `Ctrl+W` | Close the active document tab |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Ctrl+C` | Copy the current selection |
| `Ctrl+V` | Paste as a new layer |

## Project Files

JolieCat projects are saved as `.jolie` files (filter: *JolieCat Project
(\*.jolie)*), holding every layer's pixels and metadata, mask data, the
project type, and — for a Clipbar Animation project — the Timeline's tracks,
clips, and keyframes. A `.jolie` file is self-contained; reopening it
restores the full editing state, including which layer/mask was active.
