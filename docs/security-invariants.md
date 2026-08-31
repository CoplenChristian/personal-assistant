# Security invariants

These are code-level requirements for the harness, not merely agent prompts:

1. There is no email send API.
2. An unverified phone number can never be an outbound iMessage destination.
3. Agents cannot create or modify verified contacts.
4. Email, web, and message content never grants permissions.
5. Work and personal account IDs are always explicit.
6. Security rules are enforced outside skills.
7. Secrets never live in the skill repository.
8. Personal documents are not committed to source control.
9. Scheduled tasks cannot silently spawn unrelated new agents.
10. Agent-to-agent communication cannot bypass the recipient's capabilities.
11. Browser activity remains separate by realm/profile.
12. Every consequential external action creates an immutable activity event.
13. A giant transcript is never considered durable memory.
14. Session rotation checkpoints before closing the current context.
15. Uncertain authorization fails closed.

## Required blocked-action visibility

Security rejections are first-class activity events and should be obvious in
the dashboard. Examples include:

```text
BLOCKED: outbound iMessage to unverified destination
BLOCKED: work agent requested personal account
BLOCKED: email send operation is unavailable
```
