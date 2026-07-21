# AI-assisted development

Tracebag is developed with substantial assistance from AI coding tools. This
includes design exploration, implementation, debugging, tests, documentation,
repository maintenance, and visual-asset iteration.

The current maintainer primarily uses OpenAI Codex and OpenAI image-generation
tools. Tooling may change over time; the responsibilities described here do not.

## Human ownership

AI tools are assistants, not maintainers or project authors. The human
maintainer remains responsible for:

- product direction and scope;
- architecture and security decisions;
- understanding and reviewing every accepted change;
- verifying behavior with tests and direct inspection;
- dependency, license, and provenance review;
- releases, vulnerability handling, and ongoing maintenance.

AI output is treated as an untrusted draft. A model's explanation, test claim,
or confidence is not accepted as evidence that a change is correct.

## Engineering standard

AI-assisted work must meet the same standard as any other contribution:

- changes are focused, explainable, and consistent with the architecture;
- security boundaries are preserved and tested;
- generated tests exercise behavior rather than merely mirroring an
  implementation;
- verification commands are actually run and their failures are resolved;
- user-facing claims are supported by implemented and observed behavior;
- temporary scaffolding, speculative abstractions, and generic filler are
  removed before review;
- third-party code or content is not reproduced without compatible licensing
  and attribution.

Tracebag is security-sensitive software. AI assistance must never be used as a
reason to weaken label opt-in, authentication, CSRF protection, capture limits,
runner isolation, artifact safeguards, or the prohibition on arbitrary shell
and Docker commands.

## Data handling

Do not provide production logs, traces, dumps, incident exports, credentials,
customer data, private source code, or other confidential material to an
external AI service. Use synthetic reproductions and redacted examples.

Repository content is public once contributed. Contributors are responsible for
understanding the data-handling terms of any tool they use.

## Contribution disclosure

Contributors are welcome to use AI tools. Significant assistance should be
disclosed in the pull-request description, including the tools used, the areas
they influenced, and the human verification performed. Incidental completion or
spelling assistance does not require a detailed disclosure.

For a commit materially shaped by an AI tool, the preferred trailer is:

```text
Assisted-by: OpenAI Codex
```

Do not list an AI tool as a `Co-authored-by` contributor. Authorship,
accountability, and the obligation to respond to review remain with the human
submitting the change.

## Repository instructions

[`AGENTS.md`](AGENTS.md) contains the architecture, security, quality, and
verification rules supplied to coding agents. It is committed publicly so that
agent behavior is reviewable and contributors can use the same project context.

This policy is intended to make the development process visible without
lowering the project's engineering or review standards.
