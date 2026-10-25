# Lucia.OS inference qualification

Use the existing `OpenAICompat` backend for Lucia.OS inference. No new provider
adapter is required by the client-side contract checks. A running Lucia.OS host
and a specific model still need live qualification before evaluation results can
establish model quality, latency or context capacity.

## Configure the existing backend

Add a named backend through local configuration:

```json
{
  "Harness": {
    "Backends": [
      {
        "Name": "Lucia.OS",
        "Type": "OpenAICompat",
        "Endpoint": "https://your-lucia-os-host/v1"
      }
    ],
    "ParameterProfiles": {
      "lucia-os-smoke": { "Name": "lucia-os-smoke", "NumPredict": 128 }
    }
  }
}
```

Server roots and URLs ending in `/v1` normalize to the same inference routes.
Keep the endpoint and inference key in local configuration or user secrets.
For example, in PowerShell 7, supply the key for the first backend without putting
its value in command history:

```powershell
$env:Harness__Backends__0__ApiKey = Read-Host "Lucia.OS inference-only key" -MaskInput
dotnet run --project .\lucia.EvalHarness -- --list-backend-models
Remove-Item Env:Harness__Backends__0__ApiKey
```

Use the actual backend index in an existing configuration. Do not commit the
key, include it in reports, or use an owner/admin key. Use HTTPS with a trusted
certificate for remote hosts. The client does not load, download or swap models.
Select the loaded chat model ID returned by `/v1/models`, not a model-catalog GUID.
An empty catalog is not proof that chat inference is ready.

Choose an output cap supported by the host. The example profile requests 128
output tokens through `max_completion_tokens`; it does not allocate a 128-token
context. Lucia.OS controls context allocation on the server. The pinned server
rejects explicit output limits outside its configured `MaxOutputTokens` range.

## Offline contract coverage

The source baseline is Lucia.OS commit
[`bdf38e5e440debab7e9e888ea2bc1ed129deedc9`](https://github.com/seiggy/lucia.os/tree/bdf38e5e440debab7e9e888ea2bc1ed129deedc9).
Relevant contracts are in
[InferenceEndpoints.cs](https://github.com/seiggy/lucia.os/blob/bdf38e5e440debab7e9e888ea2bc1ed129deedc9/src/Lucia.Homelab.Server/Host/InferenceEndpoints.cs),
[InferenceRuntime.cs](https://github.com/seiggy/lucia.os/blob/bdf38e5e440debab7e9e888ea2bc1ed129deedc9/src/Lucia.Homelab.Server/Host/InferenceRuntime.cs),
[HostPlatformEndpoints.cs](https://github.com/seiggy/lucia.os/blob/bdf38e5e440debab7e9e888ea2bc1ed129deedc9/src/Lucia.Homelab.Server/Host/HostPlatformEndpoints.cs),
[HostApiKeyAuthentication.cs](https://github.com/seiggy/lucia.os/blob/bdf38e5e440debab7e9e888ea2bc1ed129deedc9/src/Lucia.Homelab.Server/Host/HostApiKeyAuthentication.cs)
and the [worker startup](https://github.com/seiggy/lucia.os/blob/bdf38e5e440debab7e9e888ea2bc1ed129deedc9/src/Lucia.Inference.Worker/Program.cs).

`LuciaOsInferenceContractTests` runs real model discovery, the backend factory,
the installed chat SDK and profile injection against an in-memory HTTP handler.
No server, credentials, model download or network connection is required.

| Check | Evidence from the fixture |
| --- | --- |
| Four root and `/v1` URL forms | Exact `/v1/models` and `/v1/chat/completions` routes |
| Inference credential wiring | Synthetic Bearer key on discovery and chat; no management requests |
| Model discovery | Lucia.OS metadata shape; embedding-only model excluded |
| Plain chat | Requested model, output cap, parsed answer, finish reason and usage |
| Tool round trip | Actual `ClimateControlSkill.GetClimateStateAsync` schema, call ID and entity argument, fixed Unicode tool result, final answer |
| HTTP failures | Chat preserves 401/403/503 status and error code; discovery throws rather than returning an empty successful catalog |
| Invalid JSON | Discovery and chat reject malformed responses |
| Cancellation and timeout | Caller cancellation propagates; discovery timeout reports unavailable; chat transport timeouts retain timeout causes inside the SDK retry aggregate |

Run the focused checks:

```powershell
dotnet test .\lucia.EvalHarness.Tests --filter "FullyQualifiedName~LuciaOsInferenceContractTests|FullyQualifiedName~BackendModelDiscoveryTests"
```

The model metadata and error shapes follow pinned source. Successful chat and
tool replies are synthetic OpenAI-compatible examples, not captured TensorSharp
responses. The fixture proves client serialization and parsing, not Lucia.OS's
server implementation, model tool quality, authentication enforcement, TLS or
GPU readiness. It tests non-streaming chat only. The tool is never invoked; a
fixed result stands in for a device read. Timeout tests shorten the injected test
HttpClient timeout only; they do not change production defaults.

## Live acceptance for a specific host and model

Use an explicitly approved test host with an already-loaded model and an
inference-only key. Record the Lucia.NET commit, Lucia.OS build, serving backend,
model ID and digest, quantization, hardware, effective context, output cap,
timeout, certificate trust and request latency. Mark unavailable observations as
unknown, rather than inferring them from requested settings.

1. Discover `/v1/models` through the configured harness backend.
2. Run plain text chat and inspect the selected model ID, finish reason and usage.
3. Run a synthetic climate-state tool request and verify the returned function
   name, entity argument and call ID. Supply a fixed tool result and verify the
   final response. Do not execute a Home Assistant tool or physical action.
4. With the owner's approval, confirm that the same inference key cannot read
   `/api/host/status` on the host, or `/api/worker/status` on a worker. Expect an
   authorization denial. Do not test management writes or substitute an owner key.
5. Retain pass/fail results and sanitized error codes. Exclude authorization
   headers, home data and sensitive prompts from the report.

Recheck the contract when the deployed Lucia.OS build differs from the source
baseline. An existing Ollama smoke test is not a Lucia.OS qualification. Add an
adapter only after a real incompatible request or response demonstrates a need.
