namespace GrayCat.Core.Generation;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrayCat.Core.Models;
using GrayCat.Core.Models.Generation;
using GrayCat.Core.Serialization;
using GrayCat.Shared.Models;

/// <summary>Builds the full file set (relative path → content) for each project type. Pure: no disk access.</summary>
public static class ProjectScaffolder
{
    /// <summary>Project file of GrayCat 1.0 (kept inside the code folder). Still opened and converted to .graycat.</summary>
    public const string ProjectFileName = "graycat.project.json";

    /// <summary>Written into every generated code folder; lets GrayCat regenerate there safely.</summary>
    public const string OutputMarker = ".graycat-output";

    public const string ViteVersion = "^6.0.0";
    public const string VitePluginReactVersion = "^4.3.4";
    public const string WebReactVersion = "^18.3.1";
    public const string NextVersion = "14.2.35";
    public const string ExpressVersion = "^4.21.2";
    public const string CorsVersion = "^2.8.5";
    public const string Sqlite3Version = "^5.1.7";

    /// <param name="assetFiles">"assets/..." files present in the project (needed by React Native).</param>
    public static Dictionary<string, string> Build(ProjectModel project, GenerationRequest request, IReadOnlyCollection<string>? assetFiles = null)
    {
        var site = SiteBuilder.Build(project, new SiteBuildOptions
        {
            Links = request.Type == ProjectType.StaticHtml ? PageLinkStyle.Files : PageLinkStyle.Paths
        });
        var siteJson = JsonSerializer.Serialize(site, GrayCatJson.Web);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OutputMarker] = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["generatedBy"] = "GrayCat Studio",
                ["project"] = project.ProjectName,
                ["type"] = request.Type.ToString(),
                ["note"] = "Generated code. Edit the .graycat project in GrayCat Studio and generate again; changes made here to generated files are overwritten."
            }, GrayCatJson.Web)
        };

        switch (request.Type)
        {
            case ProjectType.StaticHtml:
                AddStatic(files, site, "");
                files["README.md"] = Readme(site, "Static website", """
Open `index.html` in a browser. No build step is needed.

To publish, upload the `.html` files, `styles.css`, `script.js` and the `assets` folder to any static host
(GitHub Pages, Netlify, Cloudflare Pages, a plain web server...).

Forms send JSON to the URL set in GrayCat (for example `/api/contact`).
They need a backend at that address: the GrayCat **NodeJsApi** project type provides one.
""");
                break;

            case ProjectType.React:
                AddReact(files, project, site, siteJson);
                break;

            case ProjectType.NextJs:
                AddNext(files, project, site, siteJson);
                break;

            case ProjectType.NodeJsApi:
                AddNode(files, project, site, siteJson, request.UseSQLite);
                break;

            case ProjectType.ReactNative:
                AddReactNative(files, project, site, siteJson, assetFiles ?? Array.Empty<string>());
                break;

            default:
                throw new NotSupportedException($"Project type {request.Type} is not supported.");
        }

        if (request.Type != ProjectType.StaticHtml)
            files[".gitignore"] = GitIgnore(request.Type);

        // Normalise line endings so generated files look the same on every machine.
        foreach (var key in files.Keys.ToList())
            files[key] = files[key].Replace("\r\n", "\n");

        return files;
    }

    /// <summary>Self-contained home page (CSS and JS inline).</summary>
    /// <param name="projectDirectory">When set, "assets/..." paths point into this folder.</param>
    public static string BuildPreviewHtml(ProjectModel project, string? projectDirectory = null)
    {
        var site = SiteBuilder.Build(project, PreviewOptions(projectDirectory, editor: false));
        return HtmlRenderer.RenderPage(site, site.Pages[0], inlineAssets: true);
    }

    /// <summary>Every page as a self-contained file (index.html, about.html...), linked to each other.</summary>
    public static Dictionary<string, string> BuildPreviewSite(ProjectModel project, string? projectDirectory)
    {
        var site = SiteBuilder.Build(project, PreviewOptions(projectDirectory, editor: false));
        return site.Pages.ToDictionary(p => p.File, p => HtmlRenderer.RenderPage(site, p, inlineAssets: true));
    }

    /// <summary>Page shown inside the editor: click-to-select markers, hidden blocks dimmed.</summary>
    public static string BuildEditorHtml(ProjectModel project, string? projectDirectory, string pageSlug = "")
    {
        var site = SiteBuilder.Build(project, PreviewOptions(projectDirectory, editor: true));
        return HtmlRenderer.RenderPage(site, PageOrHome(site, pageSlug), inlineAssets: true, editor: true);
    }

    /// <summary>Just the blocks of one page, for refreshing the editor page in place.</summary>
    public static string BuildEditorBody(ProjectModel project, string? projectDirectory = null, string pageSlug = "")
    {
        var site = SiteBuilder.Build(project, PreviewOptions(projectDirectory, editor: true));
        return HtmlRenderer.RenderBlocks(PageOrHome(site, pageSlug), editor: true);
    }

    private static SitePage PageOrHome(SiteModel site, string slug) =>
        site.Pages.FirstOrDefault(p => p.Slug.Equals(slug ?? "", StringComparison.OrdinalIgnoreCase)) ?? site.Pages[0];

    private static SiteBuildOptions PreviewOptions(string? projectDirectory, bool editor) => new()
    {
        Links = PageLinkStyle.Files,
        IncludeHidden = editor,
        AssetBase = projectDirectory == null
            ? null
            : new Uri(Path.TrimEndingDirectorySeparator(projectDirectory) + Path.DirectorySeparatorChar).AbsoluteUri
    };

    /// <summary>Static site files (one .html per page, styles.css, script.js) for export.</summary>
    public static Dictionary<string, string> BuildStaticSite(ProjectModel project)
    {
        var files = new Dictionary<string, string>();
        AddStatic(files, SiteBuilder.Build(project, new SiteBuildOptions { Links = PageLinkStyle.Files }), "");
        return files;
    }

    public static string PackageName(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-', '.', '_');
        if (slug.Length > 100) slug = slug[..100].Trim('-');
        return slug.Length == 0 ? "graycat-site" : slug;
    }

    // ---------- Static ----------

    private static void AddStatic(Dictionary<string, string> files, SiteModel site, string prefix)
    {
        foreach (var page in site.Pages)
            files[prefix + page.File] = HtmlRenderer.RenderPage(site, page, inlineAssets: false);
        files[prefix + "styles.css"] = SiteAssets.Css.TrimStart();
        files[prefix + "script.js"] = SiteAssets.FormScript.TrimStart();
    }

    // ---------- React (Vite) ----------

    private static void AddReact(Dictionary<string, string> files, ProjectModel project, SiteModel site, string siteJson)
    {
        files["package.json"] = PackageJson(new Dictionary<string, object?>
        {
            ["name"] = PackageName(project.ProjectName),
            ["private"] = true,
            ["version"] = "0.1.0",
            ["type"] = "module",
            ["author"] = project.Author,
            ["scripts"] = new Dictionary<string, string>
            {
                ["dev"] = "vite",
                ["build"] = "vite build",
                ["preview"] = "vite preview"
            },
            ["dependencies"] = new Dictionary<string, string>
            {
                ["react"] = WebReactVersion,
                ["react-dom"] = WebReactVersion
            },
            ["devDependencies"] = new Dictionary<string, string>
            {
                ["@vitejs/plugin-react"] = VitePluginReactVersion,
                ["vite"] = ViteVersion
            }
        });

        files["vite.config.js"] = """
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    // Forms post to /api/...; during development they go to a local API
    // (for example a GrayCat "NodeJsApi" project running on port 3000).
    proxy: {
      '/api': 'http://localhost:3000',
    },
  },
});
""";

        files["index.html"] = $"""
<!DOCTYPE html>
<html lang="{WebUtility.HtmlEncode(site.Language)}">
  <head>
    <meta charset="UTF-8" />{(site.Favicon.Length > 0 ? $"\n    <link rel=\"icon\" href=\"{WebUtility.HtmlEncode(site.Favicon)}\" />" : "")}
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="{WebUtility.HtmlEncode(site.Description)}" />
    <title>{WebUtility.HtmlEncode(site.Title)}</title>{(site.Theme.FontUrl.Length > 0 ? $"\n    <link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(site.Theme.FontUrl)}\" />" : "")}
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.jsx"></script>
  </body>
</html>
""";

        files["src/main.jsx"] = """
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './styles/graycat.css';

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
""";

        files["src/App.jsx"] = """
import { useEffect, useState } from 'react';
import site from './data/site.json';
import { Page, themeStyle } from './components';

function normalize(path) {
  return path.replace(/\/+$/, '') || '/';
}

function pageFor(path) {
  return site.pages.find((p) => p.path === normalize(path)) || null;
}

export default function App() {
  const [page, setPage] = useState(() => pageFor(window.location.pathname));

  // Links between pages change the page without reloading; the back button works as usual.
  useEffect(() => {
    const onPopState = () => setPage(pageFor(window.location.pathname));
    const onClick = (event) => {
      const link = event.target.closest('a');
      if (!link || link.target === '_blank' || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
      const url = new URL(link.href, window.location.href);
      if (url.origin !== window.location.origin) return;
      if (normalize(url.pathname) === normalize(window.location.pathname)) return; // same page: let #anchors scroll
      const next = pageFor(url.pathname);
      if (!next) return;
      event.preventDefault();
      window.history.pushState(null, '', url.pathname + url.hash);
      setPage(next);
      window.scrollTo(0, 0);
    };
    window.addEventListener('popstate', onPopState);
    document.addEventListener('click', onClick);
    return () => {
      window.removeEventListener('popstate', onPopState);
      document.removeEventListener('click', onClick);
    };
  }, []);

  useEffect(() => {
    document.title = page && page.path !== '/' ? `${page.title} · ${site.title}` : site.title;
  }, [page]);

  return (
    <div className="gc-site" style={themeStyle(site.theme)}>
      {page ? (
        <Page page={page} />
      ) : (
        <div className="gc-empty">
          <h1>Page not found</h1>
          <p><a href="/">Go to the home page</a></p>
        </div>
      )}
    </div>
  );
}
""";

        files["src/data/site.json"] = siteJson;
        files["src/styles/graycat.css"] = SiteAssets.Css.TrimStart();
        foreach (var (name, code) in ReactLibrary.Components)
            files["src/components/" + name] = code.TrimStart();

        files["README.md"] = Readme(site, "React + Vite", """
```bash
npm install
npm run dev      # development server on http://localhost:5173
npm run build    # production build in dist/
```

- Page content lives in `src/data/site.json` (one entry per page); each block is drawn by a component in `src/components/`.
- `src/App.jsx` switches pages by URL (`/`, `/about`...). When hosting the `dist/` build, send unknown paths
  to `index.html` (the usual "SPA fallback" setting) so links like `/about` work after a reload.
- Styles are in `src/styles/graycat.css`.
- Forms post to `/api/...`. In development these requests are proxied to `http://localhost:3000`
  (see `vite.config.js`), so run a GrayCat **NodeJsApi** project there to receive them.

Regenerating from GrayCat Studio rewrites the generated files listed above.
""");
    }

    // ---------- Next.js ----------

    private static void AddNext(Dictionary<string, string> files, ProjectModel project, SiteModel site, string siteJson)
    {
        files["package.json"] = PackageJson(new Dictionary<string, object?>
        {
            ["name"] = PackageName(project.ProjectName),
            ["private"] = true,
            ["version"] = "0.1.0",
            ["author"] = project.Author,
            ["scripts"] = new Dictionary<string, string>
            {
                ["dev"] = "next dev",
                ["build"] = "next build",
                ["start"] = "next start"
            },
            ["dependencies"] = new Dictionary<string, string>
            {
                ["next"] = NextVersion,
                ["react"] = WebReactVersion,
                ["react-dom"] = WebReactVersion
            }
        });

        files["next.config.js"] = """
/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
};

module.exports = nextConfig;
""";

        files["pages/_app.jsx"] = """
import '../styles/graycat.css';

export default function App({ Component, pageProps }) {
  return <Component {...pageProps} />;
}
""";

        files["lib/SitePage.jsx"] = """
import Head from 'next/head';
import site from '../data/site.json';
import { Page, themeStyle } from '../components';

export default function SitePage({ slug }) {
  const page = site.pages.find((p) => p.slug === slug) || site.pages[0];
  const title = page.path === '/' ? site.title : `${page.title} · ${site.title}`;
  return (
    <>
      <Head>
        <title>{title}</title>
        <meta name="description" content={site.description} />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        {site.theme.fontUrl && <link rel="stylesheet" href={site.theme.fontUrl} />}
        {site.favicon && <link rel="icon" href={`/${site.favicon.replace(/^\//, '')}`} />}
      </Head>
      <div className="gc-site" style={themeStyle(site.theme)}>
        <Page page={page} />
      </div>
    </>
  );
}
""";

        files["pages/_document.jsx"] = """
import { Html, Head, Main, NextScript } from 'next/document';
import site from '../data/site.json';

export default function Document() {
  return (
    <Html lang={site.language || 'en'}>
      <Head />
      <body>
        <Main />
        <NextScript />
      </body>
    </Html>
  );
}
""";

        files["pages/index.jsx"] = """
import SitePage from '../lib/SitePage';

export default function Home() {
  return <SitePage slug="" />;
}
""";

        files["pages/[slug].jsx"] = """
import site from '../data/site.json';
import SitePage from '../lib/SitePage';

export default function OtherPage({ slug }) {
  return <SitePage slug={slug} />;
}

export function getStaticPaths() {
  return {
    paths: site.pages.filter((p) => p.slug).map((p) => ({ params: { slug: p.slug } })),
    fallback: false,
  };
}

export function getStaticProps({ params }) {
  return { props: { slug: params.slug } };
}
""";

        files["pages/api/[form].js"] = """
// Receives GrayCat form submissions (POST /api/contact, /api/subscribe, ...)
// and appends them to data/submissions.json. Swap this for a database or e-mail service in production.
import { promises as fs } from 'fs';
import path from 'path';

export default async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return res.status(405).json({ error: 'Method not allowed' });
  }

  const form = String(req.query.form || '');
  if (!/^[a-z0-9_-]{1,50}$/i.test(form)) {
    return res.status(400).json({ error: 'Invalid form name' });
  }

  const data = req.body && typeof req.body === 'object' ? req.body : {};
  if (Object.keys(data).length === 0) {
    return res.status(400).json({ error: 'Empty submission' });
  }

  const file = path.join(process.cwd(), 'data', 'submissions.json');
  let submissions = [];
  try {
    submissions = JSON.parse(await fs.readFile(file, 'utf8'));
  } catch {
    // first submission
  }
  submissions.push({ form, data, receivedAt: new Date().toISOString() });
  await fs.mkdir(path.dirname(file), { recursive: true });
  await fs.writeFile(file, JSON.stringify(submissions, null, 2));

  return res.status(201).json({ ok: true });
}
""";

        files["data/site.json"] = siteJson;
        files["styles/graycat.css"] = SiteAssets.Css.TrimStart();
        foreach (var (name, code) in ReactLibrary.Components)
            files["components/" + name] = code.TrimStart();

        files["README.md"] = Readme(site, "Next.js", """
```bash
npm install
npm run dev      # development server on http://localhost:3000
npm run build && npm start
```

- Page content lives in `data/site.json`; `pages/index.jsx` is the home page and `pages/[slug].jsx` the other pages.
  Each block is drawn by a component in `components/`.
- Forms post to `pages/api/[form].js`, which stores submissions in `data/submissions.json`.

Regenerating from GrayCat Studio rewrites the generated files listed above.
""");
    }

    // ---------- Node.js API ----------

    private static void AddNode(Dictionary<string, string> files, ProjectModel project, SiteModel site, string siteJson, bool useSqlite)
    {
        var deps = new Dictionary<string, string>
        {
            ["cors"] = CorsVersion,
            ["express"] = ExpressVersion
        };
        if (useSqlite) deps["sqlite3"] = Sqlite3Version;

        files["package.json"] = PackageJson(new Dictionary<string, object?>
        {
            ["name"] = PackageName(project.ProjectName),
            ["version"] = "1.0.0",
            ["private"] = true,
            ["description"] = $"{site.Title} API - created with GrayCat",
            ["author"] = project.Author,
            ["main"] = "src/index.js",
            ["engines"] = new Dictionary<string, string> { ["node"] = ">=18" },
            ["scripts"] = new Dictionary<string, string>
            {
                ["start"] = "node src/index.js",
                ["dev"] = "node --watch src/index.js"
            },
            ["dependencies"] = deps
        });

        files["src/index.js"] = """
const path = require('path');
const express = require('express');
const cors = require('cors');
const formsRouter = require('./routes/forms');
const site = require('./data/site.json');

const app = express();
const PORT = process.env.PORT || 3000;

app.use(cors());
app.use(express.json({ limit: '100kb' }));
app.use(express.urlencoded({ extended: false }));

app.get('/api/health', (req, res) => res.json({ status: 'ok' }));
app.get('/api/site', (req, res) => res.json(site));
app.use('/api', formsRouter);
app.use('/api', (req, res) => res.status(404).json({ error: 'Not found' }));

// The site designed in GrayCat, served as static files.
app.use(express.static(path.join(__dirname, '..', 'public'), { extensions: ['html'] }));

app.use((err, req, res, next) => {
  console.error(err);
  res.status(500).json({ error: 'Internal server error' });
});

if (require.main === module) {
  app.listen(PORT, () => {
    console.log(`Server running on http://localhost:${PORT}`);
  });
}

module.exports = app;
""";

        files["src/routes/forms.js"] = """
const express = require('express');
const storage = require('../storage');

const router = express.Router();

// List submissions. Disabled unless ADMIN_TOKEN is set; call with ?token=<ADMIN_TOKEN>.
router.get('/submissions', async (req, res, next) => {
  const token = process.env.ADMIN_TOKEN;
  if (!token) return res.status(403).json({ error: 'Set ADMIN_TOKEN to enable this endpoint' });
  if (req.query.token !== token) return res.status(401).json({ error: 'Invalid token' });
  try {
    res.json(await storage.list());
  } catch (err) {
    next(err);
  }
});

// Receive a form: POST /api/contact, /api/subscribe, ...
router.post('/:form', async (req, res, next) => {
  const { form } = req.params;
  if (!/^[a-z0-9_-]{1,50}$/i.test(form)) {
    return res.status(400).json({ error: 'Invalid form name' });
  }
  const data = req.body && typeof req.body === 'object' ? req.body : {};
  if (Object.keys(data).length === 0) {
    return res.status(400).json({ error: 'Empty submission' });
  }
  try {
    const saved = await storage.save(form, data);
    res.status(201).json({ ok: true, id: saved.id });
  } catch (err) {
    next(err);
  }
});

module.exports = router;
""";

        files["src/storage.js"] = useSqlite ? """
// Stores form submissions in SQLite (data/app.db).
const fs = require('fs');
const path = require('path');
const sqlite3 = require('sqlite3');

const dir = path.join(__dirname, '..', 'data');
fs.mkdirSync(dir, { recursive: true });
const db = new sqlite3.Database(path.join(dir, 'app.db'));

const ready = new Promise((resolve, reject) => {
  db.run(
    `CREATE TABLE IF NOT EXISTS submissions (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      form TEXT NOT NULL,
      data TEXT NOT NULL,
      received_at TEXT NOT NULL
    )`,
    (err) => (err ? reject(err) : resolve())
  );
});

async function save(form, data) {
  await ready;
  const receivedAt = new Date().toISOString();
  return new Promise((resolve, reject) => {
    db.run(
      'INSERT INTO submissions (form, data, received_at) VALUES (?, ?, ?)',
      [form, JSON.stringify(data), receivedAt],
      function onInsert(err) {
        if (err) return reject(err);
        resolve({ id: this.lastID, form, data, receivedAt });
      }
    );
  });
}

async function list() {
  await ready;
  return new Promise((resolve, reject) => {
    db.all('SELECT * FROM submissions ORDER BY id', (err, rows) => {
      if (err) return reject(err);
      resolve(rows.map((r) => ({ id: r.id, form: r.form, data: JSON.parse(r.data), receivedAt: r.received_at })));
    });
  });
}

module.exports = { save, list };
""" : """
// Stores form submissions in data/submissions.json.
const fs = require('fs/promises');
const path = require('path');

const file = path.join(__dirname, '..', 'data', 'submissions.json');
let queue = Promise.resolve();

async function readAll() {
  try {
    return JSON.parse(await fs.readFile(file, 'utf8'));
  } catch {
    return [];
  }
}

function save(form, data) {
  // Writes run one after another so concurrent requests cannot overwrite each other.
  const run = queue.then(async () => {
    const all = await readAll();
    const item = { id: all.length + 1, form, data, receivedAt: new Date().toISOString() };
    all.push(item);
    await fs.mkdir(path.dirname(file), { recursive: true });
    await fs.writeFile(file, JSON.stringify(all, null, 2));
    return item;
  });
  queue = run.catch(() => {});
  return run;
}

function list() {
  return readAll();
}

module.exports = { save, list };
""";

        files["src/data/site.json"] = siteJson;
        AddStatic(files, site, "public/");
        files[".env.example"] = """
PORT=3000
# Enables GET /api/submissions?token=...
ADMIN_TOKEN=
""";

        files["README.md"] = Readme(site, "Node.js + Express API", $$"""
```bash
npm install
npm start        # http://localhost:3000
npm run dev      # restarts on file changes
```

| Endpoint | Description |
|---|---|
| `GET /` | The site designed in GrayCat (`public/`) |
| `GET /api/health` | Health check |
| `GET /api/site` | Page content as JSON |
| `POST /api/:form` | Receives a form (`/api/contact`, `/api/subscribe`, ...) |
| `GET /api/submissions?token=...` | Lists submissions; needs the `ADMIN_TOKEN` environment variable |

Submissions are stored in {{(useSqlite ? "`data/app.db` (SQLite)" : "`data/submissions.json`")}}.
""");
    }

    // ---------- React Native (Expo) ----------

    private static void AddReactNative(Dictionary<string, string> files, ProjectModel project, SiteModel site, string siteJson, IReadOnlyCollection<string> assetFiles)
    {
        var slug = PackageName(project.ProjectName);
        files["package.json"] = PackageJson(new Dictionary<string, object?>
        {
            ["name"] = slug,
            ["version"] = "1.0.0",
            ["private"] = true,
            ["author"] = project.Author,
            ["main"] = "index.js",
            ["scripts"] = new Dictionary<string, string>
            {
                ["start"] = "expo start",
                ["android"] = "expo start --android",
                ["ios"] = "expo start --ios",
                ["web"] = "expo start --web"
            },
            ["dependencies"] = new Dictionary<string, string>
            {
                ["expo"] = ReactNativeLibrary.ExpoVersion,
                ["expo-status-bar"] = ReactNativeLibrary.ExpoStatusBarVersion,
                ["react"] = ReactNativeLibrary.ReactVersion,
                ["react-native"] = ReactNativeLibrary.ReactNativeVersion,
                // Lets "npx expo start --web" show the app in a browser.
                ["react-dom"] = ReactNativeLibrary.ReactVersion,
                ["react-native-web"] = ReactNativeLibrary.ReactNativeWebVersion,
                ["@expo/metro-runtime"] = ReactNativeLibrary.MetroRuntimeVersion
            }
        });

        files["app.json"] = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["expo"] = new Dictionary<string, object>
            {
                ["name"] = site.Title,
                ["slug"] = slug,
                ["version"] = "1.0.0",
                ["orientation"] = "portrait",
                ["userInterfaceStyle"] = "light"
            }
        }, GrayCatJson.Web);

        files["index.js"] = """
import { registerRootComponent } from 'expo';
import App from './App';

registerRootComponent(App);
""";

        files["App.js"] = ReactNativeLibrary.AppJs.TrimStart();
        files["src/blocks.js"] = ReactNativeLibrary.BlocksJs.TrimStart();
        files["src/config.js"] = ReactNativeLibrary.ConfigJs.TrimStart();
        files["src/assets.js"] = ReactNativeAssets(assetFiles);
        files["src/data/site.json"] = siteJson;

        files["README.md"] = Readme(site, "React Native (Expo)", """
```bash
npm install
npx expo start   # scan the QR code with the Expo Go app, or press a / i / w
```

- Screen content lives in `src/data/site.json`; blocks are drawn by `src/blocks.js`.
- Forms send data to `API_BASE_URL` from `src/config.js` (a phone cannot reach `localhost` on your PC;
  use your computer's network address, e.g. `http://192.168.1.10:3000`).
- If Expo Go asks for a different SDK version, run `npx expo install --fix`.
""");
    }

    /// <summary>Maps "assets/..." paths to require() calls so the bundler packs local images.</summary>
    private static string ReactNativeAssets(IReadOnlyCollection<string> assetFiles)
    {
        var lines = assetFiles
            .Where(ProjectAssets.IsNativeImage)
            .Select(a => $"  {JsonSerializer.Serialize(a)}: require({JsonSerializer.Serialize("../" + a)}),\n");
        return "// Generated by GrayCat: local images from the assets folder.\n" +
               "export default {\n" + string.Concat(lines) + "};\n";
    }

    // ---------- helpers ----------

    private static string PackageJson(Dictionary<string, object?> content)
    {
        if (content.TryGetValue("author", out var a) && string.IsNullOrWhiteSpace(a as string))
            content.Remove("author");
        return JsonSerializer.Serialize(content, GrayCatJson.Web) + "\n";
    }

    private static string Readme(SiteModel site, string stack, string body) =>
        $"# {site.Title}\n\n{stack} project generated by **GrayCat Studio**" +
        (string.IsNullOrWhiteSpace(site.Author) ? "" : $" for {site.Author}") +
        $".\n\n## Getting started\n\n{body.Trim()}\n\nThis folder holds generated code. The design itself lives in the `.graycat` file next to it; " +
        "open that file in GrayCat Studio to make changes and generate again.\n";

    private static string GitIgnore(ProjectType type) => type switch
    {
        ProjectType.NextJs => "node_modules/\n.next/\nout/\ndata/submissions.json\n.env\n",
        ProjectType.NodeJsApi => "node_modules/\ndata/\n.env\n",
        ProjectType.ReactNative => "node_modules/\n.expo/\ndist/\nweb-build/\n*.jks\n*.p8\n*.p12\n*.key\n*.mobileprovision\n.env\n",
        _ => "node_modules/\ndist/\n.env\n"
    };
}
