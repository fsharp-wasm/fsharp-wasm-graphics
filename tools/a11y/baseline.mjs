// node tools/a11y/baseline.mjs <root-dir> <label> <path>...
// axe-core (WCAG 2.0 / 2.1 / 2.2, levels A and AA) and what assistive technology sees, per page: the nodes of Chromium's
// accessibility tree, how many of them can take the keyboard focus, and how many stops Tab reaches.  One JSON line per page.
// `npm install` in this folder fetches axe-core; the pages are served from <root-dir> (for example fsharp-wasm-runtime/site).
// Output: one JSON line per page on stdout.
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
// Playwright from node_modules, else the global install (CI images have it globally)
let chromium;
try { ({ chromium } = require('playwright')); } catch { ({ chromium } = require(require('node:child_process').execSync('npm root -g').toString().trim() + '/playwright')); }
const axeSource = readFileSync(new URL('./node_modules/axe-core/axe.min.js', import.meta.url), 'utf8');

const [root, label, ...paths] = process.argv.slice(2);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2' };
const server = http.createServer(async (req, res) => {
  try {
    let p = normalize(join(root, decodeURIComponent(new URL(req.url, 'http://x').pathname)));
    if (!p.startsWith(root)) throw new Error('outside');
    if (extname(p) === '') p = join(p, 'index.html');
    res.writeHead(200, { 'content-type': types[extname(p)] ?? 'application/octet-stream' });
    res.end(await readFile(p));
  } catch { res.writeHead(404); res.end('nf'); }
}).listen(0);
await new Promise((r) => server.once('listening', r));
const base = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch();
for (const path of paths) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  let result = { label, path };
  try {
    await page.goto(base + path, { waitUntil: 'load' });
    await page.waitForTimeout(1500);                       // wasm modules start, the first frame is drawn
    await page.evaluate(axeSource);
    const axe = await page.evaluate(() => axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] }, resultTypes: ['violations', 'incomplete'] }));
    const wcag = (tags) => tags.filter((t) => /^wcag\d{3,4}$/.test(t)).map((t) => t.slice(4).split('').join('.'));
    result.violations = axe.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, wcag: wcag(v.tags) }));
    result.incomplete = axe.incomplete.map((v) => ({ id: v.id, nodes: v.nodes.length }));
    result.passes = axe.passes.length;
    // what assistive technology gets: the accessibility tree of the page (Chromium's own)
    const cdp = await page.context().newCDPSession(page);
    const { nodes } = await cdp.send('Accessibility.getFullAXTree');
    const visible = nodes.filter((n) => !n.ignored);
    const prop = (n, name) => n.properties?.find((p) => p.name === name)?.value?.value;
    result.ax = {
      nodes: visible.length,
      roles: visible.reduce((m, n) => { const r = n.role?.value ?? '?'; m[r] = (m[r] ?? 0) + 1; return m; }, {}),
      focusable: visible.filter((n) => prop(n, 'focusable')).length,
    };
    // the keyboard: how many stops does Tab reach
    const stops = new Set();
    for (let i = 0; i < 60; i++) {
      await page.keyboard.press('Tab');
      const d = await page.evaluate(() => { const e = document.activeElement; return e && e !== document.body ? `${e.tagName.toLowerCase()}#${e.id}.${e.getAttribute('aria-label') ?? e.textContent?.trim().slice(0, 20)}` : ''; });
      if (!d) break; stops.add(d);
    }
    result.tabStops = stops.size;
    result.canvases = await page.evaluate(() => [...document.querySelectorAll('canvas')].map((c) => ({ label: c.getAttribute('aria-label'), role: c.getAttribute('role'), tabindex: c.getAttribute('tabindex') })));
  } catch (e) { result.error = String(e.message ?? e).split('\n')[0]; }
  result.pageErrors = errors.slice(0, 2);
  console.log(JSON.stringify(result));
  await page.close();
}
await browser.close();
server.close();
