// Converts Skyframe's src/data/filters.js into assets/catalog/filters.json.
// Usage: node scripts/import-skyframe-filters.mjs <path-to-skyframe-checkout>
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const root = process.argv[2];
if (!root) { console.error('Usage: node scripts/import-skyframe-filters.mjs <skyframe>'); process.exit(1); }
const source = fs.readFileSync(path.join(root, 'src/data/filters.js'), 'utf8');
const sandbox = { window: {} };
vm.runInNewContext(source, sandbox);
const kinds = { bb: 'broadband', lp: 'lightPollution', multi: 'multiband', nb: 'narrowband' };
const filters = sandbox.window.FILTER_DB.map((f) => ({
  id: f.id,
  brand: f.brand,
  name: f.name,
  series: f.series ?? null,
  kind: kinds[f.kind] ?? f.kind,
  channel: f.ch ?? null,
  camera: f.for,
  bands: f.bands.map(([from, to, peak]) => ({ fromNm: from, toNm: to, peak })),
  approximate: Boolean(f.approx),
  source: f.src || null,
}));
const out = { schema: 1, origin: 'Skyframe src/data/filters.js', filters };
fs.writeFileSync(new URL('../assets/catalog/filters.json', import.meta.url), JSON.stringify(out, null, 1) + '\n');
console.log(`${filters.length} filters written`);
