# Privacy and file layout

This repository is public source code. It must contain portable implementation
material only, not personal state.

## Ownership rule

~~~text
tracked Git content
  -> code, policy defaults, reviewed instructions, skill procedures, templates

ignored runtime content
  -> user preferences, generated memory, handoffs, transcripts, caches,
     browser profiles, screenshots, downloads, roster/session state

macOS Keychain
  -> provider credentials, tokens, and credential references

external local vault
  -> personal documents
~~~

A private or local runtime directory is not made safe by the repository's
GitHub visibility. The repository must remain safe if every tracked file is
publicly readable.

## Tracked templates

Tracked templates contain no populated personal context:

~~~text
agents/<id>/MEMORY.template.md
agents/<id>/HANDOFF.template.md
shared/USER.template.md
~~~

At runtime, the harness materializes them into:

~~~text
runtime/agents/<id>/MEMORY.md
runtime/agents/<id>/HANDOFF.md
runtime/shared/USER.md
~~~

Human-maintained content outside generated markers must be preserved in the
runtime files, not copied back into the templates automatically.

## Ignored runtime material

The following belongs outside tracked source:

- generated/materialized agent memory;
- clear/rotation handoffs;
- local agent definitions and overrides that contain private paths/context;
- native transcripts and terminal/session artifacts;
- browser profiles and browser credentials/state;
- mail caches and downloaded message content;
- screenshots, browser downloads, and exported artifacts;
- credential/state artifacts such as p12/pfx/token files, storage state,
  cookies, credentials, secrets, OAuth, token, and JSONL directories/files;
- SQLite databases, roster snapshots, logs, and session state; and
- personal documents in PersonalAssistantVault or another external vault.

The .gitignore rules are a guardrail, not the security boundary. Code review
and the privacy check must still inspect the staged file set.

## Planned privacy check

Before a commit or public push, add a deterministic npm run privacy-check
command. It should run without network access and fail closed when:

- tracked or staged paths match runtime, cache, transcript, browser-profile,
  screenshot, download, vault, or generated-memory patterns;
- tracked files contain credential-shaped material or private key markers;
- tracked paths contain credential/state artifacts such as p12, pfx, token,
  storage-state, cookies, credentials, secrets, OAuth, tokens, or JSONL files;
- instantiated MEMORY.md, HANDOFF.md, or USER.md files appear outside ignored
  runtime paths; or
- a generated runtime artifact is being force-added despite ignore rules.

The command should report the exact path and reason for every rejection. It
must not upload files or attempt to infer whether content is sensitive by using
an LLM.

## Public-repository review checklist

Before changing visibility or pushing:

- [ ] git status and the staged file list are known.
- [ ] No runtime database, log, transcript, cache, profile, screenshot, or download is staged.
- [ ] No populated memory, handoff, or user-context file is staged.
- [ ] No provider credential or token is present.
- [ ] Personal documents remain outside the repository.
- [ ] The privacy check passes.
