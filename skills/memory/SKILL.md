# Memory skill

Use SQLite FTS5 for searchable memory. Store concise, grounded facts with
realm, agent, confidence, timestamps, and a source reference. Do not copy
whole policies, tax returns, or long transcripts into memory.

The generated section of an agent's `MEMORY.md` is materialized from SQLite;
preserve human-maintained content outside its markers. Checkpoint unresolved
work before a clear or hard session rotation.
