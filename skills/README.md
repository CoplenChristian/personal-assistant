# Canonical skills catalog

The repository owns one canonical skill catalog. Each skill has procedural
instructions and metadata, but never credentials or authorization decisions.

The planned activation path is:

~~~text
dashboard / iMessage / scheduler / agent-message ingress
        -> normalize source and realm
        -> deterministic trigger matcher
        -> eligible agent skills
        -> prompt/context injection
        -> native Claude/Codex skill discovery
~~~

The trigger matcher is rule- or keyword-based. No second LLM is introduced
solely to route prompts. If Claude Code and Codex require different native
skill layouts, adapters, symlinks, or generated views may be produced from
this catalog; the source of truth remains this directory.

Current catalog:

| Skill | Purpose | Future capability boundary |
| --- | --- | --- |
| email | Search/read/organize mail | mail broker, never send |
| messaging | Verified replies/sends | verified-contact broker |
| reminders | Calendar/reminder requests | EventKit helper |
| documents | Vault search with provenance | document index |
| memory | Search and checkpoint durable memory | SQLite/FTS5 |
| agents | Roster lookup and agent messages | AgentRegistry/broker |
| browser | Controlled realm-specific browsing | BrowserProvider |
