import { useState, useEffect, useCallback, useRef } from 'react'
import CustomSelect from '../components/CustomSelect'
import {
  fetchOptimizableSkills,
  fetchSkillDevices,
  fetchSkillTraces,
  fetchModelProviders,
  startOptimization,
  fetchOptimizerJob,
  cancelOptimizerJob,
  updateConfigSection,
} from '../api'
import type {
  OptimizableSkillInfo,
  SkillDeviceInfo,
  OptimizationTestCase,
  JobStatusResponse,
  OptimizationCaseResult,
  ModelProvider,
} from '../types'

/* ------------------------------------------------------------------ */
/*  Toast                                                              */
/* ------------------------------------------------------------------ */

interface Toast { id: number; message: string; type: 'success' | 'error' }
let toastId = 0

function ToastContainer({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: number) => void }) {
  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={`flex items-center gap-3 rounded-xl px-4 py-3 shadow-lg text-sm font-medium transition-all duration-300 ${
            t.type === 'success' ? 'bg-sage/20 text-light' : 'bg-ember/20 text-light'
          }`}
        >
          <span>{t.type === 'success' ? '✓' : '✕'}</span>
          <span className="flex-1">{t.message}</span>
          <button onClick={() => onDismiss(t.id)} className="ml-2 opacity-70 hover:opacity-100">×</button>
        </div>
      ))}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Main Page                                                          */
/* ------------------------------------------------------------------ */

