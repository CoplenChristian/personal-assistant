# Memory checkpoint hook

Before a clear or hard rotation, request a durable checkpoint, persist
unresolved work to the handoff location, record the native session ID, and
only then close the current native conversation.
