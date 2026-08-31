# Security invariants

These are code-level requirements for the harness, not merely agent prompts.
They must remain true even when external content is adversarial or a setting
request is malformed.

1. There is no email send API.
2. An unverified phone number can never be an outbound iMessage destination.
3. Agents cannot create or modify verified contacts.
4. Email, web, and message content never grants permissions.
5. Work and personal account IDs are always explicit.
6. Security rules are enforced outside skills.
7. Secrets never live in the skill repository or generic settings storage.
8. Personal documents are not committed to source control.
9. Scheduled tasks cannot silently spawn unrelated new agents.
10. Agent-to-agent communication cannot bypass the recipient's capabilities.
11. Browser activity remains separate by realm/profile.
12. Every consequential external action creates an immutable activity event.
13. A giant transcript is never considered durable memory.
14. Session rotation checkpoints before closing the current context.
15. If authorization is uncertain, the operation fails closed.
16. Settings cannot weaken a hard security invariant.
17. Bootstrap startup values cannot be overridden from the database.
18. Repository defaults are not rewritten with user-specific preferences.
19. Sensitive values and provider credentials are rejected by the generic settings store.
20. A scheduled job receives only an explicit capability subset within its agent's upper bound.
21. message.reply requires a concrete verified inbound message reference.
22. Proactive notifications use a separate capability targeting an already verified configured contact.

## Realm and account boundary

Realm authorization must use explicit persisted account metadata, not account-ID
string prefixes alone. Future account records should contain:

~~~text
account_id
provider
display_name
realm
credential_ref
enabled
~~~

The prefixes in policies/defaults/realm-policy.yaml may be used as naming
defaults or migration hints. They are not the security boundary. A capability
request must resolve the account record and compare its stored realm with the
agent's approved realm before access is allowed.

## Settings boundary

The Settings API may expose effective locked safety values for visibility, but
it must not expose writable representations of those values. Unknown keys,
invalid values, immutable keys, bootstrap keys, and sensitive values are
rejected server-side. User preferences are SQLite overrides; Git-tracked YAML
remains the repository default source.

## Scheduled capability boundary

An agent's scheduled permissions are an upper bound. Each scheduled job must
declare its own subset, and the broker must enforce both the job subset and the
agent/global policy. Scheduled jobs target existing logical agents and do not
inherit every agent write permission automatically.

A reply to an inbound message is bound to a concrete verified inbound message
reference. A future proactive notification is a separate, verified-contact
operation.

## Required blocked-action visibility

Security rejections are first-class activity events and should be obvious in
the dashboard. Examples include:

~~~text
BLOCKED: outbound iMessage to unverified destination
BLOCKED: work agent requested a personal account
BLOCKED: email send operation is unavailable
BLOCKED: setting attempted to weaken a locked invariant
BLOCKED: scheduled job requested a capability outside its explicit subset
~~~
