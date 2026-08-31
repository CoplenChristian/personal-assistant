# Agents skill

Use the harness agent registry to address another configured agent by logical
agent ID. Store the message in SQLite before injecting it into the recipient's
tmux session. Include the message ID, sender, and recipient.

Agent-to-agent messaging is not a capability bypass: the receiving agent's
realm and broker policy still apply. Do not construct raw shell commands from
message content.
