import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Loader2, Plus, Save, X } from 'lucide-react'
import Notice from './Notice'
import { fetchAgentDefinitions } from '../../api'
import { ROLE_DEFAULTS, ROLE_LABELS, savePermissions } from '../../family-api'
import type { ActionRule, CapabilityRule, FamilyMember } from '../../family-api'
import { errorText, input, primaryButton, secondaryButton, section, sectionHint, sectionTitle } from './familyStyles'

type Choice = ActionRule | 'Default'

const CHOICES: { value: Choice; label: string }[] = [
  { value: 'Default', label: 'Default' },
  { value: 'Allow', label: 'Allow' },
  { value: 'AskGuardian', label: 'Ask guardian' },
  { value: 'Deny', label: 'Block' },
]

const RULE_LABELS: Record<ActionRule, string> = { Allow: 'Allow', AskGuardian: 'Ask guardian', Deny: 'Block' }

interface MemberPermissionsProps {
  member: FamilyMember
  onSaved: (rules: CapabilityRule[]) => void
}

/** Per-member overrides of the role's default agent and tool permissions. */
export default function MemberPermissions({ member, onSaved }: MemberPermissionsProps) {
  const agentsQuery = useQuery({ queryKey: ['agent-definitions'], queryFn: fetchAgentDefinitions })
  const [rules, setRules] = useState<CapabilityRule[]>(member.capabilityRules)
  const [toolName, setToolName] = useState('')
  const [toolRule, setToolRule] = useState<ActionRule>('AskGuardian')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)

  const agents = [...(agentsQuery.data ?? [])]
    .filter(agent => !agent.isOrchestrator)
    .sort((left, right) => (left.displayName || left.name).localeCompare(right.displayName || right.name))
  const toolRules = rules.filter(rule => rule.kind === 'Tool')
  const dirty = JSON.stringify(rules) !== JSON.stringify(member.capabilityRules)
  const duplicateTool = toolRules.some(rule => rule.capabilityId.toLowerCase() === toolName.trim().toLowerCase())

  function agentChoice(agentId: string): Choice {
    return rules.find(rule => rule.kind === 'Agent' && rule.capabilityId === agentId)?.rule ?? 'Default'
  }

  function setAgentChoice(agentId: string, choice: Choice) {
    const others = rules.filter(rule => !(rule.kind === 'Agent' && rule.capabilityId === agentId))
    setRules(choice === 'Default' ? others : [...others, { kind: 'Agent', capabilityId: agentId, rule: choice }])
    setNotice(null)
  }

  function addToolRule() {
    const id = toolName.trim()
    if (!id || duplicateTool) return
    setRules([...rules, { kind: 'Tool', capabilityId: id, rule: toolRule }])
    setToolName('')
    setNotice(null)
  }

  async function save() {
    setBusy(true)
    setNotice(null)
    try {
      const saved = await savePermissions(member.id, rules)
      setRules(saved)
      onSaved(saved)
      setNotice({ error: false, text: `Permissions saved for ${member.displayName}.` })
    } catch (error) {
      setNotice({ error: true, text: errorText(error, 'Could not save permissions.') })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-6">
      <div className={section}>
        <h3 className={sectionTitle}>Agents</h3>
        <p className={sectionHint}>
          {ROLE_LABELS[member.role]} default: {ROLE_DEFAULTS[member.role]} A blocked agent refuses every request from {member.displayName}, including through the new assistant.
        </p>
        {agentsQuery.isLoading ? (
          <div role="status" aria-label="Loading agents" className="h-48 animate-pulse rounded-xl bg-basalt/70" />
        ) : agentsQuery.isError ? (
          <p role="alert" className="text-sm text-rose">{agentsQuery.error.message}</p>
        ) : agents.length === 0 ? (
          <p className="text-sm text-fog">No agents are configured yet.</p>
        ) : (
          <ul className="divide-y divide-stone rounded-xl border border-stone">
            {agents.map(agent => {
              const choice = agentChoice(agent.id)
              const name = agent.displayName || agent.name
              return (
                <li key={agent.id} className="flex flex-col gap-3 p-4 lg:flex-row lg:items-center lg:justify-between">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-light">
                      {name}
                      {agent.isRemote && <span className="ml-2 text-xs font-normal text-dust">Remote</span>}
                      {!agent.enabled && <span className="ml-2 text-xs font-normal text-dust">Disabled</span>}
                    </p>
                    {agent.description && <p className="mt-0.5 line-clamp-2 text-xs leading-5 text-fog">{agent.description}</p>}
                  </div>
                  <div role="group" aria-label={`${name} permission`} className="flex w-full shrink-0 rounded-lg border border-stone bg-basalt p-1 lg:w-auto">
                    {CHOICES.map(option => (
                      <button key={option.value} type="button" aria-pressed={choice === option.value} disabled={busy}
                        title={option.value === 'Default' ? `Use the ${ROLE_LABELS[member.role].toLowerCase()} role default` : undefined}
                        onClick={() => setAgentChoice(agent.id, option.value)}
                        className={`min-h-9 flex-1 whitespace-nowrap rounded-md px-3 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 lg:flex-none ${
                          choice === option.value
                            ? option.value === 'Deny' ? 'bg-rose/12 text-rose' : 'bg-obsidian text-amber shadow-sm'
                            : 'text-dust hover:bg-stone/40 hover:text-light'
                        }`}>
                        {option.label}
                      </button>
                    ))}
                  </div>
                </li>
              )
            })}
          </ul>
        )}
        <p className="text-xs leading-5 text-dust">
          Remote agents and agents on GitHub Copilot can't have their tools checked, so they refuse children, guests, and anyone with a non-allow rule.
        </p>
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Tool overrides</h3>
        <p className={sectionHint}>
          Override a single tool by name, for example <code className="font-mono text-xs text-cloud">ControlLights</code> or{' '}
          <code className="font-mono text-xs text-cloud">SetClimateTemperature</code>. A tool's rule applies together with its agent's; the stricter one wins.
        </p>
        {toolRules.length > 0 && (
          <ul className="divide-y divide-stone rounded-xl border border-stone">
            {toolRules.map(rule => (
              <li key={rule.capabilityId} className="flex items-center justify-between gap-3 px-4 py-2">
                <span className="min-w-0 break-all font-mono text-sm text-cloud">{rule.capabilityId}</span>
                <span className="flex items-center gap-2">
                  <span className={`text-sm ${rule.rule === 'Deny' ? 'text-rose' : 'text-fog'}`}>{RULE_LABELS[rule.rule]}</span>
                  <button type="button" aria-label={`Remove the ${rule.capabilityId} override`} disabled={busy}
                    onClick={() => setRules(rules.filter(candidate => candidate !== rule))}
                    className="flex h-11 w-11 items-center justify-center rounded-lg text-dust hover:bg-stone/40 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60">
                    <X className="h-4 w-4" aria-hidden="true" />
                  </button>
                </span>
              </li>
            ))}
          </ul>
        )}
        <div className="flex flex-col gap-2 sm:flex-row">
          <label className="sr-only" htmlFor={`tool-name-${member.id}`}>Tool name</label>
          <input id={`tool-name-${member.id}`} className={`${input} font-mono`} value={toolName} placeholder="Tool name" maxLength={100}
            disabled={busy} onChange={event => setToolName(event.target.value)}
            onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); addToolRule() } }} />
          <label className="sr-only" htmlFor={`tool-rule-${member.id}`}>Rule</label>
          <select id={`tool-rule-${member.id}`} className={`${input} sm:w-44`} value={toolRule} disabled={busy}
            onChange={event => setToolRule(event.target.value as ActionRule)}>
            <option value="Allow">Allow</option>
            <option value="AskGuardian">Ask guardian</option>
            <option value="Deny">Block</option>
          </select>
          <button type="button" className={`${secondaryButton} shrink-0 whitespace-nowrap`} disabled={busy || !toolName.trim() || duplicateTool} onClick={addToolRule}>
            <Plus className="h-4 w-4" aria-hidden="true" /> Add override
          </button>
        </div>
        {duplicateTool && <p className="text-sm text-amber">That tool already has an override. Remove it first to change it.</p>}
      </div>

      <Notice notice={notice} />

      <div className="flex flex-wrap items-center gap-3 border-t border-stone pt-6">
        <button type="button" className={primaryButton} disabled={busy || !dirty} onClick={() => void save()}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Save className="h-4 w-4" aria-hidden="true" />}
          Save permissions
        </button>
        {dirty && !busy && (
          <button type="button" className={secondaryButton} onClick={() => { setRules(member.capabilityRules); setNotice(null) }}>
            Discard changes
          </button>
        )}
      </div>
    </div>
  )
}
