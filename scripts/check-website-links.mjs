#!/usr/bin/env node

import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { extname, join, relative, resolve } from 'node:path';

const repositoryRoot = resolve(import.meta.dirname, '..');
const outputRoot = join(repositoryRoot, 'website', 'dist');
const basePath = '/tracebag/';

if (!existsSync(outputRoot)) {
  console.error('Website output is missing. Run npm run build --prefix website first.');
  process.exit(1);
}

function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}

function targetExists(url) {
  const withoutQuery = url.split(/[?#]/, 1)[0];
  const path = withoutQuery.startsWith(basePath)
    ? withoutQuery.slice(basePath.length)
    : withoutQuery.replace(/^\//, '');
  const decoded = decodeURIComponent(path);
  const candidate = join(outputRoot, decoded);

  if (decoded === '') return existsSync(join(outputRoot, 'index.html'));
  if (extname(candidate)) return existsSync(candidate);
  return existsSync(candidate) || existsSync(join(candidate, 'index.html')) || existsSync(`${candidate}.html`);
}

const failures = [];
for (const file of walk(outputRoot).filter((path) => path.endsWith('.html'))) {
  const html = readFileSync(file, 'utf8');
  const links = [...html.matchAll(/(?:href|src)=["']([^"']+)["']/g)].map((match) => match[1]);
  for (const link of links) {
    if (/^(?:https?:|mailto:|tel:|data:|#)/.test(link)) continue;
    if (!targetExists(link)) failures.push(`${relative(outputRoot, file)} -> ${link}`);
  }
}

if (failures.length > 0) {
  console.error('Broken generated website links:');
  failures.forEach((failure) => console.error(`  ${failure}`));
  process.exit(1);
}

console.log('Generated website links are valid.');
