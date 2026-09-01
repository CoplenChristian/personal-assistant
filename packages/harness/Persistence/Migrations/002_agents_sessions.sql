CREATE TABLE IF NOT EXISTS agents (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    runtime TEXT NOT NULL CHECK (runtime IN ('claude', 'codex')),
    working_directory TEXT NOT NULL,
    realms_json TEXT NOT NULL,
    skills_json TEXT NOT NULL,
    browser_profile TEXT,
    memory_scope TEXT,
    scheduled_task_permissions_json TEXT NOT NULL,
    auto_start INTEGER NOT NULL CHECK (auto_start IN (0, 1)),
    desired_state TEXT NOT NULL CHECK (desired_state IN ('running', 'stopped')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL REFERENCES agents(id),
    tmux_session_name TEXT NOT NULL UNIQUE,
    runtime TEXT NOT NULL CHECK (runtime IN ('claude', 'codex')),
    native_conversation_ref TEXT,
    observed_state TEXT NOT NULL CHECK (
        observed_state IN ('missing', 'starting', 'running', 'exited', 'error')
    ),
    started_at TEXT,
    last_seen_at TEXT,
    stopped_at TEXT,
    last_error TEXT,
    UNIQUE (agent_id)
);
