# GrayCat Studio

Visual, block-based builder for websites and mobile apps. Design a page from blocks in the
desktop editor and generate a ready-to-run project.

## Projects

| Project | What it is |
|---|---|
| `GrayCat.UI` | WPF editor (.NET 8). Start here. |
| `GrayCat.Core` | Models, validation and the code generator (`Generation/`). |
| `GrayCat.Service` | Optional background service on `http://localhost:5123`. It runs the same generator; the editor uses it when it is running and generates in-process otherwise. |
| `GrayCat.Tests` | xUnit tests for the generator, validation and project files. |

`GrayCatUI`, `GrayCatService` and `GrayCat.Shared` are the earlier .NET 6 prototype. They are not part of the solution.

## Run

```bash
dotnet run --project GrayCat.UI            # the editor
dotnet run --project GrayCat.Service       # optional service
dotnet test                                # tests
```

`GrayCat.UI.exe path\to\MySite.graycat` opens a project directly; **Help → Open .graycat Files with
GrayCat Studio** makes double-clicking `.graycat` files open them (registered for the current user only).

## Project files (.graycat)

A project that has not been built is a single `.graycat` file: a package (ZIP) with `graycat.json`
(format version), `project.json` (pages, blocks, theme, settings) and `assets/` (images and videos added
in the editor). It can be copied or sent like any document.

```
Documents\GrayCatProjects\
  MySite.graycat     ← the project
  MySite\            ← generated code, created only by Generate Code / Run App / Build for Production
```

- While a project is open it is unpacked to `%LOCALAPPDATA%\CatSuite\GrayCat\Work\`; every save
  (automatic, 1.5 s after the last change) writes the `.graycat` file again, replacing it only once the
  new file is complete.
- The code folder contains a `.graycat-output` marker. GrayCat regenerates only into folders that have it,
  so it never overwrites a folder it did not create. Generated files should not be edited by hand.
- Projects from GrayCat 1.0 (`MySite\graycat.project.json`) still open: they are saved as `MySite.graycat`
  next to the old folder, which stays the code folder. The old file is not deleted.

### Installing the service (optional)

From PowerShell started as Administrator:

```powershell
GrayCat.Service\install-service.ps1             # publish, register and start
GrayCat.Service\install-service.ps1 -Uninstall  # remove
```

The service runs as `LocalService` (low rights) and only listens on `localhost`. If it cannot write to
a folder, the editor generates the project itself. Starting a second copy, or starting it while
another program uses port 5123, ends with a clear message instead of a crash.

## Using the editor

1. **File → New Project**: choose a name, folder, project type and a starting template
   (landing page, 4-page business site, portfolio, shop or blank).
2. Click a block in the library to add it after the selected block (or at the end).
   The **Design** view shows the real page; click any part of it to select that block.
   The **Layout** view lets you drag blocks around; their top-to-bottom order is the page order.
3. Select a block to edit it on the right. Changes are saved automatically to the `.graycat` file.
   **Browse…** next to image and video fields copies a local file into the project's `assets/` folder;
   generated projects publish it where each type serves files (`public/assets` for React, Next.js
   and Node, bundled with `require()` for React Native).
   Undo/redo with Ctrl+Z / Ctrl+Y; drag the blue corner of a selected block to resize it;
   **Project → Settings and Theme** changes the site name, author, description, project type,
   main/accent colours and font (system fonts or Google Fonts).
   Each block also has a **section id** (menu links like `#contact` scroll to it) and a
   **Hide on the published site** switch.
6. **Pages**: the toolbar's page selector switches the page being edited; **Pages…** adds, renames,
   reorders and deletes pages. New pages start with the home page's navigation bar and footer, and
   navigation bars list all pages automatically (with the current one highlighted). Each project type
   gets real pages: `about.html` for static sites and Node, `/about` routes in React and Next.js,
   screens in React Native.
7. **File → Save Project As** (Ctrl+Shift+S) saves a copy under another name and keeps editing the copy
   (its generated code goes to a folder named after the new file). **File → Export Project as ZIP** packs
   the project without `node_modules` and build output.
