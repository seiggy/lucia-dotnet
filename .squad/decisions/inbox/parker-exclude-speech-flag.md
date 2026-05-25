# Parker — EXCLUDE_SPEECH flag for ARM / Jetson builds

## Context

Jetson Nano ARM64 builds need to run `lucia.AgentHost` without the Wyoming speech runtime because the current .NET voice pipeline depends on Sherpa-ONNX and ONNX Runtime packages that are difficult to ship for this target.

## Decision

Add an `ExcludeSpeech` MSBuild property that defines the `EXCLUDE_SPEECH` compilation symbol through `Directory.Build.props`.

When `ExcludeSpeech=true`:

1. `lucia.Wyoming` keeps command-routing types and registrations needed by `/api/conversation`.
2. `lucia.Wyoming` conditionally skips ONNX / Sherpa / audio package references.
3. `lucia.Wyoming` conditionally removes ONNX-backed engine/session/server source files from compilation.
4. `lucia.AgentHost` conditionally removes speech-only API files and skips mapping the Wyoming / voice endpoints.

## Why

Removing the full `lucia.Wyoming` project reference would also remove command-routing primitives used by the conversation fast-path. The correct boundary is to preserve text command routing but exclude the speech runtime, model-management surface, and Wyoming server pieces.

## Validation

- `dotnet build .\lucia.AgentHost\lucia.AgentHost.csproj`
- `dotnet build .\lucia.AgentHost\lucia.AgentHost.csproj -p:ExcludeSpeech=true`

Both builds succeeded locally after the change.
