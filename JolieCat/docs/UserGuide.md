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
- [Image Size, Canvas Size and Rotation](#image-size-canvas-size-and-rotation)
- [Smart Objects](#smart-objects)
- [Import & Export](#import--export)
- [Undo / Redo](#undo--redo)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Project Files](#project-files)

## Getting Started

### Creating a project

Use **File → New** (or `Ctrl+N`, or the **+** button on the document tab strip)
to open the **New Document** dialog. Pick a size **preset** (Full HD, 4K,
square, phone story, A4 at 300 ppi, icon, pixel art) or type any **width and
height** up to 16000 px, and a **background**: white (the default), black or
transparent. Then choose one of three project types:

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

- **Open** (`Ctrl+O`) prompts for a `.jolie` project, a Photoshop `.psd`
  file, or a plain image (PNG, JPEG, WebP, BMP, GIF) — an image opens as a
  new one-layer document of its own size. **File → Open Recent** lists the
  last 10 files you opened or saved. Files passed on the command line (for
  example Windows' **Open with → JolieCat**) open as tabs at startup.
- A PSD opens with its layers, names, visibility, opacity, blend modes
  layer masks and groups (8-bit RGB/Grayscale; text/adjustment layers come
  in as their rendered pixels).
- **File → Export as Photoshop (PSD)** writes the document as a layered PSD
  that Photoshop, Krita, GIMP and Photopea can open.
- **Save** (`Ctrl+S`) writes the active document back to its `.jolie` file (or
  prompts for a location the first time). **Save As...** (`Ctrl+Shift+S`)
  always asks. A save is written to a temporary file first and swapped in at
  the end, so a crash mid-save never damages the existing project.
- A tab shows a **•** after its name while it has unsaved changes. Closing the
  tab or the app asks whether to save them.
- **Crash recovery:** every 2 minutes, documents with unsaved changes are
  copied to a recovery folder. If JolieCat doesn't shut down normally, the
  next start offers to restore them. Saving writes every layer's pixels
  and metadata, plus — for a Clipbar Animation project — the timeline's
  tracks, clips, and keyframes.
- The status bar shows a brief "Saving..." message, then a "✓" confirmation
  once the write completes.

## The Interface

From top to bottom:

- **Title bar** — the app icon, "JolieCat", and the minimize/maximize/close
  window controls. Drag anywhere on it to move the window; double-click to
  maximize or restore.
- **Menu bar** — **File**, **Edit**, **Image**, **Layer**, **Select**,
  **Filter**, **View** and **Help** menus, each item showing its keyboard
  shortcut. On the right: **Undo**/**Redo**, **Save** and **Export**.
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
- **Status bar** — save/export messages on the left; on the right a hint for
  the active tool (e.g. "Alt+click to set the clone source"), the cursor's
  pixel position, the canvas size, the active layer and the zoom level.

Both the Tools and Properties/Layers panels can be hidden independently from
the **View** menu, to reclaim canvas space.

### Navigating the canvas

- A new or opened document is fitted and centered in the window.
- **Mouse wheel** zooms around the pointer. **Ctrl+0** fits the document,
  **Ctrl+1** shows it at 100%, **Ctrl++ / Ctrl+-** step the zoom (5%–3200%).
- Hold **Space** and drag, or drag with the **middle mouse button**, to pan
  with any tool active.
- From 800% zoom a thin pixel grid appears, for pixel-art work.
- Brush tools show a circle the size of the brush under the pointer.

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
| **Retouching** | Spot Healing Brush, Clone Stamp, Healing Brush, Blur, Sharpen, Sponge, Dodge, Burn |
| **Vector & Text** | Pen (Path), Path Selection, Direct Selection, Shape, Horizontal Text, Vertical Text |
| **Transform** | Move, Crop, Free Transform, Warp |

Click a category header to expand or collapse it. Selecting a tool updates
the Properties panel above the Layers list to that tool's own options —
brush size and color for a paint tool, selection mode (New/Add/Subtract/
Intersect) for a selection tool, font/size/style for a text tool, and so on.
Each tool remembers its own settings when you switch away and back.

**Keyboard:** each tool's tooltip shows its letter (e.g. "Brush (B)"). Press
the letter to pick the tool (for a shared letter, the one you used last);
**Shift+letter** cycles through tools sharing it (M → Rectangular /
Elliptical Marquee, O → Sponge / Dodge / Burn, ...).

### Tool notes

- **Move (V)** — drag the active layer's content, or only the selected pixels
  when there is a selection (the selection moves with them). With a group
  active, or several layers selected in the Layers panel, all of them move
  together (masks included).
- **Horizontal / Vertical Text (T)** — click to type, **Enter** to finish
  (Shift+Enter for a new line), **Esc** to cancel. Each text goes on its own
  **text layer** (a "T" thumbnail) that stays editable: click the text with a
  Text tool, or double-click the "T", to change it. Moving it keeps it
  editable, and layer styles (shadow, stroke...) work on it. Painting,
  filters or transforms on a text layer first turn it into pixels
  (**Layer → Rasterize Type** does that by hand) — that step can be undone.
- **Zoom (Z)** — click to zoom in, Alt+click to zoom out.
- **Brush / Eraser / retouching tools** — `[` and `]` change the size. With
  a pen tablet, pressure can control size and/or opacity (brush options).
- **Clone Stamp (S)** — Alt+click to pick the source, then paint; a crosshair
  shows where it samples from. The offset stays fixed between strokes until
  you Alt+click again.
- **Spot Healing Brush (J)** — paint over a blemish, dust spot, wire or small
  object; when you release the mouse the painted area (shown dark while you
  paint) is rebuilt from the texture around it. No source to pick.
- **Healing Brush (J)** — like Clone Stamp, but the copied texture takes on
  the color of the area you paint over.
- **Blur / Sharpen / Sponge / Dodge / Burn** — brush-based local edits
  (soften, crisp up, desaturate, lighten, darken). Strength builds up as you
  paint over the same area; the Opacity slider sets how fast.
- **Shape (U)** — rectangle, rounded rectangle, ellipse or line in the
  primary color; Shift keeps it square/circular or the line at 45° steps.
- **Gradient (G)** — drag from where the gradient starts to where it ends (a
  live preview follows the pointer; Shift snaps to 45°). Types: Linear,
  Radial, Angle, Reflected. Colors go from the foreground to the background
  color, or from the foreground to transparent.
- **Colors** — the color panel shows two swatches: the **foreground** color
  (what brushes, shapes and text use; edited by the picker) and the
  **background** color. **X** swaps them, **D** resets to black/white.
- **Sliders** — click the number next to any slider to type an exact value
  (Enter applies, Esc cancels).
- **Canvas Rotate (R)** — drag around the middle of the canvas to turn the
  view (Shift = 15° steps); the image itself isn't changed. **Esc** with the
  tool active, or **View → Reset Rotation**, straightens it again.
- **Quick Selection (W)** — paint over what you want: the selection grows into
  similar colors around the brush (Tolerance and brush size in the options).
  The first stroke starts a selection, later strokes add to it, Alt subtracts.
- **Magnetic Lasso (L)** — draw roughly along an object's outline; the line
  snaps to the nearest strong edge.

## Layers

The Layers panel (bottom of the right-hand panel) lists every layer in the
active document, topmost/foreground layer first. Each row shows:

- An **eye** toggle — hides/shows the layer without deleting it.
- A **lock** toggle — prevents further edits to the layer.
- A **thumbnail** of the layer's content.
- The layer's **name** — double-click it to rename in place (Enter commits,
  Escape cancels).
- A **mask thumbnail**, once the layer has a mask (see [Masks](#masks)).

Above the list, the row of small buttons adds, deletes, reorders, and merges
layers:

- **Add Layer** (`Ctrl+Shift+N`) — adds a new, empty, fully-transparent
  layer above the current selection, and makes it active.
- **Duplicate Layer** (`Ctrl+J`) — copies the active layer (pixels, mask,
  settings) directly above it.
- **Delete Layer** — removes the active layer.
- **Move Up / Move Down** — reorders the active layer in the stack.
- **Merge Down** — composites the active layer onto the one behind it
  (respecting its own opacity and blend mode) and discards it.

The **Layer** menu also has **Merge Visible** (merges all visible layers,
leaving hidden ones alone) and **Flatten Image** (one layer; hidden layers are
discarded). **Edit → Transform → Flip Layer Horizontal/Vertical** mirrors the
active layer only.

Below the list, two controls apply to whichever layer is currently active:

- **Blend Mode** — a dropdown of every supported blend mode (Normal,
  Multiply, Screen, Overlay, Darken, Lighten, Color Dodge, Color Burn, Hard
  Light, Soft Light, Difference, Exclusion, Hue, Saturation, Color,
  Luminosity).
- **Opacity** — 0–100%.

Every layer-list operation (add/delete/reorder/merge, and mask add/remove)
is recorded as a single undo/redo step.

### Adjustment layers

**Layer → New Adjustment Layer** (or the half-filled circle button in the
Layers panel) adds a layer that changes the look of everything **beneath** it
without touching any pixels: Brightness/Contrast, Levels, Curves,
Hue/Saturation, Invert, Posterize, Threshold, Exposure, Black & White, Color
Balance, Photo Filter or Gradient Map.

- Its settings dialog opens right away, previewing on the canvas; **Cancel**
  removes the layer again.
- To change the settings later, **double-click its thumbnail** (or use
  **Layer → Adjustment Layer Settings...**). Each change can be undone.
- Every adjustment layer comes with a mask, and brushes paint on that mask:
  paint black where the adjustment shouldn't apply. Its **opacity** fades the
  effect.
- Hide it, move it up or down, or delete it at any time — the layers below
  are never changed. **Merge Down** bakes the effect into the layer below.
- Adjustment layers are saved in `.jolie` projects. A PSD export stores them
  as ordinary pixel layers that look the same but are no longer editable as
  adjustments.

### Selecting and dragging layers

- **Ctrl+click** layers in the Layers panel to select several; **Shift+click**
  selects a range. The last one clicked is the active layer.
- With several selected, **Group Layers** (`Ctrl+G`), **Duplicate**,
  **Delete** and **Merge** (`Ctrl+E`, merges them into one layer) act on all
  of them.
- **Drag** a layer (or the selected layers) to move it: a green line shows
  where it will land; dropped on a group's folder it goes inside the group
  (green outline). Each drag is one undo step.

### Layer groups

Groups (folders) keep related layers together:

- **Layer → New Group** (or the folder button in the Layers panel) adds an
  empty group; **Layer → Group Layers** (`Ctrl+G`) puts the active layer into
  a new group; **Ungroup Layers** (`Ctrl+Shift+G`) dissolves the active group
  but keeps its layers.
- A group's layers are shown indented below it. The arrow next to the folder
  shows or hides them.
- **Bring Forward / Send Backward** move a layer through the stack: moving
  past the edge of a group takes the layer out of it, and moving onto a group
  puts the layer inside it. A selected group moves together with its layers.
- A group's eye button hides all of its layers; its **opacity** and **blend
  mode** apply to the group's layers composited together.
- **Duplicate** and **Delete** work on the whole group; **Merge Down** on a
  group (Merge Group) turns it into one layer that looks the same.
- New layers are added directly above the active layer, inside the same group.
- To paint, pick a layer inside the group - a group itself has no pixels.
- A group can have a **mask** (Add Layer Mask with the group selected): paint
  black on it to hide part of everything in the group at once.
- Groups are saved in `.jolie` projects and in PSD files (they open in
  Photoshop as groups, and groups in PSD files are read back as groups).

### Clipping masks

**Layer → Create / Release Clipping Mask** (`Ctrl+Alt+G`) clips the active
layer to the layer below it: it shows only where that layer has pixels —
e.g. a photo or texture inside text or a shape. Several layers can be
clipped to the same base; they show a small arrow in the Layers panel. The
base's opacity and blend mode apply to the whole clipping group, and hiding
the base hides them too. Clipping is kept in `.jolie` and PSD files.

### Layer styles

**Layer → Layer Style → Effects...** (or the **fx** button in the Layers
panel) adds live effects to a layer. The list on the left switches each
effect on or off (checkbox) and picks which one's settings are shown:

- **Bevel & Emboss** — a lit edge facing the light and a shaded edge on the
  far side (size, depth, light angle, highlight and shadow colors).
- **Stroke** — an outline outside the content.
- **Inner Shadow** — a shadow cast inside the content's edge.
- **Inner Glow** — a glow along the inside of the edge.
- **Color Overlay** — recolors the content (its transparency is kept).
- **Gradient Overlay** — a two-color gradient across the layer's content, at
  any angle.
- **Outer Glow** — a soft halo around the content.
- **Drop Shadow** — color, opacity, light angle, distance and softness.

Effects follow whatever you paint on the layer later, and the canvas
previews changes while the dialog is open. A layer with a style shows an
**fx** badge — double-click it to edit. **Copy / Paste / Clear Layer Style**
move a style between layers. Styles are saved in `.jolie` projects; a PSD
export bakes them into the layer's pixels.

A **group** can have a style too: the effects are built from everything in
the group together — a stroke goes around the whole group, not around each
layer. (PSD export doesn't carry group styles yet.)

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

**Select → Color Range...** selects every pixel of the visible image close
to the foreground color — connected or not (pick the color with the
Eyedropper first; **Fuzziness** sets how close).

**Content-Aware Fill** (`Shift+F5`, Edit menu): select an object you want
gone — loosely, with a little background around it — and the selection is
filled with texture continued from its surroundings.

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

**Combining selections:** hold **Shift** while starting a selection to add
to the current one, **Alt** to subtract, **Shift+Alt** to intersect — or pick
the mode in the selection tool's options. The **Feather** option (or
**Select → Feather...**, `Shift+F6`) gives the selection a soft edge:
painting, fills, deletes, filters and adjustments fade out across it.

The **Select** menu adds **All** (`Ctrl+A`), **Deselect** (`Ctrl+D`),
**Reselect** (`Ctrl+Shift+D`, brings back the last selection) and **Inverse**
(`Ctrl+Shift+I`). **Select → Modify** reshapes the selection by a number of
pixels: **Expand**, **Contract**, **Border** (a band along the edge) and
**Smooth** (rounds corners, drops specks). **Delete** clears the selected
pixels; **Alt+Backspace** fills the selection with the foreground color and
**Ctrl+Backspace** with the background color (the whole layer when nothing
is selected).

## Filters

### Liquify (Shift+Ctrl+X)

**Filter → Liquify...** opens a large preview of the layer to reshape with
brushes: **Forward Warp** (W) pushes pixels along your stroke, **Pucker**
(S) pulls them toward the brush center, **Bloat** (B) pushes them out,
**Twirl** (C / Alt+C) turns them, and **Reconstruct** (R) brushes changes
back to the original. Pucker, Bloat, Twirl and Reconstruct keep working
while you hold the mouse still. `[` / `]` change the brush size, Ctrl+Z
undoes the last stroke, **Restore All** starts over. **Show other layers**
draws the rest of the image underneath so you can see how the change fits.
OK applies everything as one undo step.

### Camera Raw Filter (Shift+Ctrl+A)

Photo development sliders with a live preview: **Temperature** and **Tint**
(white balance), **Exposure** (in stops), **Contrast**, **Highlights**
(negative recovers a bright sky), **Shadows** (positive opens up dark areas),
**Whites**, **Blacks**, **Clarity** (midtone local contrast), **Vibrance**
(boosts muted colors more than vivid ones) and **Saturation**. Highlights,
Shadows and Clarity look at the surrounding area with an edge-aware method,
so they don't draw halos along hard edges. **Auto** sets a quick starting
point.

### Other filters

**Filter** menu commands open a shared dialog with a **live preview** on the
canvas as you adjust its slider — nothing is committed until you click
**Apply**; **Cancel** (or closing the dialog any other way) discards the
preview and leaves the layer untouched. With an active selection, filters
and adjustments only change the selected area. Available filters:

- **Blur → Gaussian Blur**, **Box Blur**, **Motion Blur** (distance and
  angle), **Radial Blur** (zoom streaks from the center), **Surface Blur**
  (smooths skin and noise but keeps edges sharp)
- **Distort → Twirl**, **Pinch** (negative values bulge), **Wave**
- **Other → High Pass** (keeps only fine detail on gray; put it on a copy of
  the photo with the Overlay blend mode to sharpen)
- **Sharpen → Sharpen**, **Unsharp Mask** (amount and radius — the usual
  photo sharpening)
- **Noise → Add Noise**, **Median** (removes dust, scratches and speckles
  while keeping edges)
- **Pixelate**, **Vignette**
- **Stylize → Emboss**, **Find Edges** (Find Edges has no settings and
  applies immediately)

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

- **Desaturate** (`Ctrl+Shift+U`) and **Invert** (`Ctrl+I`) apply at once.
- **Posterize**, **Threshold** and **Vibrance** (boosts dull colors more than
  already-saturated ones, so skin tones stay natural) use the filter dialog.
- **Image → Auto Contrast** (`Ctrl+Alt+Shift+L`) stretches the tonal range;
  **Image → Auto Color** (`Ctrl+Shift+B`) does it per channel, which also
  removes color casts. Both apply at once and can be undone.
- **Exposure** — exposure in stops (+1 doubles the brightness), offset and gamma.
- **Black & White** — converts to gray, with sliders for how much the reds,
  greens and blues each contribute (darken a blue sky with a low Blues value).
- **Color Balance** — shifts colors toward cyan/red, magenta/green and
  yellow/blue, separately for shadows, midtones and highlights.
- **Photo Filter** — like a colored lens filter: color, density and
  Preserve Luminosity.
- **Gradient Map** — maps dark-to-light onto a shadow and a highlight color
  (duotone looks).

These live under **Image → Adjustments**. All of them apply only to the
active layer, and require one to be selected.

## Image Size, Canvas Size and Rotation

- **Image → Image Size...** (`Ctrl+Alt+I`) rescales the whole image — every
  layer and mask. Set a percentage or exact width/height; **Lock Aspect
  Ratio** prevents stretching. Downscaling is smoothed (no jagged edges or
  moiré), upscaling uses bicubic interpolation.
- **Image → Canvas Size...** (`Ctrl+Alt+C`) changes the canvas without
  rescaling: content is cropped if you shrink it and padded with transparency
  if you grow it. **Keep content centered** (on by default) grows/shrinks
  evenly on all sides; turn it off to pin the content to the top-left corner.
- **Image → Image Rotation** rotates the whole image 180°, 90° clockwise or
  90° counter-clockwise, or flips the canvas horizontally/vertically.

Each of these is a single undo/redo step, and clears the selection.

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

## Actions

The **ACTIONS** tab (next to LAYERS and HISTORY) records a sequence of
commands and plays it back on any document — handy for giving many photos
the same treatment.

- **+** creates an action and starts recording (● REC shows). Then just work:
  filters, Camera Raw, Brightness/Contrast, Hue/Saturation, Image Size,
  Image Rotation / Flip Canvas, Flip Layer, New / Duplicate / Delete Layer,
  Merge Down, Merge Visible and Flatten Image are recorded with their
  settings. **■** stops.
- **▶** plays the selected action on the active document, with no dialogs.
  Each step is its own undo step.
- The red **●** button adds more steps to the end of the selected action; the
  trash buttons delete an action or the selected step. Rename an action in
  the **Name** box.
- An Image Size recorded with the proportions kept replays as "this width,
  same aspect ratio", so it works on images of any shape.
- Levels and Curves are recorded too.
- Actions are saved automatically (in `%AppData%\JolieCatctions.json`).
  A "Vintage look" example is included.

### Batch processing

**File → Automate → Batch...** plays an action on every image (PNG, JPEG,
WebP, BMP, GIF) in a folder — optionally its subfolders too — and saves the
flattened results to an output folder as PNG, JPEG or WebP, with an optional
name suffix (e.g. `_edited`). The files are processed without opening tabs;
**Stop** ends the run after the current file. If a step can't run on some
file, that file is skipped and reported. Saving into the source folder
requires a suffix, so originals are never overwritten.

## Undo / Redo

Every structural change (layer add/delete/reorder/merge, mask add/remove,
canvas resize) and every paint/adjustment operation is recorded on a single
undo/redo history stack per document:

- **Undo** — `Ctrl+Z`
- **Redo** — `Ctrl+Y` or `Ctrl+Shift+Z`

The **HISTORY** tab next to **LAYERS** lists every step by name (the tool
or command that made it). Click a step to go back to that state — later
steps stay listed, dimmed, until you do something new; click one of them to
go forward again.

History keeps up to 200 steps (or about 1 GB, whichever comes first). Paint
steps only store the parts of the image they changed, so even large
documents get deep history. Layer operations no longer wipe earlier history:
you can paint, add a layer, and still undo the painting.

## Keyboard Shortcuts

**Help → Keyboard Shortcuts** (`F1`) shows the full list in the app.

| Shortcut | Action |
|---|---|
| `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` | New / Open (.jolie, .psd, images) / Save / Save As |
| `Shift` / `Alt` / `Shift+Alt` + selection | Add / Subtract / Intersect |
| `Shift+F6` | Feather selection |
| `Shift+F5` | Content-Aware Fill |
| `Shift+Ctrl+X` | Liquify |
| `Shift+Ctrl+A` | Camera Raw Filter |
| `Ctrl+G` / `Ctrl+Shift+G` | Group / Ungroup layers |
| `Ctrl+Alt+G` | Create / release clipping mask |
| `Ctrl+Shift+E` | Export image |
| `Ctrl+W` | Close the active document tab |
| `Ctrl+Z` / `Ctrl+Y` | Undo / Redo |
| `Ctrl+C` / `Ctrl+V` | Copy selection / Paste as a new layer |
| `Ctrl+A` / `Ctrl+D` / `Ctrl+Shift+D` / `Ctrl+Shift+I` | Select all / Deselect / Reselect / Inverse |
| `Delete` / `Alt+Backspace` / `Ctrl+Backspace` | Clear / Fill with foreground / Fill with background |
| `X` / `D` | Swap colors / Default colors (black, white) |
| `Ctrl+T` | Free Transform |
| `Ctrl+Shift+N` / `Ctrl+J` / `Ctrl+E` | New / Duplicate / Merge Down layer |
| `Ctrl+[` / `Ctrl+]` | Send backward / Bring forward |
| `Ctrl+L` / `Ctrl+M` / `Ctrl+U` | Levels / Curves / Hue-Saturation |
| `Ctrl+I` / `Ctrl+Shift+U` | Invert / Desaturate |
| `Ctrl+Alt+I` / `Ctrl+Alt+C` | Image size / Canvas size |
| `Ctrl+Alt+Shift+L` / `Ctrl+Shift+B` | Auto Contrast / Auto Color |
| `Ctrl+0` / `Ctrl+1` / `Ctrl++` / `Ctrl+-` | Fit / 100% / Zoom in / Zoom out |
| `Space`+drag, middle mouse | Pan |
| `[` / `]` | Smaller / bigger brush |
| Tool letters | V, M, L, W, C, I, B, N, E, G, S, J, O, R, P, A, T, U, H, Z |

## Project Files

JolieCat projects are saved as `.jolie` files (filter: *JolieCat Project
(\*.jolie)*), holding every layer's pixels and metadata, mask data, the
project type, and — for a Clipbar Animation project — the Timeline's tracks,
clips, and keyframes. A `.jolie` file is self-contained; reopening it
restores the full editing state, including which layer/mask was active.
