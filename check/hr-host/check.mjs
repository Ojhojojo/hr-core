import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const ops = join(here, '../../ops');
const allowed = new Set(
  readFileSync(join(here, 'develop-exports.txt'), 'utf8')
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean),
);

function walk(dir, out = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) walk(path, out);
    else if (entry.name.endsWith('.ts')) out.push(path);
  }
  return out;
}

function importsFrom(source) {
  const names = [];
  let from = 0;
  while (from < source.length) {
    const a = source.indexOf("from '@hr-host'", from);
    const b = source.indexOf('from "@hr-host"', from);
    const pos = a < 0 ? b : b < 0 ? a : Math.min(a, b);
    if (pos < 0) break;
    const start = Math.max(source.lastIndexOf('import', pos), source.lastIndexOf('export', pos));
    const chunk = source.slice(start, pos);
    const open = chunk.indexOf('{');
    const close = chunk.lastIndexOf('}');
    if (open >= 0 && close > open) {
      for (const part of chunk.slice(open + 1, close).split(',')) {
        const name = part
          .trim()
          .replace(/^type\s+/, '')
          .split(/\s+as\s+/)[0]
          .trim();
        if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(name)) names.push(name);
      }
    }
    from = pos + 1;
  }
  return names;
}

const used = new Set();
for (const file of walk(ops)) {
  for (const name of importsFrom(readFileSync(file, 'utf8'))) used.add(name);
}

const extra = [...used].filter((name) => !allowed.has(name)).sort();
if (extra.length) {
  console.error('New @hr-host imports that develop hosts do not export:');
  for (const name of extra) console.error('  ' + name);
  process.exit(1);
}

const names = [...allowed].sort();
const stub = names.map((name) => `export const ${name}: any = null;`).join('\n') + '\n';
const surface =
  `import {\n${names.map((name) => `  ${name},`).join('\n')}\n} from '@hr-host';\n\nexport const hrHostSurface = [\n${names
    .map((name) => `  ${name},`)
    .join('\n')}\n];\n`;
writeFileSync(join(here, 'stub.ts'), stub);
writeFileSync(join(here, 'surface.ts'), surface);

const tsc = spawnSync(
  'npx',
  ['--yes', '-p', 'typescript@5.9.3', 'tsc', '-p', join(here, 'tsconfig.json')],
  { stdio: 'inherit', cwd: here },
);
process.exit(tsc.status ?? 1);
