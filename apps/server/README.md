# Harness server

The future ASP.NET Core host. It will own Kestrel binding, dependency
composition, the HTTP/WebSocket boundary, endpoint mapping, and database
wiring while delegating settings/control-plane behavior to the C# harness
library.

Phase 0 implementation should keep the server bound to localhost by default
and route consequential operations through `packages/capability-broker`.
