# Dashboard

React/Vite local dashboard for the Phase 0A Settings, Phase 0B agent lifecycle,
and Phase 0C T1/T2 terminal slices. It loads ASP.NET Core settings/agent APIs
and renders server metadata rather than maintaining a second settings or
lifecycle policy list.

The terminal surface renders a fixed-geometry canonical screen and submits
serialized input through the harness WebSocket boundary; activity aggregation
and session hygiene remain deferred. This UI does not probe providers, store
credentials, or invent connected integration state.
