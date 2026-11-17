# JolieCat

A raster image editor for Windows with layers, filters, frame-by-frame and keyframe
animation, and sprite sheets for games. The interface is in English, Ukrainian or Russian.

**Version 4.0.0** — see [CHANGELOG.md](CHANGELOG.md).

## Download and run

1. Download `JolieCat-4.0.0-win-x64.zip` and unpack it anywhere (no installer needed).
2. Run `JolieCat.exe`.

On the first run Windows SmartScreen may say the app is from an unknown publisher (the
exe isn't code-signed): choose **More info → Run anyway**.

**Requirements:** 64-bit Windows 10 (version 2004 or newer) or Windows 11. .NET doesn't
need to be installed: it's inside the exe.

## What it does

- **Editor** — layers and groups, masks and clipping masks, blend modes, layer styles,
  adjustment and fill layers, Smart Objects and Smart Filters, text and type on a path,
  the Pen tool and saved paths, selection tools (Magic Wand, Quick Selection, Select
  Subject, Select Sky, Color Range, Select and Mask), retouching (Healing, Patch,
  Content-Aware Fill / Move / Scale, Clone Stamp, Red Eye), Liquify, Puppet Warp,
  Camera Raw, Blur Gallery, dozens of filters and adjustments, artboards, Actions and
  Batch, Photomerge and HDR.
- **Clipbar Animation** — a timeline with frames, onion skin, keyframes with easing,
  motion paths, bones, a camera, sound, and GIF / APNG / MP4 / PNG / sprite sheet export.
- **Sprite Sheets** — a slicing grid, pivots, row names, packed atlases, import of
  TexturePacker / Aseprite atlases and export for game engines (Phaser, PixiJS, Godot,
  Unity).
- **Files** — the `.jolie` project format, Photoshop `.psd` in and out, PNG, JPEG, WebP,
  BMP, GIF.

The full guide: [docs/UserGuide.md](docs/UserGuide.md); animation:
[docs/ClipbarWorkflow.md](docs/ClipbarWorkflow.md).

## Where it keeps things

- Settings (language, undo steps, rulers...): `%APPDATA%\JolieCat\preferences.json`
- Recovery copies of unsaved documents: `%LOCALAPPDATA%\JolieCat\Recovery`

Deleting the `JolieCat` folders resets the app to its first-run state.

## Building from source

Needs the .NET 8 SDK (or newer) on Windows.

```powershell
dotnet build JolieCat.slnx                 # build
dotnet test JolieCat.slnx                  # run the tests
powershell -File build-release.ps1         # tests + artifacts\JolieCat-<version>-win-x64.zip
```

The version number lives in `Directory.Build.props`.

---

## Українською

JolieCat — растровий редактор для Windows із шарами, фільтрами, покадровою й ключовою
анімацією та спрайт-аркушами для ігор. Розпакуйте архів і запустіть `JolieCat.exe` —
встановлювати нічого не треба (потрібна 64-бітна Windows 10 2004+ або Windows 11).
Мова інтерфейсу (English / Українська / Русский) — перемикач у рядку меню праворуч
або **Редагування → Налаштування → Мова**.
