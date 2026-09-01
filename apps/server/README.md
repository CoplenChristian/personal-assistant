# Harness server

The ASP.NET Core host for Phases 0A and 0B. It owns Kestrel binding,
dependency composition, endpoint mapping, and database wiring while delegating
settings and native-agent lifecycle behavior to the C# harness library.
Terminal streaming and WebSocket work remain deferred.

Phase 0 implementation should keep the server bound to localhost by default
and route consequential operations through `packages/capability-broker`.
