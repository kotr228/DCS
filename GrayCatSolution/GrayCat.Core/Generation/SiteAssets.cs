namespace GrayCat.Core.Generation;

/// <summary>Stylesheet and browser script shared by every web output (static HTML, React, Next.js).</summary>
public static class SiteAssets
{
    public const string Css = """
/* GrayCat site styles. Safe to edit: GrayCat rewrites this file only when you regenerate. */
:root {
  --gc-primary: #4f46e5;
  --gc-primary-dark: #4338ca;
  --gc-secondary: #64748b;
  --gc-success: #16a34a;
  --gc-danger: #dc2626;
  --gc-warning: #d97706;
  --gc-info: #0891b2;
  --gc-text: #1e293b;
  --gc-muted: #64748b;
  --gc-bg: #ffffff;
  --gc-surface: #f8fafc;
  --gc-border: #e2e8f0;
  --gc-radius: 12px;
  --gc-shadow: 0 10px 30px rgba(15, 23, 42, 0.08);
  --gc-font: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
}

*, *::before, *::after { box-sizing: border-box; }
html { scroll-behavior: smooth; }
body { margin: 0; font-family: var(--gc-font); color: var(--gc-text); background: var(--gc-bg); line-height: 1.6; -webkit-font-smoothing: antialiased; }
img { max-width: 100%; display: block; }
a { color: var(--gc-primary); }
h1, h2, h3 { line-height: 1.2; margin: 0 0 0.5em; }

.gc-site { min-height: 100vh; display: flex; flex-direction: column; font-family: var(--gc-font); }
[id] { scroll-margin-top: 80px; }
.gc-empty { padding: 120px 24px; text-align: center; color: var(--gc-muted); }
.gc-container { width: 100%; max-width: 1120px; margin: 0 auto; padding: 0 24px; }
.gc-narrow { max-width: 720px; }
.gc-block { padding: 56px 0; }
.gc-section-title { text-align: center; font-size: 2rem; margin-bottom: 32px; }
.gc-lead { font-size: 1.2rem; color: inherit; opacity: 0.85; margin: 0 0 1.5em; }

/* Text */
.gc-text { padding: 32px 0; }
.gc-text p { margin: 0; white-space: pre-line; font-size: 1.05rem; }

/* Header */
.gc-header { background: var(--gc-surface); text-align: center; border-bottom: 1px solid var(--gc-border); }
.gc-header h1 { font-size: 2.5rem; }
.gc-header-nav { display: flex; gap: 24px; justify-content: center; flex-wrap: wrap; margin-top: 16px; }
.gc-header-nav a { text-decoration: none; font-weight: 600; }

/* Navbar */
.gc-navbar { padding: 16px 0; background: #fff; border-bottom: 1px solid var(--gc-border); z-index: 100; }
.gc-navbar-fixed { position: fixed; top: 0; left: 0; right: 0; }
.gc-navbar-sticky { position: sticky; top: 0; }
.gc-navbar-transparent { background: transparent; border-bottom-color: transparent; }
.gc-navbar-inner { display: flex; align-items: center; gap: 24px; flex-wrap: wrap; }
.gc-brand { display: flex; align-items: center; gap: 10px; font-weight: 800; font-size: 1.25rem; color: inherit; text-decoration: none; }
.gc-brand img { height: 32px; width: auto; }
.gc-nav-links { display: flex; gap: 20px; list-style: none; margin: 0 0 0 auto; padding: 0; flex-wrap: wrap; }
.gc-nav-links a { color: inherit; text-decoration: none; font-weight: 500; opacity: 0.8; }
.gc-nav-links a:hover, .gc-nav-links a.is-active { opacity: 1; color: var(--gc-primary); }
.gc-nav-links a.is-active { font-weight: 700; }
.gc-header-nav a.is-active { text-decoration: underline; text-underline-offset: 6px; }
.gc-search { padding: 8px 12px; border: 1px solid var(--gc-border); border-radius: 8px; font: inherit; }

/* Hero */
.gc-hero { position: relative; color: #fff; background-color: var(--gc-primary-dark); background-image: linear-gradient(135deg, var(--gc-primary) 0%, var(--gc-accent) 100%); background-size: cover; background-position: center; display: flex; align-items: center; overflow: hidden; }
.gc-hero-small { min-height: 320px; }
.gc-hero-medium { min-height: 480px; }
.gc-hero-large { min-height: 640px; }
.gc-hero-full { min-height: 100vh; }
.gc-hero-overlay { position: absolute; inset: 0; background: #000; }
.gc-hero-content { position: relative; }
.gc-hero h1 { font-size: clamp(2.2rem, 5vw, 3.6rem); font-weight: 800; }
.gc-align-left { text-align: left; }
.gc-align-center { text-align: center; }
.gc-align-right { text-align: right; }
.gc-align-center .gc-lead { margin-left: auto; margin-right: auto; max-width: 640px; }

/* Buttons */
.gc-button-block { padding: 24px 0; }
.gc-btn { display: inline-block; padding: 12px 24px; border-radius: 10px; border: 2px solid transparent; font: inherit; font-weight: 600; text-decoration: none; cursor: pointer; transition: transform .15s ease, box-shadow .15s ease, background .15s ease; color: #fff; background: var(--gc-primary); }
.gc-btn:hover { transform: translateY(-1px); box-shadow: 0 6px 16px rgba(15, 23, 42, .18); }
.gc-btn-primary:hover { background: var(--gc-primary-dark); }
.gc-btn-primary { background: var(--gc-primary); }
.gc-btn-secondary { background: var(--gc-secondary); }
.gc-btn-success { background: var(--gc-success); }
.gc-btn-danger { background: var(--gc-danger); }
.gc-btn-warning { background: var(--gc-warning); }
.gc-btn-info { background: var(--gc-info); }
.gc-btn-small { padding: 8px 16px; font-size: .875rem; }
.gc-btn-large { padding: 16px 32px; font-size: 1.1rem; }
.gc-btn-block { display: block; width: 100%; text-align: center; }
.gc-btn.is-disabled { opacity: .5; pointer-events: none; }
.gc-hero .gc-btn { background: #fff; color: var(--gc-primary); }

/* Card */
.gc-card { display: flex; flex-direction: column; background: #fff; border-radius: var(--gc-radius); overflow: hidden; max-width: 420px; margin: 0 auto; transition: transform .2s ease, box-shadow .2s ease; }
.gc-card-img-left { flex-direction: row; max-width: 760px; }
.gc-card-img-right { flex-direction: row-reverse; max-width: 760px; }
.gc-card-img-bottom { flex-direction: column-reverse; }
.gc-card-img-left .gc-card-img, .gc-card-img-right .gc-card-img { width: 40%; object-fit: cover; }
.gc-card-img { width: 100%; aspect-ratio: 16 / 10; object-fit: cover; }
.gc-card-body { padding: 24px; flex: 1; }
.gc-card-body p { color: var(--gc-muted); }
.gc-card-shadow { box-shadow: var(--gc-shadow); }
.gc-card-bordered { border: 1px solid var(--gc-border); }
.gc-card-hover:hover { transform: translateY(-4px); box-shadow: 0 18px 40px rgba(15, 23, 42, .12); }
.gc-link { font-weight: 600; text-decoration: none; }

.gc-card-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 24px; width: 100%; max-width: 1120px; margin: 0 auto; padding: 56px 24px; }
.gc-card-row > .gc-card-block { padding: 0; }
.gc-card-row > .gc-card-block > .gc-container { padding: 0; height: 100%; }
.gc-card-row .gc-card { max-width: none; height: 100%; }

/* Image */
.gc-image { margin: 0 auto; padding: 32px 24px; max-width: 1120px; text-align: center; }
.gc-image img { margin: 0 auto; border-radius: inherit; }
.gc-image figcaption { color: var(--gc-muted); margin-top: 12px; font-size: .95rem; }
.gc-placeholder { padding: 48px; border: 2px dashed var(--gc-border); border-radius: var(--gc-radius); color: var(--gc-muted); text-align: center; }

/* Grids */
.gc-grid { display: grid; gap: 24px; grid-template-columns: repeat(var(--gc-cols, 3), minmax(0, 1fr)); }
.gc-gallery figure { margin: 0; }
.gc-gallery img { width: 100%; aspect-ratio: 3 / 2; object-fit: cover; border-radius: var(--gc-radius); }
.gc-gallery figcaption { color: var(--gc-muted); font-size: .9rem; margin-top: 8px; }
.gc-product { background: #fff; border: 1px solid var(--gc-border); border-radius: var(--gc-radius); overflow: hidden; display: flex; flex-direction: column; }
.gc-product img { width: 100%; aspect-ratio: 4 / 3; object-fit: cover; }
.gc-product-body { padding: 20px; display: flex; flex-direction: column; gap: 8px; flex: 1; }
.gc-product-body p { color: var(--gc-muted); margin: 0; flex: 1; }
.gc-product-price { font-size: 1.3rem; font-weight: 800; color: var(--gc-primary); }

/* Forms */
.gc-form-block { background: var(--gc-surface); }
.gc-form-block h2, .gc-form-block > .gc-container > p { text-align: center; }
.gc-form { display: grid; gap: 16px; margin-top: 24px; background: #fff; padding: 32px; border-radius: var(--gc-radius); box-shadow: var(--gc-shadow); }
.gc-field { display: grid; gap: 6px; }
.gc-field label { font-weight: 600; font-size: .95rem; }
.gc-field input, .gc-field textarea { width: 100%; padding: 12px 14px; border: 1px solid var(--gc-border); border-radius: 8px; font: inherit; color: inherit; background: #fff; }
.gc-field input:focus, .gc-field textarea:focus { outline: 2px solid var(--gc-primary); outline-offset: 1px; border-color: transparent; }
.gc-field textarea { min-height: 120px; resize: vertical; }
.gc-required { color: var(--gc-danger); }
.gc-form-status { margin: 0; min-height: 1.5em; font-weight: 600; }
.gc-form-status.is-success { color: var(--gc-success); }
.gc-form-status.is-error { color: var(--gc-danger); }

/* Video */
.gc-video-frame { position: relative; width: 100%; border-radius: var(--gc-radius); overflow: hidden; background: #000; }
.gc-video-frame iframe, .gc-video-frame video { position: absolute; inset: 0; width: 100%; height: 100%; border: 0; }

/* Testimonial */
.gc-testimonial blockquote { margin: 0; text-align: center; }
.gc-quote { font-size: 1.4rem; font-style: italic; margin: 0 0 16px; }
.gc-stars { color: #f59e0b; font-size: 1.3rem; letter-spacing: 2px; margin-bottom: 16px; }
.gc-author { display: inline-flex; align-items: center; gap: 12px; text-align: left; }
.gc-author img { width: 56px; height: 56px; border-radius: 50%; object-fit: cover; }
.gc-author span { display: block; color: var(--gc-muted); font-size: .9rem; }
.gc-testimonial-card blockquote { background: #fff; padding: 40px; border-radius: var(--gc-radius); box-shadow: var(--gc-shadow); }
.gc-testimonial-minimal .gc-quote { font-size: 1.15rem; }

/* Pricing */
.gc-price-card { position: relative; max-width: 380px; margin: 0 auto; background: #fff; border: 1px solid var(--gc-border); border-radius: var(--gc-radius); padding: 40px 32px; text-align: center; }
.gc-price-highlight { border: 2px solid var(--gc-primary); box-shadow: 0 20px 50px rgba(79, 70, 229, .18); }
.gc-badge { position: absolute; top: -14px; left: 50%; transform: translateX(-50%); background: var(--gc-primary); color: #fff; font-size: .8rem; font-weight: 700; padding: 4px 14px; border-radius: 999px; }
.gc-price { margin: 16px 0 24px; }
.gc-amount { font-size: 3rem; font-weight: 800; }
.gc-currency { font-size: 1.5rem; vertical-align: top; font-weight: 700; }
.gc-period { color: var(--gc-muted); }
.gc-features { list-style: none; padding: 0; margin: 0 0 28px; text-align: left; }
.gc-features li { padding: 8px 0; border-bottom: 1px solid var(--gc-border); }
.gc-features li::before { content: "✓"; color: var(--gc-success); font-weight: 800; margin-right: 10px; }

/* Accordion */
.gc-accordion h2 { text-align: center; margin-bottom: 24px; }
.gc-accordion-item { border: 1px solid var(--gc-border); border-radius: 10px; margin-bottom: 12px; background: #fff; overflow: hidden; }
.gc-accordion-item summary { cursor: pointer; padding: 16px 20px; font-weight: 600; list-style: none; display: flex; justify-content: space-between; gap: 16px; }
.gc-accordion-item summary::-webkit-details-marker { display: none; }
.gc-accordion-item summary::after { content: "+"; font-size: 1.3rem; line-height: 1; color: var(--gc-primary); }
.gc-accordion-item[open] summary::after { content: "−"; }
.gc-accordion-body { padding: 0 20px 18px; color: var(--gc-muted); white-space: pre-line; }

/* Stats */
.gc-stats { background: var(--gc-surface); }
.gc-stats-row { display: flex; justify-content: center; gap: 48px; flex-wrap: wrap; text-align: center; }
.gc-stats-vertical .gc-stats-row { flex-direction: column; align-items: center; gap: 24px; }
.gc-stats-grid .gc-stats-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); }
.gc-stat-number { font-size: 2.6rem; font-weight: 800; color: var(--gc-primary); }
.gc-stat-label { color: var(--gc-muted); font-weight: 500; }

/* Social links */
.gc-social { padding: 32px 0; }
.gc-social-list { list-style: none; margin: 0; padding: 0; display: inline-flex; gap: 12px; flex-wrap: wrap; }
.gc-social-link { display: inline-flex; align-items: center; gap: 8px; text-decoration: none; color: inherit; font-weight: 600; }
.gc-social-icon { display: inline-grid; place-items: center; width: 40px; height: 40px; border-radius: 50%; background: var(--gc-primary); color: #fff; font-weight: 800; }
.gc-social-small .gc-social-icon { width: 32px; height: 32px; font-size: .8rem; }
.gc-social-large .gc-social-icon { width: 52px; height: 52px; font-size: 1.2rem; }
.gc-social-icons .gc-social-name { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
.gc-social-text .gc-social-icon { display: none; }
.gc-social-buttons .gc-social-link { border: 1px solid var(--gc-border); border-radius: 999px; padding: 6px 16px 6px 6px; }

/* Divider */
.gc-divider { padding: 24px 0; }
.gc-divider-small { padding: 8px 0; }
.gc-divider-large { padding: 48px 0; }
.gc-divider hr { margin: 0; border: 0; }
.gc-divider-with-icon { display: flex; align-items: center; gap: 16px; }
.gc-divider-with-icon hr { flex: 1; }
.gc-divider-icon { color: var(--gc-muted); }

/* Footer */
.gc-footer { margin-top: auto; background: #0f172a; color: #cbd5e1; padding: 32px 0; }
.gc-footer-inner { display: flex; justify-content: space-between; align-items: center; gap: 16px; flex-wrap: wrap; }
.gc-footer p { margin: 0; }
.gc-footer nav { display: flex; gap: 20px; flex-wrap: wrap; }
.gc-footer a { color: #e2e8f0; text-decoration: none; }
.gc-footer a:hover { color: #fff; text-decoration: underline; }

@media (max-width: 768px) {
  .gc-block { padding: 40px 0; }
  .gc-grid { grid-template-columns: repeat(min(var(--gc-cols, 3), 2), minmax(0, 1fr)); }
  .gc-card-img-left, .gc-card-img-right { flex-direction: column; }
  .gc-card-img-left .gc-card-img, .gc-card-img-right .gc-card-img { width: 100%; }
  .gc-nav-links { margin-left: 0; width: 100%; }
  .gc-footer-inner { flex-direction: column; text-align: center; }
}
@media (max-width: 480px) {
  .gc-grid { grid-template-columns: 1fr; }
  .gc-form { padding: 20px; }
}
""";

