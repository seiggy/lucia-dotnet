import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { fetchExports, createExport, getExportDownloadUrl, fetchStats } from '../api'
import { LabelStatus } from '../types'
import type { ExportFilterCriteria } from '../types'
import { Download, FileDown, Filter, CheckCircle2, Loader2 } from 'lucide-react'

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function labelName(val: number | null): string {
  switch (val) {
    case LabelStatus.Positive:
      return 'Positive'
    case LabelStatus.Negative:
      return 'Negative'
    case LabelStatus.Unlabeled:
      return 'Unlabeled'
    default:
      return 'All'
  }
}

const inputStyle = 'rounded-xl border border-stone bg-basalt px-4 py-2 text-sm text-light input-focus appearance-none'

export default function ExportPage() {
  const queryClient = useQueryClient()

  const [labelFilter, setLabelFilter] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [agentFilter, setAgentFilter] = useState('')
  const [includeCorrections, setIncludeCorrections] = useState(false)

  const { data: exports, isLoading } = useQuery({
    queryKey: ['exports'],
    queryFn: fetchExports,
  })

  const { data: stats } = useQuery({
    queryKey: ['stats'],
    queryFn: fetchStats,
  })

  const mutation = useMutation({
    mutationFn: (filter: ExportFilterCriteria) => createExport(filter),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['exports'] })
    },
  })

  function handleExport() {
    const filter: ExportFilterCriteria = {
      labelFilter: labelFilter !== '' ? (Number(labelFilter) as typeof LabelStatus.Positive) : null,
      fromDate: fromDate || null,
      toDate: toDate || null,
      agentFilter: agentFilter || null,
      modelFilter: null,
      includeCorrections,
    }
    mutation.mutate(filter)
  }

  return (
    <div className="space-y-6">
      <h1 className="font-display text-2xl font-bold text-light">Dataset Exports</h1>

      {/* Export Form */}
      <div className="glass-panel rounded-xl p-5">
        <h3 className="mb-4 flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-dust">
          <Filter className="h-4 w-4" /> Generate Export
        </h3>
        <div className="flex flex-wrap items-end gap-4">
          <div>
            <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Label Filter</label>
            <select
              value={labelFilter}
              onChange={(e) => setLabelFilter(e.target.value)}
              className={inputStyle}
            >
              <option value="">All</option>
              <option value="0">Unlabeled</option>
              <option value="1">Positive</option>
              <option value="2">Negative</option>
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">From</label>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className={inputStyle}
            />
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">To</label>
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className={inputStyle}
            />
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Agent</label>
            <select
              value={agentFilter}
              onChange={(e) => setAgentFilter(e.target.value)}
              className={inputStyle}
            >
              <option value="">All Agents</option>
              {stats && Object.keys(stats.byAgent).sort().map((agentId) => (
                <option key={agentId} value={agentId}>
                  {agentId} ({stats.byAgent[agentId]})
                </option>
              ))}
            </select>
          </div>
          <label className="flex items-center gap-2 pb-0.5 text-sm text-fog">
            <input
              type="checkbox"
              checked={includeCorrections}
              onChange={(e) => setIncludeCorrections(e.target.checked)}
              className="rounded border-stone accent-amber"
            />
            Include Corrections
          </label>
        </div>
        <button
          onClick={handleExport}
          disabled={mutation.isPending}
          className="mt-5 flex items-center gap-2 rounded-xl bg-amber px-5 py-2.5 text-sm font-semibold text-void transition-all hover:bg-amber-glow disabled:opacity-40"
        >
          <FileDown className="h-4 w-4" />
          {mutation.isPending ? 'Generating…' : 'Generate Export'}
        </button>
        {mutation.isError && (
          <p className="mt-2 text-sm text-rose">Export failed. Please try again.</p>
        )}
        {mutation.isSuccess && (
          <p className="mt-2 flex items-center gap-1 text-sm text-sage"><CheckCircle2 className="h-4 w-4" /> Export created successfully.</p>
        )}
      </div>

      {/* Export History */}
      <div>
        <h3 className="mb-4 text-xs font-semibold uppercase tracking-wider text-dust">Export History</h3>
        {isLoading && <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading exports…</p>}

        {exports && exports.length === 0 && (
          <p className="text-dust">No exports yet.</p>
        )}

        {exports && exports.length > 0 && (
          <div className="glass-panel overflow-x-auto rounded-xl">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                <tr>
                  <th className="px-4 py-3">Timestamp</th>
                  <th className="px-4 py-3">Filter</th>
                  <th className="px-4 py-3">Records</th>
                  <th className="px-4 py-3">Size</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Download</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {exports.map((exp) => (
                  <tr key={exp.id} className="transition-colors hover:bg-basalt/60">
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(exp.timestamp)}
                    </td>
                    <td className="px-4 py-3 text-xs text-dust">
                      {labelName(exp.filterCriteria.labelFilter)}
                      {exp.filterCriteria.agentFilter && ` · ${exp.filterCriteria.agentFilter}`}
                      {exp.filterCriteria.fromDate && ` · from ${exp.filterCriteria.fromDate}`}
                      {exp.filterCriteria.toDate && ` · to ${exp.filterCriteria.toDate}`}
                    </td>
                    <td className="px-4 py-3 text-fog">{exp.recordCount}</td>
                    <td className="px-4 py-3 text-fog">{formatBytes(exp.fileSizeBytes)}</td>
                    <td className="px-4 py-3">
                      {exp.isComplete ? (
                        <span className="flex items-center gap-1 text-sage"><CheckCircle2 className="h-3.5 w-3.5" /> Complete</span>
                      ) : (
                        <span className="flex items-center gap-1 text-amber"><Loader2 className="h-3.5 w-3.5 animate-spin" /> Processing…</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {exp.isComplete && (
                        <a
                          href={getExportDownloadUrl(exp.id)}
                          className="flex items-center gap-1 text-amber transition-colors hover:text-amber-glow"
                        >
                          <Download className="h-4 w-4" /> Download
                        </a>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
