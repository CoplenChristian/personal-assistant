# Harness server

The ASP.NET Core host for Phase 0A. It owns Kestrel binding, dependency
composition, endpoint mapping, and database wiring while delegating
settings/control-plane behavior to the C# harness library. Native runtime and
WebSocket work remain deferred.

Phase 0 implementation should keep the server bound to localhost by default
and route consequential operations through `packages/capability-broker`.