export default function SkillOptimizerPage() {
  const [toasts, setToasts] = useState<Toast[]>([])
  const addToast = (message: string, type: Toast['type']) => {
    const id = ++toastId
    setToasts((t) => [...t, { id, message, type }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000)
  }

  // ── State ─────────────────────────────────────────────────────
  const [skills, setSkills] = useState<OptimizableSkillInfo[]>([])
  const [selectedSkill, setSelectedSkill] = useState<OptimizableSkillInfo | null>(null)
  const [devices, setDevices] = useState<SkillDeviceInfo[]>([])
  const [embeddingModels, setEmbeddingModels] = useState<ModelProvider[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [testCases, setTestCases] = useState<OptimizationTestCase[]>([])

  // Job tracking
  const [jobId, setJobId] = useState<string | null>(null)
  const [jobStatus, setJobStatus] = useState<JobStatusResponse | null>(null)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  // ── Load skills and embedding models on mount ─────────────────
  useEffect(() => {
    fetchOptimizableSkills().then(setSkills).catch(() => addToast('Failed to load skills', 'error'))
    fetchModelProviders().then((providers) => {
      const embProviders = providers.filter((p) => p.purpose === 'Embedding' && p.enabled)
      setEmbeddingModels(embProviders)
      if (embProviders.length > 0) setSelectedModel(embProviders[0].id)
    }).catch(() => addToast('Failed to load model providers', 'error'))
  }, [])

  // ── Auto-select first skill ───────────────────────────────────
  useEffect(() => {
    if (skills.length > 0 && !selectedSkill) {
      setSelectedSkill(skills[0])
    }
  }, [skills, selectedSkill])

  // ── Load devices when skill changes ───────────────────────────
  useEffect(() => {
    if (!selectedSkill) return
    fetchSkillDevices(selectedSkill.skillId)
      .then(setDevices)
      .catch(() => addToast('Failed to load devices', 'error'))
  }, [selectedSkill])

  // ── Cleanup polling on unmount ────────────────────────────────
  useEffect(() => {
    return () => { if (pollRef.current) clearInterval(pollRef.current) }
  }, [])

  // ── Auto-generate test cases from device names ────────────────
  const autoGenerateTestCases = useCallback(() => {
    const cases: OptimizationTestCase[] = devices.map((d) => ({
      searchTerm: d.friendlyName,
      expectedEntityId: d.entityId,
      maxResults: 1,
      variant: 'auto',
    }))
    setTestCases(cases)
    addToast(`Generated ${cases.length} test cases from devices`, 'success')
  }, [devices])

  // ── Import from traces ────────────────────────────────────────
  const importFromTraces = useCallback(async () => {
    if (!selectedSkill) return
    try {
      const traces = await fetchSkillTraces(selectedSkill.skillId, 200)
      if (traces.length === 0) {
        addToast('No trace data found for this skill', 'error')
        return
      }
      const newCases: OptimizationTestCase[] = traces.map((t) => ({
        searchTerm: t.searchTerm,
        expectedEntityId: '',
        maxResults: 1,
        variant: 'trace',
      }))
      setTestCases((prev) => {
        const existing = new Set(prev.map((c) => c.searchTerm.toLowerCase()))
        const unique = newCases.filter((c) => !existing.has(c.searchTerm.toLowerCase()))
        return [...prev, ...unique]
      })
      addToast(`Imported ${traces.length} search terms from traces`, 'success')
    } catch {
      addToast('Failed to import traces', 'error')
    }
  }, [selectedSkill])

  // ── Test case CRUD ────────────────────────────────────────────
  const addTestCase = () => {
    setTestCases((prev) => [...prev, { searchTerm: '', expectedEntityId: '', maxResults: 1, variant: 'manual' }])
  }

  const updateTestCase = (index: number, updates: Partial<OptimizationTestCase>) => {
    setTestCases((prev) => prev.map((c, i) => i === index ? { ...c, ...updates } : c))
  }

  const removeTestCase = (index: number) => {
    setTestCases((prev) => prev.filter((_, i) => i !== index))
  }

  // ── Start optimization ────────────────────────────────────────
  const handleStartOptimization = async () => {
    if (!selectedSkill || testCases.length === 0 || !selectedModel) return

    const validCases = testCases.filter((c) => c.searchTerm && c.expectedEntityId)
    if (validCases.length === 0) {
      addToast('At least one test case with search term and expected entity is required', 'error')
      return
    }

    try {
      const { jobId: newJobId } = await startOptimization(selectedSkill.skillId, selectedModel, validCases)
      setJobId(newJobId)
      setJobStatus(null)
      addToast('Optimization started', 'success')

      // Start polling
      pollRef.current = setInterval(async () => {
        try {
          const status = await fetchOptimizerJob(newJobId)
          setJobStatus(status)
          if (status.status !== 'running') {
            if (pollRef.current) clearInterval(pollRef.current)
            pollRef.current = null
            if (status.status === 'completed') addToast('Optimization completed!', 'success')
            else if (status.status === 'failed') addToast(`Optimization failed: ${status.error}`, 'error')
          }
        } catch {
          if (pollRef.current) clearInterval(pollRef.current)
          pollRef.current = null
        }
      }, 1500)
    } catch {
      addToast('Failed to start optimization', 'error')
    }
  }

  // ── Cancel job ────────────────────────────────────────────────
  const handleCancel = async () => {
    if (!jobId) return
    try {
      await cancelOptimizerJob(jobId)
      if (pollRef.current) clearInterval(pollRef.current)
      pollRef.current = null
      addToast('Job cancelled', 'success')
    } catch {
      addToast('Failed to cancel job', 'error')
    }
  }

  // ── Apply results to config ───────────────────────────────────
  const handleApply = async () => {
    if (!selectedSkill || !jobStatus?.result) return
    const { bestParams } = jobStatus.result
    try {
      await updateConfigSection(selectedSkill.configSection, {
        HybridSimilarityThreshold: String(bestParams.threshold),
        EmbeddingWeight: String(bestParams.embeddingWeight),
        ScoreDropoffRatio: String(bestParams.scoreDropoffRatio),
      })
      addToast('Optimal parameters applied to configuration', 'success')
    } catch {
      addToast('Failed to apply configuration', 'error')
    }
  }

  const isRunning = jobStatus?.status === 'running'
  const progress = jobStatus?.progress
  const result = jobStatus?.result

  // ── Render ────────────────────────────────────────────────────
  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <ToastContainer toasts={toasts} onDismiss={(id) => setToasts((t) => t.filter((x) => x.id !== id))} />

      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-2xl font-bold text-light">Skill Optimizer</h1>
          <p className="mt-1 text-sm text-fog">
            Find optimal matching parameters for your environment
          </p>
        </div>
      </div>

      {/* ── Skill & Model Selection ─────────────────────────────── */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <div className="rounded-2xl border border-stone/40 bg-obsidian/60 p-5">
          <label className="mb-2 block text-xs font-medium uppercase tracking-wider text-fog">Skill</label>
          <CustomSelect
            options={skills.map((s) => ({ value: s.skillId, label: s.displayName }))}
            value={selectedSkill?.skillId ?? ''}
            onChange={(v) => setSelectedSkill(skills.find((s) => s.skillId === v) ?? null)}
          />
          {selectedSkill && (
            <div className="mt-3 space-y-1 text-xs text-fog">
              <p>Threshold: <span className="text-light">{selectedSkill.currentParams.threshold}</span></p>
              <p>Embedding Weight: <span className="text-light">{selectedSkill.currentParams.embeddingWeight}</span></p>
              <p>Score Drop-off: <span className="text-light">{selectedSkill.currentParams.scoreDropoffRatio}</span></p>
            </div>
          )}
        </div>

        <div className="rounded-2xl border border-stone/40 bg-obsidian/60 p-5">
          <label className="mb-2 block text-xs font-medium uppercase tracking-wider text-fog">Embedding Model</label>
          <CustomSelect
            options={embeddingModels.map((m) => ({ value: m.id, label: `${m.name} (${m.providerType})` }))}
            value={selectedModel}
            onChange={(v) => setSelectedModel(v)}
          />
          <p className="mt-2 text-xs text-fog">
            {devices.length} devices cached • {embeddingModels.length} embedding model(s)
          </p>
        </div>
      </div>

      {/* ── Test Cases ──────────────────────────────────────────── */}
      <div className="rounded-2xl border border-stone/40 bg-obsidian/60 p-5">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-light">
            Test Cases <span className="text-fog">({testCases.length})</span>
          </h2>
          <div className="flex gap-2">
            <button
              onClick={autoGenerateTestCases}
              disabled={devices.length === 0}
              className="rounded-lg bg-charcoal/80 px-3 py-1.5 text-xs font-medium text-fog hover:text-light disabled:opacity-40"
            >
              Auto-generate
            </button>
            <button
              onClick={importFromTraces}
              disabled={!selectedSkill}
              className="rounded-lg bg-charcoal/80 px-3 py-1.5 text-xs font-medium text-fog hover:text-light disabled:opacity-40"
            >
              Import from Traces
            </button>
            <button
              onClick={addTestCase}
              className="rounded-lg bg-amber/20 px-3 py-1.5 text-xs font-medium text-amber hover:bg-amber/30"
            >
              + Add
            </button>
          </div>
        </div>

        {testCases.length === 0 ? (
          <p className="py-8 text-center text-sm text-fog">
            No test cases yet. Auto-generate from devices, import from traces, or add manually.
          </p>
        ) : (
          <div className="max-h-96 overflow-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="border-b border-stone/30 text-left text-fog">
                  <th className="px-2 py-2">Search Term</th>
                  <th className="px-2 py-2">Expected Entity</th>
                  <th className="px-2 py-2 w-20">Max</th>
                  <th className="px-2 py-2 w-16">Type</th>
                  <th className="px-2 py-2 w-8"></th>
                </tr>
              </thead>
              <tbody>
                {testCases.map((tc, i) => (
                  <tr key={i} className="border-b border-stone/20 hover:bg-charcoal/30">
                    <td className="px-2 py-1.5">
                      <input
                        type="text"
                        value={tc.searchTerm}
                        onChange={(e) => updateTestCase(i, { searchTerm: e.target.value })}
                        className="w-full rounded border border-stone/30 bg-charcoal/40 px-2 py-1 text-xs text-light"
                        placeholder="Search term..."
                      />
                    </td>
                    <td className="px-2 py-1.5">
                      <CustomSelect
                        options={[{ value: '', label: 'Select device...' }, ...devices.map((d) => ({ value: d.entityId, label: d.friendlyName }))]}
                        value={tc.expectedEntityId}
                        onChange={(v) => updateTestCase(i, { expectedEntityId: v })}
                        size="sm"
                      />
                    </td>
                    <td className="px-2 py-1.5">
                      <input
                        type="number"
                        min={1}
                        max={50}
                        value={tc.maxResults}
                        onChange={(e) => updateTestCase(i, { maxResults: parseInt(e.target.value) || 3 })}
                        className="w-full rounded border border-stone/30 bg-charcoal/40 px-2 py-1 text-xs text-light"
                      />
                    </td>
                    <td className="px-2 py-1.5">
                      <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium ${
                        tc.variant === 'auto' ? 'bg-sage/20 text-sage' :
                        tc.variant === 'trace' ? 'bg-amber/20 text-amber' :
                        'bg-fog/20 text-fog'
                      }`}>
                        {tc.variant ?? 'manual'}
                      </span>
                    </td>
                    <td className="px-2 py-1.5">
                      <button
                        onClick={() => removeTestCase(i)}
                        className="text-fog hover:text-ember"
                      >
                        ×
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ── Controls ────────────────────────────────────────────── */}
      <div className="flex items-center gap-3">
        {!isRunning ? (
          <button
            onClick={handleStartOptimization}
            disabled={testCases.length === 0 || !selectedModel || !selectedSkill}
            className="rounded-xl bg-amber px-5 py-2.5 text-sm font-semibold text-obsidian shadow-md hover:bg-amber/90 disabled:opacity-40"
          >
            Find Optimal Values
          </button>
        ) : (
          <button
            onClick={handleCancel}
            className="rounded-xl bg-ember/80 px-5 py-2.5 text-sm font-semibold text-light hover:bg-ember"
          >
            Cancel
          </button>
        )}

        {result && !isRunning && (
          <button
            onClick={handleApply}
            className="rounded-xl bg-sage/80 px-5 py-2.5 text-sm font-semibold text-obsidian hover:bg-sage"
          >
            Apply Results
          </button>
        )}
      </div>

      {/* ── Progress ────────────────────────────────────────────── */}
      {isRunning && progress && (
        <div className="rounded-2xl border border-stone/40 bg-obsidian/60 p-5">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-light">Optimizing...</h2>
            <span className="text-xs text-fog">
              Iteration {progress.iteration} • {progress.evaluatedPoints} points evaluated
            </span>
          </div>

          <div className="mb-2 h-2 w-full overflow-hidden rounded-full bg-charcoal/60">
            <div
              className="h-full rounded-full bg-amber transition-all duration-300"
              style={{ width: `${(progress.currentScore / progress.maxScore) * 100}%` }}
            />
          </div>

          <div className="flex items-center justify-between text-xs text-fog">
            <span>Score: {progress.currentScore.toFixed(1)} / {progress.maxScore.toFixed(0)}</span>
            <span>
              T={progress.bestParams.threshold.toFixed(4)}{' '}
              W={progress.bestParams.embeddingWeight.toFixed(4)}{' '}
              D={progress.bestParams.scoreDropoffRatio.toFixed(4)}
            </span>
            <span>Step: {progress.step.toFixed(4)}</span>
          </div>

          {progress.message && (
            <p className="mt-2 text-xs text-fog/70">{progress.message}</p>
          )}
        </div>
      )}

      {/* ── Results ─────────────────────────────────────────────── */}
      {result && !isRunning && <ResultsPanel result={result} currentParams={selectedSkill?.currentParams} />}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Results Panel                                                      */
/* ------------------------------------------------------------------ */

function ResultsPanel({ result, currentParams }: {
  result: NonNullable<JobStatusResponse['result']>
  currentParams?: OptimizableSkillInfo['currentParams']
}) {
  const scorePercent = (result.score / result.maxScore) * 100

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="rounded-2xl border border-stone/40 bg-obsidian/60 p-5">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-light">Results</h2>
          <span className={`rounded-lg px-2 py-1 text-xs font-bold ${
            result.isPerfect ? 'bg-sage/20 text-sage' : scorePercent >= 80 ? 'bg-amber/20 text-amber' : 'bg-ember/20 text-ember'
          }`}>
            {result.score.toFixed(1)} / {result.maxScore.toFixed(0)} ({scorePercent.toFixed(0)}%)
          </span>
        </div>

        <div className="grid grid-cols-3 gap-4">
          <ParamComparison label="Threshold" current={currentParams?.threshold} optimal={result.bestParams.threshold} />
          <ParamComparison label="Embedding Weight" current={currentParams?.embeddingWeight} optimal={result.bestParams.embeddingWeight} />
          <ParamComparison label="Score Drop-off" current={currentParams?.scoreDropoffRatio} optimal={result.bestParams.scoreDropoffRatio} />
        </div>

        <p className="mt-3 text-xs text-fog">
          {result.totalIterations} iterations • {result.totalEvaluatedPoints} parameter points evaluated
        </p>
      </div>

      {/* Per-case breakdown */}
      <div className="rounded-2xl border border-stone/40 bg-obsidian/60 p-5">
        <h2 className="mb-3 text-sm font-semibold text-light">Per-Test-Case Breakdown</h2>
        <div className="max-h-80 overflow-auto">
          <table className="w-full text-xs">
            <thead>
              <tr className="border-b border-stone/30 text-left text-fog">
                <th className="px-2 py-2 w-8"></th>
                <th className="px-2 py-2">Search Term</th>
                <th className="px-2 py-2">Expected Entity</th>
                <th className="px-2 py-2 w-20">Matches</th>
                <th className="px-2 py-2 w-16">Score</th>
              </tr>
            </thead>
            <tbody>
              {result.caseResults.map((cr: OptimizationCaseResult, i: number) => (
                <CaseRow key={i} caseResult={cr} />
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

function ParamComparison({ label, current, optimal }: {
  label: string
  current?: number
  optimal: number
}) {
  const changed = current !== undefined && Math.abs(current - optimal) > 0.001

  return (
    <div className="rounded-lg border border-stone/30 bg-charcoal/40 p-3">
      <p className="mb-1 text-[10px] font-medium uppercase tracking-wider text-fog">{label}</p>
      <div className="flex items-baseline gap-2">
        <span className="text-lg font-bold text-light">{optimal.toFixed(4)}</span>
        {changed && current !== undefined && (
          <span className="text-[10px] text-fog line-through">{current.toFixed(4)}</span>
        )}
      </div>
    </div>
  )
}

function CaseRow({ caseResult }: { caseResult: OptimizationCaseResult }) {
  const { testCase, found, matchCount, countWithinLimit, caseScore } = caseResult
  const maxScore = 4 // FoundWeight + CountWeight
  const status = caseScore >= maxScore ? '✅' : found ? '⚠️' : '❌'

  return (
    <tr className="border-b border-stone/20">
      <td className="px-2 py-1.5 text-center">{status}</td>
      <td className="px-2 py-1.5 text-light">{testCase.searchTerm}</td>
      <td className="px-2 py-1.5 font-mono text-fog">{testCase.expectedEntityId}</td>
      <td className="px-2 py-1.5">
        <span className={countWithinLimit ? 'text-sage' : 'text-amber'}>
          {matchCount}/{testCase.maxResults}
        </span>
      </td>
      <td className="px-2 py-1.5">
        <span className={`font-medium ${caseScore >= maxScore ? 'text-sage' : found ? 'text-amber' : 'text-ember'}`}>
          {caseScore.toFixed(0)}/{maxScore}
        </span>
      </td>
    </tr>
  )
}
