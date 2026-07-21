#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const outputIndex = process.argv.indexOf('--output');
const outputPath = outputIndex >= 0 ? process.argv[outputIndex + 1] : null;
if (outputIndex >= 0 && !outputPath) {
  console.error('--output requires a file path.');
  process.exit(64);
}

const forbidden = /(?:^|\b)(?:AGPL|SSPL|BUSL|Elastic-2\.0|Commons-Clause|GPL-[23]\.0-(?:only|or-later))(?:\b|$)/i;
const components = [];
const xmlEntities = new Map([
  ['&amp;', '&'],
  ['&lt;', '<'],
  ['&gt;', '>'],
  ['&quot;', '"'],
  ['&apos;', "'"]
]);

function decodeXml(value) {
  return value.replace(/&(?:amp|lt|gt|quot|apos);/g, (entity) => xmlEntities.get(entity));
}

function classifyLicenseText(value) {
  if (/Apache License[\s\S]*Version 2\.0/i.test(value)) return 'Apache-2.0';
  if (/MIT License|Permission is hereby granted, free of charge/i.test(value)) return 'MIT';
  if (/PostgreSQL License/i.test(value)) return 'PostgreSQL';
  return null;
}

function nugetLicense(id, version) {
  const packageRoot = path.join(os.homedir(), '.nuget', 'packages', id.toLowerCase(), version.toLowerCase());
  const nuspecPath = path.join(packageRoot, `${id.toLowerCase()}.nuspec`);
  const nuspec = fs.readFileSync(nuspecPath, 'utf8');
  const license = nuspec.match(/<license\s+type="([^"]+)">([^<]+)<\/license>/i);
  if (license?.[1].toLowerCase() === 'expression') return decodeXml(license[2].trim());
  if (license?.[1].toLowerCase() === 'file') {
    const licenseFile = path.join(packageRoot, decodeXml(license[2].trim()));
    if (fs.existsSync(licenseFile)) {
      const classified = classifyLicenseText(fs.readFileSync(licenseFile, 'utf8'));
      if (classified) return classified;
    }
  }
  const licenseUrl = decodeXml(nuspec.match(/<licenseUrl>([^<]+)<\/licenseUrl>/i)?.[1] ?? '');
  if (/github\.com\/dotnet\/corefx\/.*license/i.test(licenseUrl)) return 'MIT';
  if (/xunit\/.*license/i.test(licenseUrl)) return 'Apache-2.0';
  const urlExpression = licenseUrl.match(/licenses\.nuget\.org\/([^/?#]+)/i)?.[1];
  return urlExpression ? decodeURIComponent(urlExpression) : null;
}

const nugetJson = JSON.parse(execFileSync('dotnet', [
  'list', 'Tracebag.slnx', 'package', '--include-transitive', '--format', 'json', '--output-version', '1'
], { cwd: repositoryRoot, encoding: 'utf8' }));

const nugetPackages = (nugetJson.projects ?? []).flatMap((project) =>
  (project.frameworks ?? []).flatMap((framework) => [
    ...(framework.topLevelPackages ?? []),
    ...(framework.transitivePackages ?? [])
  ]));
for (const dependency of nugetPackages) {
  components.push({ ecosystem: 'nuget', name: dependency.id, version: dependency.resolvedVersion, license: nugetLicense(dependency.id, dependency.resolvedVersion) });
}

for (const lockPath of ['src/Tracebag.Web/package-lock.json', 'website/package-lock.json']) {
  const lock = JSON.parse(fs.readFileSync(path.join(repositoryRoot, lockPath), 'utf8'));
  for (const [packagePath, metadata] of Object.entries(lock.packages ?? {})) {
    if (!packagePath || !metadata.version) continue;
    const name = packagePath.split('node_modules/').at(-1);
    components.push({ ecosystem: 'npm', source: lockPath, name, version: metadata.version, license: metadata.license ?? null, developmentOnly: metadata.dev === true });
  }
}

const runnerTools = [
  ['dotnet-counters', '9.0.661903'], ['dotnet-trace', '9.0.661903'], ['dotnet-dump', '9.0.661903'], ['dotnet-gcdump', '9.0.661903'], ['dotnet-stack', '9.0.661903']
];
for (const [name, version] of runnerTools) components.push({ ecosystem: 'dotnet-tool', name, version, license: 'MIT', source: 'runners/' });

const unique = [...new Map(components.map((component) => [`${component.ecosystem}:${component.name}@${component.version}:${component.source ?? ''}`, component])).values()]
  .sort((left, right) => `${left.ecosystem}:${left.name}:${left.version}`.localeCompare(`${right.ecosystem}:${right.name}:${right.version}`));
const missing = unique.filter((component) => !component.license);
const forbiddenComponents = unique.filter((component) => forbidden.test(component.license ?? ''));
const report = {
  schemaVersion: 1,
  generatedAt: new Date().toISOString(),
  policy: {
    missingLicenseMetadata: 'deny',
    forbiddenLicenseFamilies: ['AGPL', 'SSPL', 'BUSL', 'Elastic-2.0', 'Commons-Clause', 'GPL-2.0-only/or-later', 'GPL-3.0-only/or-later'],
    reviewedBuildToolLicenses: ['LGPL-3.0-or-later', 'MPL-2.0', 'Python-2.0', 'CC-BY-3.0', 'CC-BY-4.0']
  },
  summary: { components: unique.length, missing: missing.length, forbidden: forbiddenComponents.length },
  components: unique
};

const serialized = `${JSON.stringify(report, null, 2)}\n`;
if (outputPath) {
  fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });
  fs.writeFileSync(outputPath, serialized);
} else {
  process.stdout.write(serialized);
}
console.log(`Reviewed ${unique.length} NuGet, npm, and diagnostic-tool component records.`);
if (missing.length > 0 || forbiddenComponents.length > 0) {
  for (const component of [...missing, ...forbiddenComponents]) console.error(`${component.ecosystem}:${component.name}@${component.version}: ${component.license ?? 'missing license'}`);
  process.exit(1);
}
