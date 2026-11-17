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
- **File → Export Layers to Files...** saves every visible layer (with its
  mask and style) as its own PNG, JPEG or WebP, named after the layer —
  trimmed to the layer's content or at the full canvas size.
- **File → Export as Photoshop (PSD)** writes the document as a layered PSD
  that Photoshop, Krita, GIMP and Photopea can open.
- **Save** (`Ctrl+S`) writes the active document back to its `.jolie` file (or
  prompts for a location the first time). **Save As...** (`Ctrl+Shift+S`)
  always asks. A save is written to a temporary file first and swapped in at
  the end, so a crash mid-save never damages the existing project.
- A tab shows a **•** after its name while it has unsaved changes. Closing the
  tab or the app asks whether to save them.
- **Help → Edit Keyboard Shortcuts...** lists every command that has a key
  (named as in the menus) with its key. Type a new one (`Ctrl+Shift+K`,
  `F5`, `Alt+Ctrl+1`) or clear it for none; a key that isn't one, or that
  another command already uses, is marked and must be fixed before OK.
  **Reset All** brings back the built-in keys. The menus show your keys, and
  they are kept for next time. (The single-letter tool keys — B, V, M... —
  belong to the tools.)
- **Edit → Preferences...** (`Ctrl+K`) sets how many steps Undo can go back
  (**History States**) and how much memory those steps may use, how often
  recovery copies are made (0 turns them off), and how many files **Open
  Recent** lists. **Ruler Units** sets what the rulers count in: pixels,
  inches, centimeters or percent of the document's size; inches and
  centimeters use **Resolution** (pixels per inch, 72 unless you change
  it). **Transparency Grid Size / Colors** change the checkerboard shown
  where the image is see-through (small, medium or large squares; dark,
  medium or light). **Language** switches the interface between English,
  Ukrainian and Russian. The settings apply at once and are kept for next time.
