# Model telemetry privacy and personality limits

Lucia excludes model message content from its provider telemetry by default.
Personality rewriting also has a shared output-token ceiling, with a 4096-token
fallback when no value is configured.

## Configuration

Configure these settings in AgentHost configuration or environment variables:

```json
{
  "Observability": {
    "IncludeModelContent": false
  },
  "PersonalityPrompt": {
    "MaxOutputTokens": 4096
  }
}
```

Environment variable equivalents are `Observability__IncludeModelContent` and
`PersonalityPrompt__MaxOutputTokens`.

## Model content stays private by default

`ModelProviderResolver` applies one OpenTelemetry wrapper to each chat client
and embedding generator. `IncludeModelContent` defaults to false in both
Development and Production. It explicitly disables sensitive-data capture on
these wrappers, even if the SDK's environment default enables content capture.
Operational spans, model metadata and supported usage measurements remain enabled.

Setting `IncludeModelContent` to true opts into sensitive-data capture. It also
adds the chat logging wrapper, whose Trace-level logs can contain raw messages.
Use this only with suitable access controls and retention for telemetry collectors
and logs. The SDK determines which content each provider supports recording.

The policy is read when a client is constructed. Existing cached clients retain
their original policy. Restart Lucia after changing it to apply the policy to all
clients.

This setting covers the resolver's chat and embedding wrappers. It does not redact
stored conversation traces, training exports, exception messages, provider SDK
internal logs or the separate Copilot direct-agent path. Those require their own
data-handling policies.

## Personality output has a shared ceiling

`PersonalityPrompt:MaxOutputTokens` applies to orchestrated result aggregation and
fast-path personality rendering. An explicit positive value wins. Missing, null
or blank values use the hardcoded 4096-token fallback. Zero and negative values
fail configuration validation at startup.

This limits generated output, not personality instructions or the model's input
context. It is a ceiling, not a request to generate 4096 tokens. Choose a value
your backend supports, and lower it if needed for latency or resource limits.
Input-context sizing is a separate provider setting.

The checked-in personality fixtures contain four styles. The longest instructions
are 372 characters, and the largest expected reply is 600 characters. A 512-token
limit leaves less room for longer real responses. The 4096-token default allows
more headroom without assuming a particular tokenizer or requiring a live model.

When the model reports `ChatFinishReason.Length`, Lucia discards the partial
rewrite and returns the original composed or canned response. Existing empty
response and error fallbacks remain. This does not validate the factual accuracy
of a rewrite that completes normally.

## Offline regression coverage

Run the focused tests without a model server:

```powershell
dotnet test lucia.Tests --filter "FullyQualifiedName~ModelTelemetry|FullyQualifiedName~PersonalityOutputBudgetTests"
```

Tests check wrapper policy across eight chat provider types and seven embedding
provider types, configuration binding and validation, output ceilings in both
rewrite paths, and original-response fallback on truncation. Payload tests use
an in-memory HTTP handler, not an Ollama process or network endpoint. They inspect
streaming and non-streaming chat activities and logs with synthetic markers in
Development and Production, plus default-private embedding activities.
