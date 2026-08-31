# Local runtime state

This directory is intentionally ignored except for this marker. A running
harness may create SQLite state, logs, roster metadata, and native session
state here. Do not commit its contents, credentials, live allowlists, or
personal documents.

The planned privacy-safe layout is:

~~~text
runtime/
  assistant.sqlite
  roster.json
  agents/<agent-id>/
    agent.yaml          # local/dynamic agent definition or override
    AGENTS.md           # local agent instructions, when private
    MEMORY.md           # generated/materialized memory
    HANDOFF.md          # clear/rotation handoff
    local/              # mutable agent overrides
    transcripts/        # raw native-session artifacts
  browser-profiles/<realm>/
  mail-cache/
  screenshots/
  downloads/
  session-state/
~~

The tracked agents directory contains safe definitions and templates only.
Instantiate MEMORY.template.md and HANDOFF.template.md into the corresponding
ignored runtime agent directory. The separate PersonalAssistantVault remains
outside this repository and is never indexed by the settings planning work.
