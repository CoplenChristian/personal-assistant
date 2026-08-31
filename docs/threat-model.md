# Authorization context and threat model

This is a planning gate for the architecture. It defines what the capability
broker can prove and what it cannot safely claim while native agents have
shell or browser access.

## Security claim

The capability broker is the hard authorization boundary for integrations only
when native agents cannot access integration credentials or equivalent
alternate execution paths. Native Claude/Codex processes are reasoning
runtimes, not trusted security principals.

The plan therefore treats native agents as partially trusted:

- they may reason, use approved native tools, and request broker capabilities;
- they may be exposed to malicious external content;
- they must not receive provider credentials or broker signing secrets; and
- they must not be able to reach an integration through a second path that
  bypasses broker policy.

Email, web, and inbound message content is untrusted. Human ingress and
explicit human administration are trusted sources, subject to the hard global
policy.

## Trusted authorization context

Every agent turn that can result in a capability request receives a
server-issued authorization context:

~~~text
turn_context_id
source
initiated_by
agent_id
session_id
realm
allowed_capabilities
source_reference
expires_at
~~~

Source-specific references may include:

~~~text
scheduled_job_id
verified_sender_contact_id
verified_inbound_message_id
dashboard_request_id
~~~

The context is persisted or otherwise verifiable by the harness. The broker
does not trust model-supplied values for source, initiator, realm, or allowed
capabilities. The request must carry an opaque broker-issued context
credential bound to the agent and native session; the credential expires and
cannot be widened by the model.

Examples:

| Source | Trusted authority |
| --- | --- |
| dashboard_user | The trusted dashboard ingress derives the allowed capability set from the explicit user action/request |
| scheduled_job | The named job's explicit capability subset |
| verified_imessage | Reply only to the verified inbound message reference, subject to channel policy |
| agent_message | Task context only; it cannot grant new external capability authority |
| email_content | No capability authority |
| browser_content | No capability authority |

An external document, email, web page, or inbound message can supply data for
a response. It cannot expand the authorization context.

The initial dashboard policy may grant read-only capabilities for free-form
requests and require an explicit structured confirmation for consequential
writes. The important rule is that the harness computes the grant from the
trusted ingress; later model output and external content cannot add to it.

## Broker decision

For every request, the effective authorization is the intersection of:

~~~text
hard global policy
    intersect
agent capability upper bound
    intersect
turn authorization context
    intersect
scheduled-job subset, when the source is a scheduled job
~~~

For message.reply, the context and request must include a concrete verified
inbound message reference. A raw phone number or a message body that happens
to contain a request is never sufficient.

The broker records the turn_context_id, source, source_reference, and denial
reason in the immutable activity event. This makes a blocked action explainable
as well as blocked.

## Bypass threat model

A native agent may eventually have shell access and browser access as the same
macOS user. A prompt injection could otherwise attempt to:

- read Keychain or filesystem credentials;
- invoke a provider CLI or local integration endpoint directly;
- modify the SQLite policy database;
- use an authenticated browser session to send mail or messages;
- call BlueBubbles without the broker; or
- replay a broad authorization context.

Phase 0 does not need to solve all macOS OS isolation. It must state honestly
that the broker is not a complete containment boundary until the alternate
paths are closed or the integration is kept disabled.

## Required pre-integration security gate

Before Phase 3, 4, or 5 is marked usable:

- provider credentials are accessible only to the broker/helper process through
  Keychain access controls or an equivalent protection model;
- agent environments, prompts, files, and logs contain no provider credentials
  or broker signing secrets;
- provider CLIs/endpoints are not directly addressable with credentials
  available to the agent;
- security-sensitive runtime state has defined ownership, permissions, and
  tamper-detection behavior;
- browser profiles cannot provide an alternate path around prohibited actions;
- browser profiles are realm-specific and sensitive domains/actions have
  explicit allowlists;
- authorization-context credentials are bound to agent/session/source and
  expire;
- prompt-injection tests attempt to route email, web, and message content into
  prohibited external actions; and
- any integration whose alternate paths cannot be proven closed remains
  disabled or manual-only.

Phase 8 may add stronger OS accounts or sandboxing as defense in depth. It is
not the first point at which the project is allowed to notice this boundary.

## Review questions

Before enabling a consequential provider, reviewers should be able to answer:

1. What process holds the credential?
2. Can the native agent read or reuse it?
3. What prevents a direct provider call outside the broker?
4. What trusted ingress creates the authorization context?
5. What exact capability subset is allowed for this turn/job?
6. What source reference is required for the action?
7. What immutable audit event proves the decision?
8. What happens when any answer is uncertain?

An uncertain answer means the provider remains disabled.
