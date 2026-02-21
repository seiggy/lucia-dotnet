---
name: act-local-ci
description: >
  Run and debug GitHub Actions workflows locally using the `act` CLI tool.
  Covers runner image selection, secrets/vars handling, job targeting,
  event simulation, and common troubleshooting patterns.

  TRIGGER THIS SKILL WHEN:
  - Running or testing GitHub Actions workflows locally
  - Debugging CI/CD pipeline failures without pushing to GitHub
  - Validating workflow file changes before committing
  - Simulating different GitHub events (push, pull_request, workflow_dispatch)
  - Troubleshooting act runner image issues or missing tools

  SYMPTOMS THAT TRIGGER THIS SKILL:
  - Agent pushes workflow changes to test them instead of running locally
  - Agent doesn't know how to provide secrets or vars to local workflow runs
  - Agent can't reproduce a CI failure locally
  - Agent needs to run a specific job or workflow in isolation
metadata:
  version: "1.0.0"
---

# Running GitHub Actions Locally with `act`

`act` reads `.github/workflows/` and runs them in Docker containers that simulate GitHub-hosted runners. Use it for fast feedback without commit/push cycles.

## Quick Reference

```bash
# Run all workflows triggered by push (default event)
act

# Run a specific event
act push
act pull_request
act workflow_dispatch

# List available workflows/jobs without running
act -l
act -l pull_request

# Run a specific workflow file
act -W .github/workflows/validate-infrastructure.yml

# Run a specific job by ID
act -j validate-docker

# Dry run (validate only, no containers)
act -n

# Verbose output for debugging
act -v
```

## Runner Images

`act` maps `runs-on` values to Docker images. Default images are intentionally minimal — they do NOT contain all tools GitHub provides.

### Image Tiers

| Runner Label | Micro (default) | Medium | Large (full GitHub parity) |
| --- | --- | --- | --- |
| `ubuntu-latest` | `node:16-buster-slim` | `catthehacker/ubuntu:act-latest` | `catthehacker/ubuntu:full-latest` |
| `ubuntu-22.04` | `node:16-bullseye-slim` | `catthehacker/ubuntu:act-22.04` | `catthehacker/ubuntu:full-22.04` |
| `ubuntu-20.04` | `node:16-buster-slim` | `catthehacker/ubuntu:act-20.04` | `catthehacker/ubuntu:full-20.04` |

### Selecting a Runner Image

Use `-P` to override the image for a platform:

```bash
# Use the medium image (recommended — good balance of tools vs size)
act -P ubuntu-latest=catthehacker/ubuntu:act-latest

# Use the full image (~18GB — closest to real GitHub runner)
act -P ubuntu-latest=catthehacker/ubuntu:full-latest

# Run directly on host (no Docker — for matching host OS)
act -P ubuntu-latest=-self-hosted

# Multiple platforms
act -P ubuntu-latest=catthehacker/ubuntu:act-latest \
    -P ubuntu-22.04=catthehacker/ubuntu:act-22.04
```

### When to Use Each Tier

- **Micro** (default): Simple workflows with basic shell commands, no special tooling needed.
- **Medium** (`act-*`): Workflows that need common tools (Python, Node, Docker CLI, Helm, etc.). **Best default for most projects.**
- **Large** (`full-*`): Workflows that fail on medium due to missing tools. Closest to GitHub but very large download.
- **Self-hosted** (`-self-hosted`): When you need exact host-OS tools or can't use Docker-in-Docker.

## Secrets and Variables

### Secrets

```bash
# Inline secret
act -s MY_SECRET=some-value

# Prompt for secure input (recommended — not saved in shell history)
act -s MY_SECRET

# From file (.secrets format = .env format)
act --secret-file .secrets

# GitHub token (required for many actions)
act -s GITHUB_TOKEN="$(gh auth token)"
```

### Repository Variables

```bash
# Inline variable
act --var MY_VAR=some-value

# From file
act --var-file .vars
```

### Environment Variables

```bash
# Pass env vars to containers
act --env MY_ENV=foo

# Use an env file (default: .env)
act --env-file my.env
```

## Event Simulation

### Event Types

```bash
act push              # Simulate push event
act pull_request      # Simulate PR event
act schedule          # Simulate scheduled event
act workflow_dispatch # Simulate manual trigger
```

### Event Payloads

Create a JSON file to provide event properties workflows expect:

```json
// pr-event.json — simulate a pull_request
{
  "pull_request": {
    "head": { "ref": "feature-branch" },
    "base": { "ref": "main" }
  }
}
```

