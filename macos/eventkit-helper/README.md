# EventKit helper

Planned small native macOS Swift helper for approved iCloud calendars and
reminder lists. It should expose only narrow operations through the broker:

- list approved calendars
- list approved reminder lists
- create a calendar event
- create a reminder

The helper must request EventKit permission and use stable identifiers chosen
by the human setup flow. It must not expose arbitrary credential access.
