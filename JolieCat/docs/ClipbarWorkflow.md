# Sprite Sheet & Clipbar Animation Workflow Guide

JolieCat has two project types built specifically around sprite-based
animation, on top of the general editor described in
[`UserGuide.md`](UserGuide.md):

- **Type 2 — Sprite Sheet**: a canvas with a configurable slicing grid
  overlay, for laying out and exporting individual frames from one sheet.
- **Type 3 — Clipbar Animation**: a dedicated Timeline workspace — tracks,
  clips, keyframes, a scrubbable playhead, and playback transport — for
  frame-by-frame animation.

The two are designed to connect directly: build and lay out your frames as a
Sprite Sheet, then **derive** a Clipbar Animation project from it with one
command. Each grid cell becomes its own frame layer and its own Timeline
clip automatically — you don't re-import or re-slice anything by hand.

## Part 1 — Sprite Sheet (Type 2)

### Creating a Sprite Sheet project

**File → New** (`Ctrl+N`) → choose **Sprite Sheet** in the New Project
dialog → **Create**. This opens a normal canvas plus:

- A slicing **grid overlay** drawn over the canvas.
- A **Sprite Sheet Grid** panel (in the right-hand Properties/Layers
  column), for configuring that grid.
- Marquee selection tools snap to the grid's lines automatically when
  **Snap Marquee to Grid** is checked, so hand-placed selections land
  exactly on cell boundaries.

Build your sheet like any other document — add layers, paint, import
reference images, etc. — using the grid overlay to see where each frame's
boundaries fall.

### Configuring the grid

The Sprite Sheet Grid panel has six settings, all live-updating the overlay
immediately:

| Setting | What it controls |
|---|---|
| **Columns** | Number of cells across (1–64). |
| **Rows** | Number of cells down (1–64). |
| **Padding X / Padding Y** | Spacing between adjacent cells, in document pixels. |
| **Margin X / Margin Y** | Border around the whole grid before the first row/column starts, in document pixels. |
| **Snap Marquee to Grid** | When checked, Marquee selections snap to the nearest grid line on each axis. |

Cell size is always *derived* from these settings plus the document's own
width/height — you never set a cell's size directly, which keeps the column/
row count, padding, and margin from ever disagreeing with the actual canvas
dimensions. Cells are numbered **row-major**: row 0's columns left-to-right,
then row 1's, and so on — this is the exact order both exporting and
deriving a Clipbar Animation use.

If you need a different overall canvas size once the grid layout is set,
use **Resize Canvas...** (see `UserGuide.md`) — the grid recalculates
against the new dimensions automatically.

### Slicing & exporting frames

**Slice & Export Frames...** (in the Sprite Sheet Grid panel) flattens the
document and writes one image file per grid cell:

1. Click the button — a folder-picker dialog opens.
2. Choose a destination folder.
3. Every cell is cropped from the flattened composite and written as its own
   file, named `<document title>_<index>.<ext>` — zero-padded to three
   digits, e.g. `Untitled 1_000.png`, `Untitled 1_001.png`, `Untitled
   1_002.png`, ... — in the same row-major order the grid itself uses.
   The format is always PNG.
4. A cell whose configured rectangle has collapsed to nothing (e.g. margins/
   padding too large for the grid) is skipped — but its index is still
   consumed, so a later cell's number always matches its actual position in
   the grid rather than shifting down to fill the gap.
5. The status bar confirms how many frames were written.

This is a one-off export for use outside JolieCat (a game engine's own
import pipeline, sharing a set of individual frame images, etc.) — it
doesn't affect the Sprite Sheet document itself and doesn't require or
produce a Clipbar Animation project.

## Part 2 — Deriving a Clipbar Animation (Type 2 → Type 3)

### Running the derivation

Once your Sprite Sheet's grid and content are laid out the way you want,
**Derive Clipbar Animation...** (right below **Slice & Export Frames...** in
the same panel) builds a complete Clipbar Animation project from it in one
step:

1. Click **Derive Clipbar Animation...**.
2. A brand new document tab opens, titled `<source title> (Clipbar)`,
   already active.

No dialog or extra input is needed — the conversion uses the source
document's current grid settings and flattened content directly. Under the
hood, this is what happens:

- The source Sprite Sheet's flattened composite is sliced into the grid's
  cells, in the same row-major order slicing-to-files uses.
