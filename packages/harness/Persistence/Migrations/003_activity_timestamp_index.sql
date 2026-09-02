ALTER TABLE activity_events ADD COLUMN timestamp_utc_ms INTEGER;

CREATE INDEX IF NOT EXISTS ix_activity_events_timestamp_utc_ms_id
    ON activity_events(timestamp_utc_ms, id);
