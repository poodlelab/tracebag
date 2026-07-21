#!/usr/bin/env node

import fs from 'node:fs';

const [reportPath, imageName = 'image'] = process.argv.slice(2);
if (!reportPath) {
  console.error('Usage: node scripts/evaluate-trivy-report.mjs <trivy-report.json> [image]');
  process.exit(64);
}

const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
const findings = (report.Results ?? [])
  .flatMap((result) => result.Vulnerabilities ?? [])
  .filter((finding) => finding.Severity === 'HIGH' || finding.Severity === 'CRITICAL');
const fixable = findings.filter((finding) => Boolean(finding.FixedVersion));
const awaitingUpstreamFix = findings.filter((finding) => !finding.FixedVersion);

console.log(`${imageName}: ${findings.length} high/critical findings; ${fixable.length} fixable; ${awaitingUpstreamFix.length} awaiting an upstream fix.`);
if (awaitingUpstreamFix.length > 0) {
  console.log('Unfixed findings are retained in the full report and governed by docs/supply-chain-security.md.');
}
if (fixable.length > 0) {
  for (const finding of fixable) {
    console.error(`${finding.Severity} ${finding.VulnerabilityID} ${finding.PkgName} ${finding.InstalledVersion} -> ${finding.FixedVersion}`);
  }
  console.error('Fixable high/critical image vulnerabilities are not permitted.');
  process.exit(1);
}
