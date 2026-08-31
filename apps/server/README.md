# Harness server

The future local Node.js service. It will own the HTTP/WebSocket boundary,
agent registry, session manager, scheduler, activity bus, and database wiring.

Phase 0 implementation should keep the server bound to localhost by default
and route consequential operations through `packages/capability-broker`.
