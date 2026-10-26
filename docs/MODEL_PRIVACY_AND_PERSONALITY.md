# Model telemetry privacy and personality limits

Lucia records model prompts and responses in its provider telemetry by default,
matching earlier releases. Users choose during setup whether to keep that, and
upgraded installs keep it on. Personality rewriting also has a shared
output-token ceiling, with a 4096-token fallback when no value is configured.

## Configuration

```json
{
  "ModelTelemetry": {
    "IncludeModelContent": true
  },
  "PersonalityPrompt": {
    "MaxOutputTokens": 4096
  }
}
```

Environment variable equivalents are `ModelTelemetry__IncludeModelContent` and
`PersonalityPrompt__MaxOutputTokens`.

The setting has its own `ModelTelemetry` section rather than living under
`Observability`. `ConfigSeeder` copies whole sections into the config store, and
the config store overrides environment variables. Seeding `Observability` would
freeze `Observability:Mode` and the OTLP settings that AppHost and the appliance
manager set through the environment.

## What the setting controls

`ModelProviderResolver` applies one OpenTelemetry wrapper to each chat client
and embedding generator. `IncludeModelContent` sets the wrapper's sensitive-data
capture, and when true also adds the chat logging wrapper, whose Trace-level logs
contain raw messages. With it off, Lucia still records operational spans, model
metadata and supported usage measurements, and explicitly disables content
capture even if the SDK's environment default would enable it.
The SDK determines which content each provider supports recording.

Content capture helps with troubleshooting and collecting training data, but
prompts can include names, household activity and anything said to Lucia.
Anyone who can read the telemetry collector or logs can read them. Keep it on
only with suitable access controls and retention for telemetry collectors and logs.

The resolver reads the policy when it builds a client. Cached clients keep the
policy they were built with, so restart Lucia after changing it.

This setting covers the resolver's chat and embedding wrappers. It does not redact
stored conversation traces, training exports, exception messages, provider SDK
internal logs or the separate Copilot direct-agent path. Those need their own
data-handling policies.

## New installs, upgrades and later changes

New installs get a Model telemetry step in the setup wizard, after the Home
Assistant connection and before AI providers. It explains what gets recorded and
shows a switch that starts on. Asking before any provider exists means model
clients are built with the chosen policy without a restart. The wizard saves the
choice through `POST /api/setup/configure-model-telemetry` and reloads
configuration.

Upgraded installs never see the wizard. On startup `ConfigSeeder` inserts
`ModelTelemetry:IncludeModelContent = true` from appsettings because the key is
missing, so existing troubleshooting and training workflows keep working. The
seeder never overwrites a stored value, so a later change to the shipped default
won't flip existing installs either.

Headless installs that skip the wizard get the same seeded default. Set
`ModelTelemetry__IncludeModelContent=false` before first start to seed `false`
instead.

After setup, change the value in the ModelTelemetry section of the Configuration
page, then restart Lucia.

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
provider types, the default-on binding, configuration validation, output ceilings
in both rewrite paths, and original-response fallback on truncation. Payload tests
use an in-memory HTTP handler, not an Ollama process or network endpoint. They
inspect streaming and non-streaming chat activities and logs with synthetic markers
in Development and Production, plus embedding activities with content off.

Upgrade tests run `ConfigSeeder` against a real SQLite config store. They check
that an existing install gets the default seeded, that a stored `false` survives,
and that no `Observability` key reaches the store. Setup tests check that the
wizard endpoint stores the choice and reloads configuration, and that the
Configuration page schema matches the code default.
