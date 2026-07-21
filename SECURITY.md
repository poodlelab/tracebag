# Security Policy

Tracebag is security-sensitive software: it reads Docker logs, controls
temporary diagnostic containers, and may collect traces or dumps containing
application data.

## Supported versions

| Version | Supported |
| --- | --- |
| 0.1.x | Yes |
| Earlier development builds | No |

Security fixes apply to the latest patch release. Development revisions from
`main` are not supported deployment versions.

## Reporting a vulnerability

Do not disclose a suspected vulnerability in a public issue. Use the
[repository's private security-advisory reporting channel](https://github.com/poodlelab/tracebag/security/advisories/new)
and include:

- affected revision or image version;
- reproduction steps;
- expected and observed behavior;
- likely impact;
- any suggested mitigation.

Never include real credentials, dumps, traces, or customer logs in a report.

## Docker socket warning

An unrestricted Docker socket gives Tracebag capabilities that are effectively
host-level. Label filtering, fixed runner profiles, authentication, and CSRF
protection reduce remote and accidental misuse, but they do not make the Docker
socket a low-privilege interface.

Required operational controls:

- bind Tracebag to localhost by default;
- use HTTPS through a trusted reverse proxy for remote access;
- keep authentication enabled;
- use a unique strong administrator password;
- keep the application port private and trust forwarded headers only from the
  exact reverse-proxy IP or controlled network;
- never expose PostgreSQL publicly;
- opt in only intended target containers;
- keep restart and full-dump operations disabled unless needed;
- protect and rotate backups, artifacts, and data-protection keys;
- update Tracebag and its runner images regularly.

Tracebag bounds login request bodies and credential fields, performs one
password-hash verification for every syntactically valid credential failure,
and returns the same public error for an unknown user and an incorrect
password. A fixed-window per-client login limit returns HTTP 429 without
queueing excess requests. This is a defense-in-depth control, not a substitute
for a strong password, a private application port, HTTPS, or an upstream access
control layer.

Forwarded client addresses and HTTPS scheme headers are accepted only from
loopback or `TRACEBAG_TRUSTED_PROXIES`. Never configure a catch-all trusted
network on an interface an untrusted peer can reach: doing so allows spoofed
client addresses and defeats per-client partitioning. The application and
published proxy examples emit CSP, frame-denial, MIME-sniffing, referrer,
permissions-policy, and HTTPS HSTS headers.

Temporary diagnostic runners share one reviewed baseline: no network, a
read-only root filesystem, all capabilities dropped, `no-new-privileges`, init,
and bounded memory, CPU, and process counts. They receive neither the Docker
socket nor arbitrary mounts; only durable jobs receive the artifact volume.
These controls limit a diagnostic tool failure but do not reduce the privilege
of Tracebag's own Docker socket.

## Diagnostic data

Logs, traces, GC dumps, full process dumps, stacks, and exported bundles can
contain secrets or personal data. Tracebag stores them locally by default, but
the operator remains responsible for access, retention, backups, and deletion.

Tracebag analysis runs locally and does not send diagnostic data to external
analysis providers.

PostgreSQL audit events default to 30-day and 100,000-event limits. Cleanup is
indexed and batched. Operators with stricter data requirements should shorten
these limits and align backup retention separately; deletion from the live
database does not remove an event from an older backup.

Incidents are never deleted automatically. Explicit deletion requires the exact
incident ID and is blocked while capture or analysis is active. It permanently
removes incident-owned summaries and findings, but only releases linked raw
captures to their separately configured retention. Backups and previously
exported Tracebags remain the operator's responsibility.
