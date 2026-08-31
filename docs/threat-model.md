# Local-trust threat model

This document defines the deployment assumptions and the narrower security
claim for the Personal Assistant Harness. It replaces the earlier design that
attempted to authorize individual model turns.

## Deployment and access

This is a single-user personal assistant running on one Mac. Other native
local harnesses such as Claude Code, Codex, and similar tools may run under the
same macOS user.

Remote dashboard access is intended to use Tailscale or another trusted local
network:

~~~text
Internet
   X

Tailscale / trusted local network
        |
        v
single-user Personal Assistant dashboard
        |
        v
local harness and native local agents
~~~

The server retains a safe bind configuration and must not be casually exposed
to the public internet. This is not a cloud multi-user application, so there
is no signup, registration, tenant, organization, multi-user RBAC, password
reset, or public OAuth login design.

## Trust model

Native Claude Code, Codex, and other local agent harnesses are trusted to the
same extent as the macOS user running them. They are not separately
authenticated security principals, and this application does not claim to
sandbox them.

Email, web pages, documents, and inbound messages remain untrusted input.
System/agent instructions, skill procedures, narrow APIs, deterministic
capability restrictions, and explicit user-facing confirmations provide
defense in depth against untrusted content.

## What the harness guarantees

The harness guarantees that operations invoked through its pa CLI and
capability broker obey deterministic product guardrails:

- unsupported capabilities are unavailable by construction;
- realm/account checks use explicit stored account metadata;
- credentials are kept in Keychain or integration-specific protected state;
- provider-specific restrictions are applied at the integration boundary;
- consequential broker operations are audited; and
- blocked operations are visible in activity.

For example, pa mail send fails because no email-send capability exists.

## What the harness does not guarantee

A separately trusted local Claude Code, Codex, or other process may independently
open Gmail in a browser, invoke another provider CLI, use another browser
profile, query an OS API, or otherwise act with the privileges of the same
macOS user. That action is outside the Personal Assistant Harness security
boundary.

The harness therefore does not claim to prevent unrelated local applications or
agent harnesses from performing actions that the pa broker would block. It
protects and constrains harness-managed capabilities; it does not turn the Mac
into a complete sandbox.

## Broker role

Keep the pa CLI and capability broker as the stable local integration
interface:

~~~text
pa mail ...
pa message ...
pa reminder ...
pa calendar ...
pa memory ...
pa documents ...
pa agents ...
pa browser ...
~~~

The broker centralizes integration abstraction, validation, explicit
realm/account checks, secrets handling, and activity recording. It does not
implement IAM around Claude/Codex or make an independent decision about the
meaning of every natural-language model turn.

## Deterministic guardrails

The following remain capability/API constraints:

### Email

There is no mail.send, mail.reply, mail.forward, or draft.send capability.
Mail is read/organize only.

### Messaging

Outbound BlueBubbles operations require verified contact IDs. Raw arbitrary
phone numbers, Apple-ID email recipients, groups, agent-created contacts, and
attachments are disabled initially. Outbound and blocked operations are
audited. message.reply requires a concrete verified inbound message reference.

### Realms and accounts

Future account records contain account_id, provider, display_name, realm,
credential_ref, and enabled. The stored realm is the authorization boundary;
account-name prefixes are naming defaults or migration hints only. There is no
silent fallback between work and personal accounts.

### Secrets and privacy

Credentials never live in skills, Git, generic Settings storage, prompts, or
runtime logs. Generated memory, handoffs, local overrides, transcripts,
browser profiles, caches, screenshots, downloads, and databases stay in the
ignored runtime layout. Personal documents stay in the external vault.

## External content

External content may inform an answer, but skills and agent instructions tell
the native runtime not to treat it as trusted operating guidance or permission
to perform unrelated actions.

The enforcement model is intentionally layered:

~~~text
human/network application boundary
        +
agent/system instructions
        +
deterministic broker restrictions
        +
narrow provider APIs
        +
explicit confirmations where useful
        +
activity audit
~~~

This is defense in depth for harness-managed actions, not a claim of perfect
containment around a trusted local macOS user.

## Scheduler

Scheduled jobs still declare an explicit allowed_capabilities list. The
scheduler/broker checks:

~~~text
job capability subset
    subset of
agent scheduled capability upper bound
    subset of
global capability policy
~~~

Jobs target existing logical agents and inject into their existing context.
The scheduler applies the declared job subset and ordinary broker policy; it
does not create a separate security identity for each run.

## Browser implication

The browser capability provided by this harness follows its normal
realm/profile restrictions, domain policies, visible activity, and untrusted
website-content rules.

The harness does not claim that a separate local Claude Code/Codex instance
cannot open another browser profile or application. Browser restrictions apply
to harness-managed browser operations only.

## Normal integration readiness

Before enabling an integration, review ordinary credential handling, provider
permissions, account realm checks, narrow API behavior, blocked-action tests,
and audit coverage. Do not require proof that every unrelated path on the Mac
has been closed.

If the harness cannot safely constrain the operation through its own
capability interface, keep that capability disabled or manual-only. Stronger
process isolation or separate macOS accounts remain optional future hardening,
not a prerequisite for the normal integration roadmap.

See docs/security-invariants.md and docs/privacy.md for the related boundaries.