8. The **Device** selector above the Design view shows the page at tablet (820 px) or phone (390 px) width.
9. **Project → Settings and Theme** also sets the site language (`<html lang>`) and the browser-tab icon.
10. **Project → Build for Production** creates the version to publish: `dist/` for React and React Native
    (web export), `.next/` for Next.js (`npm start` to serve). Static sites and Node projects need no build.

## Building a release

```powershell
.\publish.ps1        # runs the tests, then writes artifacts\GrayCatStudio-<version>-win-x64.zip
```

The package is self-contained (the target PC does not need .NET): `Studio\GrayCat.UI.exe` is the editor,
`Service\` holds the optional service and `install-service.ps1`. Node.js is still needed on the target PC to run
React, Next.js, Node and Expo projects. The version comes from `<Version>` in `GrayCat.UI.csproj`.

## Look and feel

The editor's style follows the other Cat apps (AsmodayCat, BlackCat): a palette in
`GrayCat.UI/Themes/GrayCatTheme.xaml` taken from the logo (warm cat greys, the brown outline as the main
accent, green eyes for "Run"), and an own title bar for every window (`Controls/ThemedWindow.cs`:
logo, title, menu, caption buttons, snapping and a maximized size that stops at the taskbar).
Logo files are in `GrayCat.UI/Assets` (`GrayCatLogo.ico` is the app icon, `GrayCatMark.png` the cropped mark).

### Interface language

The editor speaks Ukrainian and English (switch in the status bar; the choice is kept in
`%APPDATA%\CatSuite\GrayCat\settings.json`, the first run follows the Windows language).
The structure follows AsmodayCat: `Localization/Loc.cs`, `{l:Tr '…'}` in XAML, `Loc.T` / `Loc.F` in code.
Keys are the English texts, so anything untranslated shows in English. Block type names use
`Loc.Block`, combo-box values `Loc.Value` (the stored values stay unchanged). All texts are in
`Localization/Strings.cs`; `LocalizationTests` checks that translations keep the same `{0}` placeholders.
Message boxes use `Views/ThemedMessageBox.cs` (same calls as `MessageBox.Show`).
The main window's size, position and maximized state are kept in the same `settings.json`.

### Site content language

The site language (Project → Settings) also chooses the language of starter texts: a new project gets the
interface language, and its template, new blocks and form messages ("Sending...", "… is required",
the footer copyright) are written in it. The texts are in `GrayCat.Core/Generation/ContentText.cs`;
links, image paths and other non-text values are never translated, and anything not in the table stays English.

To review the windows without opening them on screen, set `GRAYCAT_SNAPSHOT_DIR` and run
`dotnet test GrayCat.UI.Tests --filter RenderWindows`: the windows are rendered off-screen to PNG files.

## Tests

`GrayCat.Tests` covers the generator, rendering, pages, themes and validation;
`GrayCat.UI.Tests` drives the editor's view model (pages, undo/redo, saving, generation) without a window.
4. **Run App** (F5) runs the real project: it writes the code, runs `npm install` the first time,
   starts the dev server (Vite, Next.js, Express or Expo web) and opens it in the browser.
   While it runs, every edit is written to the project and the app reloads by itself.
   **Stop** (Shift+F5) ends it; closing the editor also stops it. Output is in **Run Log**.
   Static HTML projects simply open `index.html`. Node.js must be installed for the other types.
5. **Generate Code** (Ctrl+G) only writes the project files. **Static HTML Preview** shows the
   page without Node.js, and **Export Static Site** writes `index.html` / `styles.css` / `script.js` anywhere.

## Generated project types

| Type | Output | Run |
|---|---|---|
| `StaticHtml` | HTML + CSS + JS | open `index.html` |
| `React` | Vite + React 18 | `npm install`, `npm run dev` |
| `NextJs` | Next.js 14 (pages router) with `/api/[form]` storing form submissions | `npm install`, `npm run dev` |
| `NodeJsApi` | Express server: serves the site, `POST /api/:form` stores submissions (JSON file or SQLite) | `npm install`, `npm start` |
| `ReactNative` | Expo SDK 58 app | `npm install`, `npx expo start` |

All web outputs share one stylesheet and the same markup, so the preview, the static export and
the React/Next.js apps look the same. Page content is written to `site.json` and drawn by the
block components, so it can be edited without touching the components.
