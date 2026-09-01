CREATE TABLE IF NOT EXISTS settings_overrides (
    scope_type TEXT NOT NULL
        CHECK (scope_type IN ('global', 'realm', 'agent', 'integration')),
    scope_id TEXT NOT NULL DEFAULT '',
    key TEXT NOT NULL,
    value_json TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (scope_type, scope_id, key),
    CHECK (
        (scope_type = 'global' AND scope_id = '')
        OR
        (scope_type <> 'global' AND length(scope_id) > 0)
    )
);

CREATE TABLE IF NOT EXISTS activity_events (
    id TEXT PRIMARY KEY,
    timestamp TEXT NOT NULL,
    agent_id TEXT,
    realm TEXT,
    category TEXT NOT NULL,
    operation TEXT NOT NULL,
    target TEXT,
    status TEXT NOT NULL,
    duration_ms INTEGER,
    metadata_json TEXT NOT NULL
);