```json
// dispatch-event.json — simulate workflow_dispatch with inputs
{
  "inputs": {
    "environment": "staging",
    "dry_run": "true"
  }
}
```

```bash
act pull_request -e pr-event.json
act workflow_dispatch -e dispatch-event.json
```

### Workflow Dispatch Inputs

```bash
# Via --input flag
act workflow_dispatch --input NAME=value --input OTHER=value

# Via input file
act workflow_dispatch --input-file .input
```

## Matrix Targeting

Run only specific matrix combinations:

```bash
# Run only the node 18 matrix entry
act --matrix node:18

# Combine matrix filters
act --matrix node:18 --matrix os:ubuntu-latest
```

## Performance and Caching

### Offline Mode

Speeds up repeated runs by caching actions and images:

```bash
act --action-offline-mode
```

### Skip Image Pulls

```bash
act --pull=false
```

### Reuse Containers

Keep containers between runs to preserve state:

```bash
act -r    # --reuse
```

### Artifacts

Enable artifact upload/download support:

```bash
act --artifact-server-path ./.artifacts
```

## Configuration File

Create `.actrc` in the repo root for persistent defaults (one arg per line):

```text
--container-architecture=linux/amd64
--action-offline-mode
-P ubuntu-latest=catthehacker/ubuntu:act-latest
```

Files are loaded in order: XDG config → `$HOME/.actrc` → project `.actrc` → CLI args.

## Skipping Jobs/Steps Locally

### Skip a job when running locally

```yaml
jobs:
  deploy:
    if: ${{ !github.event.act }}
    runs-on: ubuntu-latest
```

Provide an event file to trigger the skip:

```json
{ "act": true }
```

```bash
act -e event.json
```

### Skip a step when running locally

```yaml
- name: Post to Slack
  if: ${{ !env.ACT }}
  run: echo "Only runs on GitHub"
```

The `ACT` environment variable is automatically set by `act`.

## Troubleshooting

### Common Issues

| Problem | Solution |
| --- | --- |
| Action fails with "token" error | Pass `-s GITHUB_TOKEN="$(gh auth token)"` |
| Missing tools in runner | Use medium image: `-P ubuntu-latest=catthehacker/ubuntu:act-latest` |
| Docker-in-Docker not working | Use `--privileged` flag or self-hosted runner |
| systemd not available | Known limitation — Docker containers don't support systemd |
| Slow first run | Images are large; subsequent runs use cache. Use `--action-offline-mode` |
| Actions re-downloaded every run | Enable `--action-offline-mode` or `--pull=false` |
| Need to bind mount working dir | Use `-b` / `--bind` instead of copy |
| Container architecture mismatch | Set `--container-architecture=linux/amd64` |
| Workflow uses Windows/macOS runner | Use `-P windows-latest=-self-hosted` (must be on matching OS) |

### Debugging Workflow

```bash
# 1. Validate workflow syntax first
act -n

# 2. List what would run
act -l

# 3. Run specific job with verbose output
act -j my-job -v

# 4. Run with medium image for better tool support
act -j my-job -P ubuntu-latest=catthehacker/ubuntu:act-latest

# 5. If that fails, try full image
act -j my-job -P ubuntu-latest=catthehacker/ubuntu:full-latest

# 6. If Docker-in-Docker needed
act -j my-job --privileged -P ubuntu-latest=catthehacker/ubuntu:act-latest
```

### Custom Container Engine

Use Podman or remote Docker:

```bash
# Podman
DOCKER_HOST='unix:///var/run/podman/podman.sock' act

# Remote Docker via SSH
DOCKER_HOST='ssh://user@host' act
```

## Project-Specific Examples

### Running lucia-dotnet Workflows

```bash
# Validate infrastructure (uses docker compose, helm, yamllint)
act -W .github/workflows/validate-infrastructure.yml \
    -P ubuntu-latest=catthehacker/ubuntu:act-latest

# Lint Helm chart only
act -W .github/workflows/helm-lint.yml \
    -P ubuntu-latest=catthehacker/ubuntu:act-latest

# Docker build (needs privileged for Docker-in-Docker)
act -W .github/workflows/docker-build-push.yml \
    -j build-and-push \
    --privileged \
    -P ubuntu-latest=catthehacker/ubuntu:act-latest \
    -s DOCKER_HUB_USERNAME=your-username \
    -s DOCKER_HUB_TOKEN=your-token

# Run a specific job from validate-infrastructure
act -j validate-kubernetes \
    -W .github/workflows/validate-infrastructure.yml \
    -P ubuntu-latest=catthehacker/ubuntu:act-latest

# Dry-run to check workflow validity
act -n -W .github/workflows/validate-infrastructure.yml
```
