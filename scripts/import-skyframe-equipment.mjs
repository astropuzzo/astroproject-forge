// Builds assets/catalog/equipment.json (cameras and telescopes) from Skyframe's gear data and the AstroBin equipment list.
// Usage: node scripts/import-skyframe-equipment.mjs <path-to-skyframe-checkout> [astrobin-equipment.json]
// The AstroBin list defaults to <skyframe>/scripts/data/astrobin-equipment.json. Aliases curated in the existing output
// are kept across imports.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { createRequire } from 'node:module';

const root = process.argv[2];
if (!root) { console.error('Usage: node scripts/import-skyframe-equipment.mjs <skyframe> [astrobin-equipment.json]'); process.exit(1); }
const require = createRequire(import.meta.url);
const gear = require(path.resolve(root, 'scripts/gear.cjs'));
const astrobin = JSON.parse(fs.readFileSync(process.argv[3] ?? path.join(root, 'scripts/data/astrobin-equipment.json'), 'utf8'));
const model = fs.readFileSync(path.join(root, 'src/js/model.js'), 'utf8');
const block = (name) => vm.runInNewContext(model.slice(model.indexOf(`const ${name} = [`) + `const ${name} = `.length, model.indexOf('];', model.indexOf(`const ${name} = [`)) + 1));
const skyCameras = block('CAMERAS').filter((c) => c.id !== 'custom');
const skyOptics = block('OPTICS').filter((o) => o.ap && o.fl);

const target = new URL('../assets/catalog/equipment.json', import.meta.url);
const previous = fs.existsSync(target) ? JSON.parse(fs.readFileSync(target, 'utf8')) : { cameras: [], telescopes: [] };
const aliasesOf = (list) => Object.fromEntries(list.map((item) => [item.id, item.aliases ?? []]));
const cameraAliases = aliasesOf(previous.cameras), telescopeAliases = aliasesOf(previous.telescopes);

const key = (name) => String(name).toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '');
const slug = (name) => String(name).toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 60);
const types = { osc: 'color', mono: 'mono', dslr: 'dslr', dslrmod: 'dslrModified' };
const unique = (items) => { const seen = new Set(); return items.filter((item) => { const id = item.id; if (seen.has(id)) return false; seen.add(id); return true; }); };

const cameras = [];
for (const c of skyCameras) cameras.push({
  id: c.id, name: c.name.replace(/\s*\((non raffreddata|camera integrata)\)/, ''), type: types[c.type], sensor: null,
  pixelUm: c.pix, width: c.w, height: c.h, qe: c.qe, readNoise: c.rn, uses: 0, source: 'skyframe',
});
const known = new Set(cameras.map((c) => key(c.name)));
for (const c of astrobin.cameras) {
  if (known.has(key(c.name))) {
    const item = cameras.find((camera) => key(camera.name) === key(c.name));
    item.uses = c.count; item.sensor = c.sensor ? c.sensor.replace(/\s*\((mono|color)\)$/i, '') : null;
    continue;
  }
  const specs = gear.camOf(c.name, c.sensor, c.colorMode);
  const type = specs ? types[specs.type] : c.colorMode === 'mono' ? 'mono' : c.colorMode === 'color' ? 'color' : 'unknown';
  cameras.push({
    id: slug(c.name), name: c.name, type, sensor: c.sensor ? c.sensor.replace(/\s*\((mono|color)\)$/i, '') : null,
    pixelUm: specs?.pix ?? null, width: specs?.w ?? null, height: specs?.h ?? null, qe: specs?.qe ?? null, readNoise: specs?.rn ?? null,
    uses: c.count, source: 'astrobin',
  });
}

const telescopes = [];
for (const o of skyOptics) telescopes.push({
  id: o.id, name: o.name, apertureMm: o.ap, focalMm: o.fl, obstructionPct: o.obs,
  reducers: (o.acc ?? []).map(([name, factor]) => ({ name, factor })), uses: 0, source: 'skyframe',
});
const knownScopes = new Set(telescopes.map((t) => key(t.name)));
for (const t of astrobin.telescopes) {
  if (knownScopes.has(key(t.name))) { telescopes.find((item) => key(item.name) === key(t.name)).uses = t.count; continue; }
  const specs = gear.scopeOf(t.name);
  if (!specs?.fl || !specs.ap) continue;
  telescopes.push({ id: slug(t.name), name: t.name, apertureMm: specs.ap, focalMm: specs.fl, obstructionPct: specs.obs, reducers: [], uses: t.count, source: 'astrobin' });
}

const withAliases = (list, aliases) => unique(list).map((item) => ({ ...item, aliases: aliases[item.id] ?? [] }));
const out = {
  schema: 1,
  origin: 'Skyframe src/js/model.js e scripts/gear.cjs, nomi e diffusione da AstroBin (scripts/data/astrobin-equipment.json)',
  cameras: withAliases(cameras, cameraAliases),
  telescopes: withAliases(telescopes, telescopeAliases),
};
fs.writeFileSync(target, JSON.stringify(out, null, 0).replace(/\},\{"id"/g, '},\n{"id"').replace(/\[\{"id"/g, '[\n{"id"') + '\n');
console.log(`${out.cameras.length} cameras, ${out.telescopes.length} telescopes written`);
