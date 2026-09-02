# Harness server

The ASP.NET Core host for Phases 0A through 0C. It owns Kestrel binding,
dependency composition, endpoint mapping, and database wiring while delegating
settings and native-agent lifecycle behavior to the C# harness library.
Phase 0C exposes the personal canonical-screen WebSocket, serialized input
boundary, and checkpoint-gated session hygiene endpoints; activity endpoints
remain deferred.

Phase 0 implementation should keep the server bound to localhost by default
and route consequential operations through `packages/capability-broker`.