- **Crash recovery:** every 2 minutes (see Preferences), documents with unsaved changes are
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
  shortcut. On the right: the **language switcher** (EN / УКР / РУС),
  **Undo**/**Redo**, **Save** and **Export**.
- **Interface language** — English, Ukrainian or Russian. Pick it in the
  switcher on the menu bar or in **Edit → Preferences... → Language**: the
  menus, panels, dialogs, tooltips and messages change at once, with no
  restart, and the choice is kept for next time. Names you give things
  (layers, documents, artboards) are never translated.
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

### Rulers, grid and guides

- **View → Rulers** (`Ctrl+R`) shows rulers in document pixels along the top
  and left; a blue mark follows the pointer.
- **View → Grid** (`Ctrl+'`) draws a grid over the document; **View → Grid
  Size...** sets its spacing.
- **Guides**: drag from the left ruler for a vertical guide, from the top ruler
  for a horizontal one, or use **View → New Guide...** to type a position.
  With the Move tool, drag a guide to move it, or drop it back on a ruler to
  remove it. **View → Guides** (`Ctrl+;`) hides/shows them; **Clear Guides**
  removes all.
- With **Snap to Guides and Grid** on, the marquee, crop, shape and gradient
  tools snap to nearby guides, grid lines and the document edges. The Move
  tool snaps the moved content's left/center/right and top/middle/bottom
  edges to guides, grid lines and the document's edges and center (only while
  there are guides or the grid is shown).
- Guides are saved in `.jolie` projects, and they turn, flip, scale and shift
  with the image when you rotate or flip the canvas, change Image/Canvas Size
  or crop (undo puts them back too).

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
| **Painting** | Brush, Mixer Brush, Pencil, Eraser, Background Eraser, Magic Eraser, Paint Bucket, Gradient, Eyedropper, Color Sampler |
| **Retouching** | Spot Healing Brush, Clone Stamp, Pattern Stamp, Healing Brush, Patch, Content-Aware Move, Red Eye, History Brush, Blur, Sharpen, Smudge, Sponge, Dodge, Burn |
| **Vector & Text** | Pen (Path), Path Selection, Direct Selection, Shape, Horizontal Text, Vertical Text |
| **Transform** | Move, Crop, Perspective Crop, Free Transform, Puppet Warp, Warp |

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
  The Text tool's options: any font installed on the computer, size, **B /
  I / U / S** (bold, italic, underline, strikethrough), **Alignment** (left /
  center / right, for multi-line text), **Leading** (line spacing),
  **Tracking** (letter spacing) and **Warp Text**: Arc, Arch, Bulge, Wave,
  Flag, Rise or Fish, with a **Bend** amount (negative bends the other way). With a text layer selected and the Text
  tool active, changing any of these restyles that layer at once (one undo
  step).
  **Paragraph text:** drag a box with the Text tool instead of clicking
  (a dashed outline shows it), then type: the words wrap to the box's width,
  and **Alignment** places each line within the box (left, centered or
  right). The typing box on the canvas wraps the same way. A plain click
  still makes ordinary text whose lines break only at Enter. A word longer
  than the box is broken where it doesn't fit. The box width is saved with
  the project.
  **Type on a path:** draw a curve with the **Pen** tool (click points, drag
  for curves), then choose **Layer → Type on Path...** and type the text. The
  letters follow the curve, each turned along it, in the Text tool's font,
  size and tracking and the foreground color. **Alignment** puts the text at
  the start, middle or end of the curve. Letters that don't fit past the
  curve's end are left out. The text stays editable with the Text tool, and
  moving the layer moves its curve too.
- **Zoom (Z)** — click to zoom in, Alt+click to zoom out.
- **Ruler (I, with the Eyedropper)** — drag on the image to measure: the
  Properties panel shows the length, the angle, the width and height, and
  where the line starts and ends. Shift keeps the line at 45° steps. Drag
  along something that should be level — a horizon, a table edge — and
  click **Straighten Layer**: the active layer turns around the middle of
  the image so the line becomes level (or upright, if it was closer to
  vertical). One undo step. **Clear** removes the line.
- **Picture brushes:** select an area (or nothing, for the whole image) and
  choose **Edit → Define Brush Preset...**: its darker, opaque parts become a
  brush tip (white and see-through parts don't paint), saved with your brush
  presets. The Brush tool takes it at once; each dab stamps that shape in the
  foreground color, at the brush size, turned by Angle and squashed by
  Roundness, with jitter, scatter and pen pressure as usual. The options show
  the **Tip** in use; **Round tip** goes back to the ordinary one.
- **Brush / Eraser / retouching tools** — `[` and `]` change the size. With
  a pen tablet, pressure can control size and/or opacity (brush options).
  **Flow** sets how much paint each dab lays down (low flow builds up like an
  airbrush). Under **Brush tip & dynamics**: **Angle** and **Roundness** make
  a flat, angled tip (calligraphy), **Size jitter** varies each dab's size and
  **Scatter** spreads dabs around the stroke (chalk, spray). Presets at the top
  — Hard Round, Soft Round, Airbrush, Calligraphy, Chalk, Spray — set all of
  these at once; **Save as preset** keeps your own (right-click it to delete).
  The brush outline on the canvas shows the tip's shape and angle.
- **Clone Stamp (S)** — Alt+click to pick the source, then paint; a crosshair
  shows where it samples from. The offset stays fixed between strokes until
  you Alt+click again.
- **Spot Healing Brush (J)** — paint over a blemish, dust spot, wire or small
  object; when you release the mouse the painted area (shown dark while you
  paint) is rebuilt from the texture around it. No source to pick.
- **Healing Brush (J)** — like Clone Stamp, but the copied texture takes on
  the color of the area you paint over.
- **Patch (J)** — draw around an area (or use any selection), then drag the
  selection. **Source** mode (default): drag onto clean texture; the selected
  area is rebuilt from there and healed into its surroundings so no seam
  shows — for blemishes and stains. **Destination** mode: the selected
  content is copied to where you drop it. One undo step.
- **Perspective Crop (C)** — drag the four corner handles onto the corners of
  something seen at an angle (a page, a sign, a painting); Enter straightens
  it into a rectangle and crops to it, Esc puts the corners back. Dragging
  away from the handles draws a new rectangle to start from. One undo step.
- **Content-Aware Move (J)** — draw around an object, then drag it to its new
  place: it moves there (its edge fades in over a few pixels) and the place it
  left is filled in from its surroundings. **Extend** mode copies it instead
  and leaves the original. Draw closely around the object: whatever else is
  inside the selection moves with it.
- **Blur / Sharpen / Sponge / Dodge / Burn** — brush-based local edits
  (soften, crisp up, desaturate, lighten, darken). Strength builds up as you
  paint over the same area; the Opacity slider sets how fast.
- **Smudge (R)** — drags paint along the stroke, like a finger through wet
  paint. Opacity sets how strongly.
- **History Brush (Y)** — paints the image back the way it was in an earlier
  state: by default when the document was opened (the **Opening state**
  snapshot in the History panel). Click the brush icon next to any snapshot
  to paint from that one instead. It brings back erased (transparent) areas
  too.
- **Background Eraser (E)** — erases only colors close to the one under the
  brush's center, so you can run it along an object's edge and remove the
  background without touching the object. **Tolerance** sets how close.
- **Magic Eraser (E)** — one click erases the area of similar color around
  it (or that color everywhere, with **Contiguous** off). Tolerance as for
  the Paint Bucket.
- **Mixer Brush (B)** — paints with the foreground color, but the paint
  picks up the colors it passes over, like a wet brush on wet paint, so
  strokes blend into what's there. **Wet** sets how much it picks up.
- **Pattern Stamp (S)** — paints with a pattern (pick it under
  **Pattern**; built-in ones use the foreground color). The pattern is
  anchored to the document, so separate strokes line up seamlessly.
- **Color Sampler (I)** — click to place up to 10 sample points; their
  colors stay listed in the **INFO** panel while you edit (handy to watch a
  skin tone or a gray point during color corrections). Drag a point to move
  it, Alt+click it to remove it, or **Clear** them all in the INFO panel.
  They are not saved in the file.
- **Puppet Warp** (also **Edit → Puppet Warp**) — click to stick pins into
  the active layer, then drag a pin: the image bends around the other pins
  like rubber, without stretching. Alt+click a pin to remove it. **Enter**
  applies (one undo step), **Esc** cancels; switching tools applies too.
  **Rigidity** sets how broadly a pin's pull spreads, **Mesh Cell Size**
  how fine the bending is (smaller is smoother), **Show Mesh** shows the
  bent grid. Pin the parts that must stay put first.
- **Red Eye (J)** — click on a red pupil: the red is replaced with a dark
  natural tone. **Pupil Size** is how far around the click it looks;
  **Darken Amount** how dark the pupil gets. Skin around it is left alone.
- **Shape (U)** — rectangle, rounded rectangle, ellipse, line, polygon or
  star; Shift keeps it square/circular or the line at 45° steps. In **Shape**
  mode (the default) each shape goes on its own editable **shape layer**
  (a small shape badge on its thumbnail): select it with the Shape tool and
  change its fill and stroke colors, stroke width, corner radius or number of
  sides — it redraws sharply (one undo step). The Move tool moves it as a
  shape; painting or filters turn it into pixels first. Image Size scales
  shapes (and text) and keeps them editable; rotating or flipping the canvas
  keeps rectangles, ellipses and lines as shapes (text, polygons and stars
  become pixels then). **Pixels** mode paints
  the shape straight onto the active layer.
- **Gradient (G)** — drag from where the gradient starts to where it ends (a
  live preview follows the pointer; Shift snaps to 45°). Types: Linear,
  Radial, Angle, Reflected. Colors go from the foreground to the background
  color, from the foreground to transparent, or **Custom**: any number of
  color stops, each with its location and opacity (Add color stop / ✕ to
  remove). The presets B&W, Spectrum, Sunset, Chrome and Fade are starting
  points. On the gradient bar, drag a stop to move it, click under the bar to
  add one, double-click a stop to change its color, drag a stop down off the
  bar to remove it. **Save as preset** keeps your gradient as a preset button
  (right-click it to delete).
- **Colors** — the color panel shows two swatches: the **foreground** color
  (what brushes, shapes and text use; edited by the picker) and the
  **background** color. **X** swaps them, **D** resets to black/white.
- **Sliders** — click the number next to any slider to type an exact value
  (Enter applies, Esc cancels).
- **Canvas Rotate (R)** — drag around the middle of the canvas to turn the
  view (Shift = 15° steps); the image itself isn't changed. **Esc** with the
  tool active, or **View → Reset Rotation**, straightens it again.
- **Object Selection (W)** — drag a box loosely around a thing: the thing
  inside is selected (the box's edges count as background, so leave a little
  room around it). Shift adds to the selection, Alt subtracts. Works best when
  the thing differs in color from what's around it.
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

**Edit → Transform → Distort / Perspective...** pins the active layer's
painted part by its four corners: drag each blue corner on the preview
(the rest of the image shows underneath). Fit a picture into a frame, a
screen or a book cover, or straighten a photo of a poster taken at an
angle. With **Perspective** on, dragging a corner along an edge moves the
corner across from it the other way, so the layer narrows or widens
evenly. **Reset** puts the corners back. It works on pixel layers (text,
shape, fill and Smart Object layers need Rasterize first); the layer
mask stays as it is. One undo step.

Below the list, three controls apply to whichever layer is currently active:

- **Blend Mode** — all 27 of Photoshop's modes, in its order: Normal,
  Dissolve; Darken, Multiply, Color Burn, Linear Burn, Darker Color; Lighten,
  Screen, Color Dodge, Linear Dodge (Add), Lighter Color; Overlay, Soft
  Light, Hard Light, Vivid Light, Linear Light, Pin Light, Hard Mix;
  Difference, Exclusion, Subtract, Divide; Hue, Saturation, Color,
  Luminosity. They are saved in the project and in PSD files.
- **Opacity** — 0–100%.
- **Fill** — 0–100%, like Opacity but only for the layer's own pixels (and
  overlays inside them): a drop shadow, outer glow or stroke stays at full
  strength. At 0% only the effects show — for example a stroke outline with
  nothing inside, or a shadow cast by an invisible shape.

Every layer-list operation (add/delete/reorder/merge, and mask add/remove)
is recorded as a single undo/redo step.

### Fill layers

**Layer → New Fill Layer → Solid Color / Gradient / Pattern** adds a layer
that covers the canvas with a color, a two-color gradient (linear or
radial, at any angle) or a pattern (any built-in one or your own, at any
scale). With a selection active, the new layer's mask shows the fill only
inside it. A small paint-bucket badge marks fill layers: **double-click the
thumbnail** (or **Layer → Layer Content Options...**) to change the fill — it
previews live and is one undo step. A fill layer keeps filling the whole
canvas through Canvas Size, Image Size, rotation and cropping. Painting on it
turns it into ordinary pixels first.

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
- **Layer → Arrange** also has **Bring to Front** (`Shift+Ctrl+]`) and
  **Send to Back** (`Shift+Ctrl+[`): straight to the top or bottom of the
  layer's level (inside its group, if it's in one).
- **Layer → Align** lines layers up: **Top / Bottom / Left / Right Edges**
  or **Vertical / Horizontal Centers**. With several layers selected
  (Ctrl+click in the Layers panel), they line up with each other, using the
  outer edges of all of them together. With one layer, it lines up with the
  canvas (Horizontal Centers puts it in the middle). Only what's painted
  counts, not the empty part of the layer. Text moves with its layer and
  stays editable. Locked layers stay put.
- **Layer → Distribute** spaces three or more selected layers so their
  middles are evenly apart between the outermost two.
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

### Picking colors

Wherever a dialog asks for a color (Layer Style, adjustments, gradient
stops) there is a hex box and a swatch: click the swatch to open the **Color
Picker** — a saturation/brightness square, a hue strip, R/G/B and hex fields,
and the new color above the current one (click the current one to go back).

The color panel also has your own **SWATCHES**: **+** adds the foreground
color, clicking a swatch picks it, right-click deletes it. They're kept
between runs.

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
- **Pattern Overlay** — a repeating pattern (Checker, Stripes, Diagonal, Dots,
  Grid, Noise) in any color, with scale and opacity. **Edit → Define Pattern**
  turns the selected part of the image (or the whole image) into your own
  pattern — "Pattern 1", "Pattern 2", ... — which then appears in the list,
  in its own colors. Your patterns are kept between runs. **Edit → Manage
  Patterns...** shows them with previews; rename (layer styles in open
  documents follow the new name) or delete them.
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

**Select → Sky** selects the sky (plain, bright or blue areas reaching the
top edge). **Select → Focus Area...** selects the sharp, in-focus parts of a
photo — handy for a subject against a blurred background; **Sensitivity**
takes in softer parts too. Refine either with Select and Mask.

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
- **Cut** (`Ctrl+X`) copies, then clears the selected pixels. **Copy Merged**
  (`Shift+Ctrl+C`) copies what you see — all visible layers together.
  **Paste in Place** (`Shift+Ctrl+V`) pastes as a new layer exactly where the
  pixels were copied from.

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

More in the **Select** menu:

- **Grow...** adds the touching pixels of similar color (with a tolerance);
  **Similar...** adds every pixel of similar color anywhere on the layer.
- **Load Layer Transparency** selects the active layer's visible pixels — or
  **Ctrl+click** any layer's thumbnail (Ctrl+Shift adds, Ctrl+Alt subtracts).
- **Save Selection...** keeps the selection under a name (saved in the
  project); **Load Selection...** brings it back as a new selection or added
  to / subtracted from / intersected with the current one.

And around the app:

- **Edit → Stroke...** draws a line along the selection's edge — width,
  color, inside / center / outside, opacity.
- **Edit → Fade...** (`Shift+Ctrl+F`), right after a filter, adjustment, fill
  or brush stroke, softens it (opacity) or blends it with another blend mode.
- **Quick Mask** (`Q`, or **Select → Edit in Quick Mask Mode**): the
  selection turns into a red overlay over everything not selected. Paint with
  any brush, eraser, fill or gradient — black adds red (leaves it out), white
  removes it (selects); the layers aren't touched. Press `Q` again to turn it
  back into a selection.
- **Select → Select and Mask...** (`Alt+Ctrl+R`) cleans up the selection's
  edge: **Smooth** rounds off jagged steps and drops specks, **Shift Edge**
  moves the edge out or in, **Feather** softens it; the outline previews live.
- **Select → Subject** selects the photo's main subject by itself: a person,
  an animal, a product. It compares the colors along the photo's edges (the
  background) with those in the middle, and keeps the biggest solid piece. It
  works best when the subject's colors differ from the background's; where
  they are alike (a dark subject on a dark background), part of it may be
  missed. Fix the edges with **Select and Mask** or Quick Selection.
- **Select → Transform Selection...** changes only the selection's outline,
  not the pixels: **Width** and **Height** (percent) and **Angle** work
  around its middle, **Move X / Y** slide it. The outline previews live, and
  any feather is kept.
- **Image → Duplicate** opens a copy of the whole document in a new tab.
- **Image → Content-Aware Scale...** changes the width and/or height (30-200%)
  by taking out — or repeating — the least noticeable paths of pixels (sky,
  walls, grass) instead of squashing everything, so people and objects keep
  their shape. Choose **Protect: Selection** to keep a selected subject
  untouched. All layers change together; one undo step.
- **Image → Crop** crops to the selection. **Image → Trim** removes
  transparent edges, or edges the color of the top-left pixel.
- **Layer → Layer Mask from Selection → Reveal / Hide Selection** makes a
  mask that shows (or hides) just the selected area.

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

### Smart Filters

**Filter → Convert for Smart Filters** turns the active layer into a Smart
Filter layer (an **SF** badge appears in the Layers panel). From then on, any
filter or adjustment you apply to it — the Filter menu, Camera Raw, Blur
Gallery, Brightness/Contrast, Hue/Saturation, Levels, Curves — is kept as an
editable step on top of the untouched original instead of changing its pixels.

**Layer → Smart Filters...** (or double-click the **SF** badge) lists them,
applied top to bottom: untick one to hide it, **Edit...** (or double-click)
reopens its dialog with its settings, **Move Up/Down** changes the order,
**Delete** removes it, **Rasterize Layer** keeps the current look as ordinary
pixels. Every change is one undo step, and Smart Filters are saved in
`.jolie` projects (a PSD export gets the filtered pixels).

A Smart Filter layer can be moved (Move tool), transformed (Free Transform)
and warped: the original changes and the filters run again. Its pixels can't
be painted or liquified (the next filter change would undo that) — rasterize
it first. Its mask can be painted. Rotating, flipping, resizing and cropping
the canvas transform the original too.

### Blur Gallery

**Filter → Blur Gallery → Tilt-Shift...** keeps a band sharp and blurs more and
more above and below it (set its center, height, angle and the transition);
with some extra saturation photos look like miniature models. **Iris
Blur...** keeps an ellipse sharp and blurs around it. Both preview live and
can be recorded in Actions.

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

- **Vanishing Point...** (`Alt+Ctrl+V`) lays a picture onto a flat surface of
  the photo in perspective: a poster on a wall, a screen image on a laptop,
  tiles on a floor. Drag the four blue corners onto the surface's corners
  (the grid shows the perspective). Then pick the **Picture**: the pixels
  you copied, any layer (just its painted part), a file, or the sample
  checker tiles. **Across** / **Down** place it on the surface, **Size** is
  its width as a part of the surface's width, and **Tile** repeats it over
  the whole surface. Nothing goes past the surface's edges. **OK** adds the
  result as a new layer named "Vanishing Point"; set it to **Multiply** to
  let the wall's own light and shade show through.
- **Blur → Gaussian Blur**, **Box Blur**, **Motion Blur** (distance and
  angle), **Radial Blur** (zoom streaks from the center), **Surface Blur**
  (smooths skin and noise but keeps edges sharp), **Lens Blur** (an
  out-of-focus camera look: points become discs, and highlights brighter
  than **Specular Threshold** bloom into bright bokeh circles by **Specular
  Brightness**), **Average** (fills the layer or selection with its
  average color, applied at once), **Spin Blur** (smears the picture round
  its middle, like a spinning wheel; Angle is how far)
- **Distort → Twirl**, **Pinch** (negative values bulge), **Wave**,
  **Ripple** (small pond-like ripples; Size is their wavelength),
  **Spherize** (wraps the image around a ball; negative values pinch it in),
  **Polar Coordinates** (Rectangular to Polar curls the picture into a
  circle, with its top edge in the middle, so a wide panorama becomes a
  "little planet"; Polar to Rectangular unrolls a circle back into a strip),
  **ZigZag** (rings running out from the middle, like a stone dropped in a
  pond; Ridges is how many), **Shear** (each row slid sideways along a
  curve, most at the middle)
- **Other → Custom...** applies your own convolution kernel: rows of numbers
  split by `;` (3×3, 5×5 or 7×7; the middle one is the pixel itself), divided
  by **Scale**, plus **Offset**, previewed live. `0 -1 0; -1 5 -1; 0 -1 0`
  sharpens, `-1 -1 -1; -1 8 -1; -1 -1 -1` finds edges.
- **Distort → Displace...** shifts every pixel by the brightness of a **Map**
  (another layer, or a picture file): middle gray stays, lighter moves right /
  down, darker left / up, by up to the scales in pixels. A blurred gray copy
  of a fabric photo as the map makes text follow its folds.
- **Other → High Pass** (keeps only fine detail on gray; put it on a copy of
  the photo with the Overlay blend mode to sharpen), **Maximum** /
  **Minimum** (light or dark areas spread by the radius: Minimum thickens
  dark lines, Maximum thins them), **Offset** (slides the picture; what
  leaves one edge comes back at the other, so you can see and fix the seams
  of a tile that has to repeat)
- **Sharpen → Sharpen**, **Unsharp Mask** (amount and radius — the usual
  photo sharpening), **Sharpen Edges** (sharpens only the edges, so skin and
  sky don't turn grainy; applied at once), **Smart Sharpen** (sharpens brightness only, so edges
  get no colored halos, and skips differences smaller than film grain or
  sensor noise), **Shake Reduction** (undoes the blur of a camera that moved
  during the shot: set the direction it moved and the length of the streaks;
  **Strength** is how hard it works — too high adds noise)
- **Noise → Add Noise**, **Median** (removes dust, scratches and speckles
  while keeping edges), **Despeckle** (softens grain and specks, keeps
  edges; applied at once), **Reduce Noise** (smooths grain in flat areas;
  Preserve Details keeps fine edges sharp)
- **Pixelate → Mosaic** (big square pixels), **Crystallize** (flat-colored
  cells around scattered points), **Color Halftone** (printed as cyan,
  magenta and yellow dots; Max. Radius is the biggest dot), **Mezzotint**
  (grainy, every pixel fully on or off)
- **Vignette**
- **Stylize → Emboss**, **Find Edges** (Find Edges has no settings and
  applies immediately), **Oil Paint** (turns detail into flat brush-like
  patches; Stylization is the brush size, Detail how many tones are kept),
  **Solarize** (bright tones turned over, like film exposed while
  developing), **Wind** (bright parts streak off to the right; Strength is
  how far), **Diffuse** (pixels scattered a little, like frosted glass),
  **Tiles** (cut into tiles, each nudged; the number is tiles across),
  **Glowing Edges** (edges glow in their own colors on black, like neon),
  **Cutout** (a few flat colors with smooth edges, like colored paper)
- **Render → Fibers** fills the layer with woven-looking vertical fibers from
  the foreground to the background color (Variance: how much they change
  along their length; Strength: how often).
- **Render → Clouds** fills the layer with soft clouds from the foreground
  to the background color (applies at once). **Lens Flare** adds the glow,
  halo, streak and colored reflections of a bright light shining into a
  lens; Center X/Y place the light (percent of the image), with three lens
  types. **Lighting Effects** lights the layer like a lamp in a dark room:
  a **Spot** (oval pool), **Omni** (round) or **Infinite** (even light from
  one side) light with its color, position, size and intensity, over an
  **Ambience** level; **Texture** makes the image's own brightness act as
  relief that catches the light.
- **Lens Correction** (`Shift+Ctrl+R`) fixes lens faults: **Remove
  Distortion** straightens lines bent outward (positive) or inward
  (negative), the two **Fringe** sliders remove red/cyan and blue/yellow
  color edges near the borders, and **Vignette** lightens (positive) or
  darkens (negative) the corners, starting from **Midpoint**.

Lens Correction and Lens Flare, like every filter here, are recorded into
actions and can be Smart Filters.

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
- **Channel Mixer** — builds each output channel (picked in **Output
  Channel**) from the red, green and blue channels plus a constant.
  **Monochrome** uses the red output's mix for all three, giving a custom
  black-and-white conversion.
- **Selective Color** — pick a color range in **Colors** (reds, yellows,
  greens, cyans, blues, magentas, whites, neutrals, blacks) and add or
  remove cyan, magenta, yellow and black ink in just that range. Each range
  keeps its own values while you switch between them. **Relative** (the
  default) changes in proportion to the ink already there, so pure white
  stays white; **Absolute** adds the full amount.
- **Shadows/Highlights** — brightens dark areas and darkens bright ones,
  judged by each pixel's surroundings (**Radius**), so backlit faces and
  blown skies recover without flattening edges. **Tone** sets how far each
  correction reaches into the midtones.
- **Replace Color** — changes hue, saturation and lightness only where the
  image is close to one color (the foreground color at first; click the
  swatch to pick another). **Fuzziness** sets how close.
- **Edit → Sky Replacement...** finds the sky and puts a new one in: Blue
  Sky, Light Clouds, Sunset, Dusk or Overcast. **Shift Edge** grows
  (positive) or shrinks the sky area, **Fade Edge** softens the join, and
  **Foreground Lighting** tints the rest of the photo to match the new sky.
  The sky should touch the top edge of the photo and be fairly plain.
- **Color Lookup** — a ready-made film look: Warm, Cool, Teal & Orange,
  Vintage Fade, Bleach Bypass or Film Noir, with **Intensity** to mix it
  with the original. Also as an adjustment layer.
- **Match Color** — gives the active layer the overall colors and
  brightness of another open document or another layer (**Source**), with
  **Luminance**, **Color Intensity** and **Fade**. **Neutralize** removes a
  color cast (it works without a source too). A recorded action keeps the
  source's colors, so it replays without the source open.
- **Equalize** — spreads the brightness values evenly over the whole range.
  It applies at once.

Exposure through Selective Color are also available as adjustment layers
(**Layer → New Adjustment Layer**). All of these, plus Shadows/Highlights
and Replace Color, are recorded into actions and can be added as Smart
Filters on a Smart Filter layer.

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

### Artboards

An **artboard** is a named area of the document that is exported as its
own picture. You can design an app icon in several sizes, a phone screen
and a banner side by side in one document, then export them all at once.

- **Image → Artboards → New Artboard from Selection...** turns the
  selected area (draw it with the Rectangular Marquee) into an artboard.
- **Image → Artboards → New Artboard...** makes one of a common size (app
  icon, phone, tablet, web page, Instagram post or story, YouTube thumbnail,
  banner, favicon) or of your own size (**Custom**). **X** and **Y** place
  its top-left corner. A part that would stick out past the canvas is cut
  off, so make the canvas big enough first (**Image → Canvas Size**).
- Each artboard shows on the canvas as an orange frame, with its name and
  size above it.
- With the **Move** tool, drag an artboard by its name label to move it
  (it stays on the canvas; one undo step).
- **Delete Artboard...** and **Clear Artboards** remove them. Adding and
  removing artboards can be undone.
- **File → Export Artboards to Files...** (also at the bottom of the
  Artboards menu) writes one PNG per artboard, named after it, with
  everything visible inside its frame.

Artboards are saved with the project. They stay on the same part of the
picture when you crop, rotate, flip or resize the canvas or the image.

### Mode and Apply Image

**Image → Mode** converts the whole document (one undo step each). The
conversion repaints the plain pixel layers. Text, shape, fill, adjustment
and Smart Object layers keep their own settings (Grayscale also turns text
gray):

- **Grayscale** — every color becomes its gray by brightness.
- **Indexed Color...** — the image drawn with only **Colors** (2–256)
  colors, one palette for the whole image, like a GIF. **Dither: Diffusion**
  mixes palette colors in fine dot patterns so gradients still look smooth;
  **None** gives flat bands.
- **Bitmap...** — pure black and white. **Diffusion Dither** shows gray
  tones as dot patterns; **50% Threshold** turns everything lighter than
  middle gray white and the rest black.

These are one-time conversions: you can keep painting in color afterwards.

**Image → Calculations...** blends one channel (gray, red, green, blue or
transparency — of any layer or of the merged image, optionally inverted) with
another into a gray mask, using a blending mode and opacity. **Result** puts
it to use: a **New Layer**, a **Selection** (the lighter half) or the active
layer's **Layer Mask**. A classic use: the Blue channel multiplied with itself
gives a strong mask of a bright sky, for selecting around hair or trees.

**Layer → Layer Mask from Transparency** gives the active layer a mask that
shows it where it's painted and hides it where it's see-through, so you can
then soften or extend the edge on the mask instead of the pixels.

**Image → Apply Image...** blends an image onto the active layer:
**Source** is a layer or **Merged** (everything visible), **Channel** is
its full color or just its Red, Green or Blue channel (as gray), **Invert**
turns it negative first, and **Blending** (Multiply, Screen, Overlay, Add,
Subtract, Darken, Lighten, Difference or Normal) and **Opacity** set how it
combines. The active layer's see-through parts stay see-through. A common
use: Apply Image with the Blue channel, Multiply, onto a copy of a portrait
to build a mask that darkens skin blemishes.

## Smart Objects

**Place Smart Object...** places an image file as its own layer type that
stays linked to the original file: scaling or rotating it later always
re-samples from that source rather than degrading a raster copy, exactly
like a non-destructive Smart Object layer in other editors. Double-clicking
a Smart Object layer opens its embedded content in its own document tab for
editing.

**Layer → Smart Objects** has:

- **Convert to Smart Object** — the active pixel layer's painted part
  becomes a Smart Object in the same place, keeping its opacity and blend
  mode. Scaling it down and back up later doesn't lose quality.
- **Edit Contents** — the same as double-clicking the layer.
- **Replace Contents...** — a new picture from a file takes the old one's
  place, size and angle. It keeps its own shape and is fitted inside the old
  box, centered. Handy for mock-ups: swap the screenshot on a phone or the
  photo in a frame.
- **Export Contents...** — saves the Smart Object's original picture, at its
  full size, as a PNG.

Converting and replacing are one undo step each.

## Import & Export

- **Import...** adds one or more image files (PNG/JPG/BMP/WebP) as new
  layers — or simply drag image files onto the canvas.
- **Export...** writes either the flattened composite or a single selected
  layer (masked, if it has one) to PNG, JPEG, or WebP. A **Quality** slider
  applies to the two lossy formats; it's disabled (not hidden) when PNG is
  selected, since PNG is always lossless.
- **Print...** (`Ctrl+P`) prints the flattened image centered on the page:
  **Fit to Page**, or at a **Print Resolution** in pixels per inch (300 ppi
  is photo quality; it shrinks to fit if larger than the page).
- **Export As...** (`Alt+Shift+Ctrl+W`) saves the flattened image in one
  format at several sizes at once: list them in **Sizes**, e.g.
  `100%, 50%, 2x`. The extra sizes get a suffix (`photo@2x.png`,
  `photo@50pct.png`). **Trim transparent edges** crops away empty borders
  first.

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

### Several shots of one scene

Import the shots as layers (File → Import), then:

- **Edit → Auto-Align Layers** shifts each visible layer so it lines up with
  the bottom one — for shots taken by hand that moved a little. It works
  even when the shots are exposed differently. Shifts only (no rotation).
  One undo step.
- **File → Automate → Photomerge...** stitches overlapping photos into a
  panorama: choose the files (they are taken in name order, each overlapping
  the one before by at least about 15%) and the result opens as a new
  document, blended smoothly where the shots overlap. Shots are placed by
  shifting, so it works best for a camera turned on a tripod or flat
  subjects (documents, walls); transparent edges are left where the shots
  don't line up — crop them off.
- **File → Automate → Merge to HDR Pro (visible layers)** blends bracketed
  exposures (a dark, a normal and a bright shot) into a new **Merged HDR**
  layer that keeps detail in both the shadows and the highlights (exposure
  fusion). The source layers are hidden. Align them first if needed.

### Batch processing

**File → Automate → Batch...** plays an action on every image (PNG, JPEG,
WebP, BMP, GIF, PSD or .jolie project) in a folder — optionally its subfolders too — and saves the
flattened results to an output folder as PNG, JPEG or WebP, with an optional
name suffix (e.g. `_edited`). The files are processed without opening tabs;
**Stop** ends the run after the current file. If a step can't run on some
file, that file is skipped and reported. Saving into the source folder
requires a suffix, so originals are never overwritten.

## Animation

Animation is made in **Clipbar Animation** projects (File → New → Clipbar
Animation): frame by frame (each frame on its own layer, with onion skin),
with keyframes (move, scale, turn and fade a layer or group), with sound,
exported as GIF, MP4, PNG files or a sprite sheet. Everything is in the
**Animation** menu; **Shift+Ctrl+T** shows or hides the Timeline under the
canvas. To animate an ordinary picture, use **Animation → Convert to
Clipbar Animation...**, which opens a copy of it as an animation project. See [ClipbarWorkflow.md](ClipbarWorkflow.md) for
the full guide.

## Navigator, Info, Histogram, Swatches, Channels and Paths

The panel above the layers has seven tabs: **PROPERTIES** (the active tool's
options), **NAVIGATOR**, **INFO**, **HISTOGRAM**, **SWATCHES**,
**CHANNELS** and **PATHS**.

- **Navigator** shows the whole image with a red frame around the part the
  canvas shows. Click or drag in the thumbnail to move the view there; the
  **Zoom** slider zooms around the view's center.
- **Info** shows the color under the pointer (R, G, B, A and hex, with a
  swatch), the pointer's position in image pixels, the document size and the
  selection's size and position.
- **Histogram** plots the brightness of the visible image (white line) over the
  red, green and blue channels, with the mean, standard deviation, median and
  pixel count. Transparent pixels are left out.
- **Swatches** is a palette of colors you keep. Click a swatch to make it
  the foreground color, Ctrl+click to make it the background color, and
  right-click to remove it. **+ Add Foreground** adds the current
  foreground color. **Reset** brings back the starter colors. The swatches
  are the same in every document and are kept when JolieCat is closed.
- **Channels** lists **RGB**, **Red**, **Green** and **Blue**. Click Red,
  Green or Blue (or press **Ctrl+3 / Ctrl+4 / Ctrl+5**) to see only that
  channel on the canvas, as gray: handy for judging contrast or choosing
  the channel to make a selection from. **RGB** (**Ctrl+2**) shows the full
  image again. Only the view changes; painting and filters still work on
  all the colors. Below them are the document's saved selections (**Select
  → Save Selection**): click one to load it, or ✕ to delete it.
- **Paths** keeps Pen tool paths. Draw one with the Pen tool and click **Save
  Current Path** ("Path 1", "Path 2"...). Click a saved path to make it the
  Pen's working path again — every point and curve handle is kept, so it can
  still be edited — then **Make Selection**, **Fill** or **Stroke** it. ✕
  deletes a path. Saved with the project.

These panels update a few times a second while they are shown, and only
recompute the image after it changes.

## Undo / Redo

**Layer Comps:** also in the **HISTORY** tab, **+** remembers which layers
are visible and each one's opacity, blend mode and layer style (not the
pixels), as "Layer Comp 1", "2", ... Click one to show the layers that way
again (one undo step) — handy for keeping several versions of a design in
one file. Layers added later keep their current look. Comps last for the
session.

**Snapshots:** in the **HISTORY** tab, the camera button saves the whole
document as it is now ("Snapshot 1", ...). Click a snapshot later to go back
to that state — however many steps ago — as one step you can undo again.
Snapshots last for the session (they aren't saved in the file).

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
| `Ctrl+X` / `Shift+Ctrl+C` / `Shift+Ctrl+V` | Cut / Copy Merged / Paste in Place |
| `Shift+Ctrl+F` | Fade |
| `Q` | Quick Mask on / off |
| `Alt+Ctrl+R` | Select and Mask |
| `Ctrl+R` / `Ctrl+'` / `Ctrl+;` | Rulers / Grid / Guides |
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
| `Shift+Ctrl+[` / `Shift+Ctrl+]` | Send to back / Bring to front |
| `Ctrl+L` / `Ctrl+M` / `Ctrl+U` | Levels / Curves / Hue-Saturation |
| `Ctrl+I` / `Ctrl+Shift+U` | Invert / Desaturate |
| `Ctrl+Alt+I` / `Ctrl+Alt+C` | Image size / Canvas size |
| `Ctrl+Alt+Shift+L` / `Ctrl+Shift+B` | Auto Contrast / Auto Color |
| `Ctrl+0` / `Ctrl+1` / `Ctrl++` / `Ctrl+-` | Fit / 100% / Zoom in / Zoom out |
| `Ctrl+2` / `Ctrl+3` / `Ctrl+4` / `Ctrl+5` | View all channels / Red / Green / Blue only |
| `Space`+drag, middle mouse | Pan |
| `[` / `]` | Smaller / bigger brush |
| Tool letters | V, M, L, W, C, I, B, N, E, G, S, J, O, R, P, A, T, U, H, Z |

## If Something Goes Wrong

If a command runs into an error, JolieCat shows a message instead of closing,
and your documents stay open: save your work (**File → Save As**) to be safe.
The details are written to `%LocalAppData%\JolieCat\crash.log`; that file
helps when reporting the problem.

## Project Files

JolieCat projects are saved as `.jolie` files (filter: *JolieCat Project
(\*.jolie)*), holding every layer's pixels and metadata, mask data, the
project type, and — for a Clipbar Animation project — the Timeline's tracks,
clips, and keyframes. A `.jolie` file is self-contained; reopening it
restores the full editing state, including which layer/mask was active.
