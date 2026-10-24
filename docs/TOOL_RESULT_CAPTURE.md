# Safe tool result capture

Lucia keeps tool-result previews out of new traces and training exports by
default. Owners can opt into bounded, sanitized previews for debugging. Trace
capture remains separate from model-provider OpenTelemetry instrumentation.

## Configuration

Set these values in AgentHost configuration or environment variables:

```json
{
  "TraceCapture": {
    "Enabled": true,
    "CaptureToolResults": false,
    "MaxToolResultBytes": 4096,
    "IncludeToolResultsInTrainingExport": false
  }
}
```

Use double underscores in environment variable names, such as
`TraceCapture__CaptureToolResults`. Restart Lucia after changing these settings.
Existing `RetentionDays` and `RedactionPatterns` settings still apply.

`Enabled=false` disables per-agent trace persistence as well as the orchestrator
observer. Inference and live tool-call metadata events continue. With capture
enabled, tool names, arguments and call IDs retain their existing behavior even
when result previews are off.

`CaptureToolResults=true` opts into previews for matched tool calls, including
both successful and failed calls. A missing SDK exception does not prove success,
so this change does not guess a failure-only mode from result text.
`ResultIsError=true` records a supplied SDK exception without copying its message.
Null means the result outcome is unknown.

## Sanitization and bounds

The same `ToolResultCapturePolicy` sanitizes previews before persistence and
again before JSONL export. It accepts strings and `JsonElement` values only.
It does not stringify arbitrary objects or capture binary/media content.

For JSON objects and arrays, it replaces values of common credential properties,
including nested properties, with `[REDACTED]`. Property matching ignores case,
underscores and hyphens. The keys are `apikey`, `token`, `accesstoken`,
`refreshtoken`, `secret`, `clientsecret`, `password`, `authorization`, `cookie`,
`setcookie` and `privatekey`.

Configured `RedactionPatterns` apply to text and decoded JSON strings before
truncation. Each regex operation has a 100 ms timeout. Invalid patterns, a timeout,
malformed JSON-shaped input or JSON deeper than 32 levels suppress the preview
instead of falling back to raw content.

Input above 64 KiB is omitted rather than processing only a prefix that might
contain half a credential. `MaxToolResultBytes` accepts 1 through 65536 bytes and
defaults to 4096. An invalid limit suppresses previews. The output bound counts
UTF-8 bytes without splitting Unicode scalars. `ResultTruncated` indicates that
the sanitized text exceeded that bound. A preview is text and need not be valid
JSON after truncation.

Results attach only to the same call ID. An unmatched result is omitted, never
attached to an unrelated call. Tool-role text is not duplicated in the ordinary
message list or trace final-response field. The chat messages and responses used
by the agent are not modified.

The dashboard already renders result strings as escaped text in a `pre` element.
No HTML execution or new large-result viewer is introduced here.

## Training exports and existing data

`IncludeToolResultsInTrainingExport` is a separate server-side opt-in. Enabling
capture alone does not enable export. The existing agent filter is still required
to include tool-result messages. Opted-in exports re-sanitize legacy values using
the current patterns and byte bound, without changing stored traces. A truncated
JSON preview that is no longer valid JSON is omitted on re-sanitization.

This changes newly generated exports only. Existing database rows and previously
generated downloadable files are not rewritten or deleted. Review retention and
access to those separately.

Redaction is a credential filter, not a guarantee that text contains no personal
data or malicious instructions. System prompts, user/assistant messages, tool
arguments, inference exception text and spans still require their own privacy
review. An assistant can also repeat sensitive tool content in its own words.
Keep result capture off unless the diagnostic value justifies that risk, and
review any training data before use.

## Offline tests

```powershell
dotnet test lucia.Tests --filter "FullyQualifiedName~Training"
```

Tests cover default-private persistence and legacy exports, explicit opt-ins,
request/response result paths, disabled capture on success and exceptions, exact
call-ID matching, structured credentials, configured patterns, Unicode byte
bounds, malformed/oversized input, regex failure, factory option forwarding and
the export handler's use of server policy. No model server is required.
