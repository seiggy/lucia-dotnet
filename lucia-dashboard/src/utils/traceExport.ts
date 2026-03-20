import type { ConversationTrace, CommandTrace } from '../types'

const GITHUB_ISSUES_URL = 'https://github.com/seiggy/lucia-dotnet/issues/new'
const MAX_URL_LENGTH = 8000

/** Trigger a browser download of JSON data. */
export function downloadJson(data: unknown, filename: string): void {
  const json = JSON.stringify(data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

/** Build a date stamp for filenames (e.g. 2026-03-20). */
export function datestamp(): string {
  return new Date().toISOString().slice(0, 10)
}

function truncate(text: string, max: number): string {
  return text.length > max ? text.slice(0, max) + '…' : text
}

/** Build a GitHub new-issue URL pre-filled with conversation trace data. */
export function buildConversationTraceIssueUrl(trace: ConversationTrace): string {
  const isError = trace.isErrored
  const agents = trace.agentExecutions.map((e) => e.agentId).join(', ')
  const title = isError
    ? `[Trace] Error: "${truncate(trace.userInput, 60)}"`
    : `[Trace] Issue with: "${truncate(trace.userInput, 60)}"`

  const lines: string[] = [
    `## Conversation Trace Report`,
    ``,
    `| Field | Value |`,
    `|-------|-------|`,
    `| **Trace ID** | \`${trace.id}\` |`,
    `| **Timestamp** | ${new Date(trace.timestamp).toLocaleString()} |`,
    `| **Session ID** | \`${trace.sessionId}\` |`,
    `| **Duration** | ${trace.totalDurationMs} ms |`,
    `| **Errored** | ${trace.isErrored ? 'Yes' : 'No'} |`,
    `| **Agents** | ${agents || 'None'} |`,
    ``,
    `### User Input`,
    `\`\`\``,
    trace.userInput,
    `\`\`\``,
    ``,
  ]

  if (trace.finalResponse) {
    lines.push(`### Final Response`, `\`\`\``, truncate(trace.finalResponse, 500), `\`\`\``, ``)
  }

  if (trace.errorMessage) {
    lines.push(`### Error`, `\`\`\``, trace.errorMessage, `\`\`\``, ``)
  }

  if (trace.routing) {
    lines.push(
      `### Routing Decision`,
      `- **Selected Agent:** ${trace.routing.selectedAgentId}`,
      `- **Confidence:** ${(trace.routing.confidence * 100).toFixed(1)}%`,
      `- **Routing Duration:** ${trace.routing.routingDurationMs} ms`,
    )
    if (trace.routing.reasoning) {
      lines.push(`- **Reasoning:** ${truncate(trace.routing.reasoning, 200)}`)
    }
    lines.push(``)
  }

  if (trace.agentExecutions.length > 0) {
    lines.push(`### Agent Executions`)
    for (const exec of trace.agentExecutions) {
      const status = exec.success ? '✅' : '❌'
      lines.push(`- ${status} **${exec.agentId}** — ${exec.executionDurationMs} ms`)
      if (exec.errorMessage) {
        lines.push(`  - Error: ${truncate(exec.errorMessage, 200)}`)
      }
    }
    lines.push(``)
  }

  lines.push(
    `---`,
    `*Generated from Lucia Dashboard trace viewer*`,
  )

  const body = lines.join('\n')
  const labels = isError ? 'bug' : 'triage'

  return buildGitHubUrl(title, body, labels)
}

/** Build a GitHub new-issue URL pre-filled with command trace data. */
export function buildCommandTraceIssueUrl(trace: CommandTrace): string {
  const isError = trace.outcome === 'error'
  const skill = trace.match.skillId ?? trace.execution?.skillId ?? 'unknown'
  const action = trace.match.action ?? trace.execution?.action

  const title = isError
    ? `[Command Trace] Error: "${truncate(trace.cleanText || trace.rawText, 60)}"`
    : `[Command Trace] Issue with: "${truncate(trace.cleanText || trace.rawText, 60)}"`

  const lines: string[] = [
    `## Command Trace Report`,
    ``,
    `| Field | Value |`,
    `|-------|-------|`,
    `| **Trace ID** | \`${trace.id}\` |`,
    `| **Timestamp** | ${new Date(trace.timestamp).toLocaleString()} |`,
    `| **Outcome** | ${trace.outcome} |`,
    `| **Duration** | ${trace.totalDurationMs} ms |`,
    `| **Skill** | ${skill}${action ? ` / ${action}` : ''} |`,
    `| **Confidence** | ${(trace.match.confidence * 100).toFixed(1)}% |`,
    ``,
    `### Raw Input`,
    `\`\`\``,
    trace.rawText,
    `\`\`\``,
    ``,
  ]

  if (trace.normalizedText && trace.normalizedText !== trace.rawText) {
    lines.push(`### Normalized Text`, `\`\`\``, trace.normalizedText, `\`\`\``, ``)
  }

  if (trace.responseText) {
    lines.push(`### Response`, `\`\`\``, truncate(trace.responseText, 500), `\`\`\``, ``)
  }

  if (trace.error) {
    lines.push(`### Error`, `\`\`\``, trace.error, `\`\`\``, ``)
  }

  if (trace.match.isMatch) {
    lines.push(
      `### Pattern Match`,
      `- **Pattern ID:** \`${trace.match.patternId ?? 'N/A'}\``,
      `- **Template:** \`${trace.match.templateUsed ?? 'N/A'}\``,
      `- **Match Duration:** ${trace.match.matchDurationMs} ms`,
    )
    if (trace.match.capturedValues && Object.keys(trace.match.capturedValues).length > 0) {
      lines.push(`- **Captured Values:**`)
      for (const [key, value] of Object.entries(trace.match.capturedValues)) {
        lines.push(`  - \`${key}\` → \`${value}\``)
      }
    }
    lines.push(``)
  }

  if (trace.execution) {
    const status = trace.execution.success ? '✅' : '❌'
    lines.push(
      `### Skill Execution`,
      `- ${status} **${trace.execution.skillId}** / ${trace.execution.action} — ${trace.execution.durationMs} ms`,
    )
    if (trace.execution.error) {
      lines.push(`- Error: ${truncate(trace.execution.error, 200)}`)
    }
    if (trace.execution.toolCalls.length > 0) {
      lines.push(`- **Tool Calls:** ${trace.execution.toolCalls.map((tc) => tc.methodName).join(', ')}`)
    }
    lines.push(``)
  }

  if (trace.requestContext.deviceArea || trace.requestContext.deviceId) {
    lines.push(
      `### Context`,
      trace.requestContext.deviceArea ? `- **Area:** ${trace.requestContext.deviceArea}` : '',
      trace.requestContext.deviceId ? `- **Device:** ${trace.requestContext.deviceId}` : '',
      trace.requestContext.speakerId ? `- **Speaker:** ${trace.requestContext.speakerId}` : '',
      ``,
    )
  }

  lines.push(
    `---`,
    `*Generated from Lucia Dashboard command trace viewer*`,
  )

  const body = lines.filter(Boolean).join('\n')
  const labels = isError ? 'bug' : 'triage'

  return buildGitHubUrl(title, body, labels)
}

function buildGitHubUrl(title: string, body: string, labels: string): string {
  const params = new URLSearchParams({ title, body, labels })
  let url = `${GITHUB_ISSUES_URL}?${params.toString()}`

  if (url.length > MAX_URL_LENGTH) {
    // Iteratively shrink body until encoded URL fits within the limit
    let truncatedBody = body
    while (url.length > MAX_URL_LENGTH && truncatedBody.length > 200) {
      truncatedBody = truncatedBody.slice(0, Math.floor(truncatedBody.length * 0.8))
      const truncatedParams = new URLSearchParams({
        title,
        body: truncatedBody + '\n\n⚠️ *Body truncated due to URL length limits. Export the trace JSON for full details.*',
        labels,
      })
      url = `${GITHUB_ISSUES_URL}?${truncatedParams.toString()}`
    }
  }

  return url
}
