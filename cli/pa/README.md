# `pa` CLI

The future agent-facing command line for capability requests and safe
agent-to-agent messages, for example:

```text
pa mail search --account personal-main ...
pa reminder create ...
pa agent message research ...
```

Commands should communicate with the local capability broker over a Unix
domain socket and should never expose credentials to agents.