- **Each cell becomes its own layer** in the new document, sized to exactly
  one grid cell, named `Frame 000`, `Frame 001`, `Frame 002`, ... in cell
  order. Only `Frame 000` starts visible — every later frame layer starts
  hidden, so the new project opens showing just its first frame rather than
  every frame's pixels stacked on top of one another.
- **One Timeline track named "Frames"** is created, with one clip per frame
  layer — `Frame 000` occupying frame 0, `Frame 001` occupying frame 1, and
  so on, each one frame long — positioned sequentially and already wired so
  each clip drives its own matching frame layer's visibility (see
  [Frame-layer playback](#frame-layer-playback-how-clips-drive-visibility)
  below).
- The new project's frame rate is carried over from whatever frame rate the
  source document's Timeline was already set to (24 fps by default, if
  never changed).
- The Sprite Sheet's grid settings themselves are copied into the new
  document only for provenance — they're not shown or editable there, since
  a Clipbar Animation project's canvas is a single frame's size, not a
  multi-cell sheet.
- A degenerate cell (collapsed to nothing) is skipped exactly like the
  file-slicing export skips it, and still counts toward the frame numbering.

The source Sprite Sheet document is left completely untouched — deriving a
Clipbar Animation opens a new, independent tab rather than converting the
one you ran the command from.

## Part 3 — The Clipbar Animation Workspace (Type 3)

You can also create a Clipbar Animation project directly (**File → New** →
**Clipbar Animation**) rather than deriving one, if you're building
frame-by-frame animation from scratch instead of from a pre-built sheet.

A Clipbar Animation document's center workspace is the dedicated **Timeline**
tab instead of the plain canvas (an existing document can switch between the
two via the Design/Timeline workspace-mode toggle shown for this project
type). It has two parts:

### Playback transport bar

Along the top:

| Control | Action |
|---|---|
| ⏮ | Go to start (frame 0) |
| ⏪ | Previous frame |
| ▶ / ⏸ | Play / Pause — auto-advances the playhead at the current frame rate, looping back to frame 0 once it passes the last frame |
| ⏩ | Next frame |
| ⏭ | Go to end (last frame) |
| **Total Frames** | The animation's total length, in frames (1–2000) |
| **Frame Rate** | Playback speed, in frames per second (1–120) |

Stepping or jumping the playhead always pauses playback first, so a scrub
gesture never fights an in-progress auto-advance.

### Tracks, clips, and keyframes

Below the transport bar, the timeline itself:

- **+ Track** (toolbar button) adds a new, empty track, named
  `Track <n>`.
- Each track row has its own **+K** (add keyframe at playhead) and **+C**
  (add clip at playhead, 24 frames long) buttons.
- **Clips** are the colored bars in a track's lane — drag a clip's body to
  move it, or drag either edge to trim its length.
- **Keyframes** are the diamond markers on a track's lane, placed at the
  playhead's current position.
- The **playhead** (the vertical line over the ruler) can be dragged
  directly to scrub through the animation.
- The frame ruler and every track lane share the same horizontal frame-to-
  pixel scale, so everything lines up regardless of how far you've scrolled.

### Frame-layer playback (how clips drive visibility)

A clip derived from a Sprite Sheet (see Part 2) is wired to its own frame
layer by name. As the playhead moves, JolieCat shows exactly the layer(s)
whose clip currently contains the playhead's frame, and hides every other
wired layer — a "flipbook" effect, not general per-property interpolation.
Reopening a saved Clipbar Animation project re-establishes these same
layer/clip connections automatically.

Tracks and clips you add yourself (via **+ Track**/**+C**/**+K**, not
derived from a Sprite Sheet) aren't wired to any layer, so moving their
clips or adding keyframes on them doesn't yet drive any visible effect on
its own — this foundation is what frame-by-frame and future skeletal/
transform-based animation will build on, but no per-frame property (opacity,
transform, etc.) interpolates from a keyframe today. The derived "Frames"
track described in Part 2 is the one concrete, fully working animation
pipeline currently available end-to-end.

### Saving and exporting

- **Save** (`Ctrl+S`) writes the Timeline's tracks, clips, and keyframe
  positions into the `.jolie` file along with every layer, exactly like any
  other project type.
- To get individual frame images out for a game engine or external tool,
  use **Export...** for a single frame's flattened composite at a time, or
  build the animation as a Sprite Sheet first and use **Slice & Export
  Frames...** (Part 1) to get every frame as a numbered image file in one
  step.