    /// <summary>Hover and selection outlines for the in-editor preview.</summary>
    public const string EditorCss = """
[data-gc-id] { cursor: pointer; }
[data-gc-id]:hover { outline: 2px dashed rgba(79, 70, 229, .55); outline-offset: -2px; }
[data-gc-hidden] { opacity: .35; }
[data-gc-hidden]::before { content: "Hidden on the published site"; display: block; background: #f97316; color: #fff; font: 600 12px system-ui; padding: 4px 10px; }
.gc-selected, .gc-selected:hover { outline: 3px solid #f97316 !important; outline-offset: -3px; box-shadow: inset 0 0 0 5px rgba(255, 255, 255, .55); }
.gc-navbar-fixed { position: sticky; }
""";

    /// <summary>In-editor preview: click selects a block (reported to the host app), links and forms are inert.</summary>
    public const string EditorScript = """
(function () {
  function post(message) {
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
  }
  function blockOf(el) {
    while (el && el !== document.body) {
      if (el.dataset && el.dataset.gcId) return el;
      el = el.parentElement;
    }
    return null;
  }
  document.addEventListener('click', function (event) {
    var block = blockOf(event.target);
    event.preventDefault();
    event.stopPropagation();
    window.gcSelect(block ? block.dataset.gcId : null, false);
    post({ type: 'select', id: block ? block.dataset.gcId : null });
  }, true);
  document.addEventListener('submit', function (event) { event.preventDefault(); }, true);

  window.gcSelect = function (id, scroll) {
    document.querySelectorAll('.gc-selected').forEach(function (el) { el.classList.remove('gc-selected'); });
    if (!id) return;
    var el = document.querySelector('[data-gc-id="' + id + '"]');
    if (!el) return;
    el.classList.add('gc-selected');
    if (scroll !== false) {
      var r = el.getBoundingClientRect();
      if (r.top < 0 || r.top > window.innerHeight - 40) el.scrollIntoView({ block: 'start', behavior: 'smooth' });
    }
  };
  // Replaces the page body without reloading, so the scroll position stays put.
  window.gcUpdate = function (html, id) {
    document.querySelector('.gc-site').innerHTML = html;
    window.gcSelect(id, false);
  };
  post({ type: 'ready' });
})();
""";

    /// <summary>Submits GrayCat forms with fetch and shows a status message (static HTML output).</summary>
    public const string FormScript = """
// GrayCat forms: send as JSON and show the result in place.
document.querySelectorAll('form[data-gc-form]').forEach(function (form) {
  form.addEventListener('submit', async function (event) {
    event.preventDefault();
    var status = form.querySelector('.gc-form-status');
    var button = form.querySelector('button[type="submit"]');
    var data = Object.fromEntries(new FormData(form).entries());
    if (button) button.disabled = true;
    status.className = 'gc-form-status';
    status.textContent = form.dataset.sending || 'Sending...';
    try {
      var response = await fetch(form.getAttribute('action'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
      });
      if (!response.ok) throw new Error('HTTP ' + response.status);
      status.className = 'gc-form-status is-success';
      status.textContent = form.dataset.success;
      form.reset();
    } catch (error) {
      status.className = 'gc-form-status is-error';
      status.textContent = form.dataset.error;
    } finally {
      if (button) button.disabled = false;
    }
  });
});
""";
}
