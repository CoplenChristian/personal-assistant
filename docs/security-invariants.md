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
23. Tracked memory, handoff, user-context, transcript, cache, profile, screenshot, and download files are never populated private state.
24. An unconfigured or policy-invalid tmux session receives no capabilities.
25. Roster changes do not grant capabilities; they only update explicitly validated agent state.
26. Capability requests use a server-issued, expiring authorization context; model-supplied provenance cannot widen it.
27. The broker is not considered a hard integration boundary until native-agent alternate paths are closed or the integration remains disabled/manual-only.

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

Dynamic agent definitions and local overrides follow the same rule: a runtime
definition must be validated before it can become active, and the presence of
a pa-* tmux session alone never grants access.

## Settings boundary

The Settings API may expose effective locked safety values for visibility, but
it must not expose writable representations of those values. Unknown keys,
invalid values, immutable keys, bootstrap keys, and sensitive values are
rejected server-side. User preferences are SQLite overrides; Git-tracked YAML
remains the repository default source.

Capability requests also require a trusted turn context containing the source,
initiator, agent/session binding, realm, allowed capabilities, source
reference, and expiry. The broker authorizes the intersection of global hard
policy, agent upper bound, turn context, and scheduled-job subset when
applicable. It must not accept those fields as assertions from model output.

## Scheduled capability boundary

An agent's scheduled permissions are an upper bound. Each scheduled job must
declare its own subset, and the broker must enforce both the job subset and the
agent/global policy. Scheduled jobs target existing logical agents and do not
inherit every agent write permission automatically.

A reply to an inbound message is bound to a concrete verified inbound message
reference. A future proactive notification is a separate, verified-contact
operation.

## Privacy boundary

The public repository contains portable instructions and templates only. The
instantiated files runtime/agents/<id>/MEMORY.md,
runtime/agents/<id>/HANDOFF.md, and runtime/shared/USER.md are ignored runtime
state; their tracked counterparts are templates. Runtime databases, transcripts,
caches, browser profiles, screenshots, downloads, and personal documents
remain outside Git.

The planned privacy check is deterministic and local. It must inspect the
staged file set and fail closed on forbidden paths or credential-shaped
content; it must not use an LLM to decide whether a file is private.

## Skill and persona boundary

The canonical skills catalog is procedural guidance, not authorization. All
ingress paths use deterministic trigger matching and eligible-skill filtering
before native skill discovery. The shared SOUL.md is persona only; its
OpenClaw source/version/commit and local review must be recorded before Phase 1
and it must not contain operational permissions.

## Required blocked-action visibility

Security rejections are first-class activity events and should be obvious in
the dashboard. Examples include:

~~~text
BLOCKED: outbound iMessage to unverified destination
BLOCKED: work agent requested a personal account
BLOCKED: email send operation is unavailable
BLOCKED: setting attempted to weaken a locked invariant
BLOCKED: scheduled job requested a capability outside its explicit subset
BLOCKED: capability request has expired or mismatched turn context
BLOCKED: integration alternate path is not contained
~~~

See [threat-model.md](threat-model.md) for the native-agent trust model and
the pre-integration security gate.
