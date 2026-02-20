import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { fetchExports, createExport, getExportDownloadUrl, fetchStats } from '../api'
import { LabelStatus } from '../types'
import type { ExportFilterCriteria } from '../types'

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
      <h2 className="text-lg font-semibold">Dataset Exports</h2>

      {/* Export Form */}
      <div className="rounded-lg border border-gray-700 bg-gray-800 p-4">
        <h3 className="mb-3 text-sm font-semibold uppercase text-gray-400">Generate Export</h3>
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1 block text-xs text-gray-400">Label Filter</label>
            <select
              value={labelFilter}
              onChange={(e) => setLabelFilter(e.target.value)}
              className="rounded border border-gray-600 bg-gray-900 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
            >
              <option value="">All</option>
              <option value="0">Unlabeled</option>
              <option value="1">Positive</option>
              <option value="2">Negative</option>
            </select>
          </div>
          <div>
            <label className="mb-1 block text-xs text-gray-400">From</label>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="rounded border border-gray-600 bg-gray-900 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
            />
          </div>
          <div>
            <label className="mb-1 block text-xs text-gray-400">To</label>
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="rounded border border-gray-600 bg-gray-900 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
            />
          </div>
          <div>
            <label className="mb-1 block text-xs text-gray-400">Agent</label>
            <select
              value={agentFilter}
              onChange={(e) => setAgentFilter(e.target.value)}
              className="rounded border border-gray-600 bg-gray-900 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
            >
              <option value="">All Agents</option>
              {stats && Object.keys(stats.byAgent).sort().map((agentId) => (
                <option key={agentId} value={agentId}>
                  {agentId} ({stats.byAgent[agentId]})
                </option>
              ))}
            </select>
          </div>
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input
              type="checkbox"
              checked={includeCorrections}
              onChange={(e) => setIncludeCorrections(e.target.checked)}
              className="rounded border-gray-600"
            />
            Include Corrections
          </label>
        </div>
        <button
          onClick={handleExport}
          disabled={mutation.isPending}
          className="mt-4 rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
        >
          {mutation.isPending ? 'Generating…' : 'Generate Export'}
        </button>
        {mutation.isError && (
          <p className="mt-2 text-sm text-red-400">Export failed. Please try again.</p>
        )}
        {mutation.isSuccess && (
          <p className="mt-2 text-sm text-green-400">Export created successfully.</p>
        )}
      </div>

      {/* Export History */}
      <div>
        <h3 className="mb-3 text-sm font-semibold uppercase text-gray-400">Export History</h3>
        {isLoading && <p className="text-gray-400">Loading exports…</p>}

        {exports && exports.length === 0 && (
          <p className="text-gray-500">No exports yet.</p>
        )}

        {exports && exports.length > 0 && (
          <div className="overflow-x-auto rounded-lg border border-gray-700">
            <table className="w-full text-left text-sm">
              <thead className="bg-gray-800 text-xs uppercase text-gray-400">
                <tr>
                  <th className="px-4 py-3">Timestamp</th>
                  <th className="px-4 py-3">Filter</th>
                  <th className="px-4 py-3">Records</th>
                  <th className="px-4 py-3">Size</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Download</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-700">
                {exports.map((exp) => (
                  <tr key={exp.id} className="bg-gray-900">
                    <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                      {formatDate(exp.timestamp)}
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-400">
                      {labelName(exp.filterCriteria.labelFilter)}
                      {exp.filterCriteria.agentFilter && ` · ${exp.filterCriteria.agentFilter}`}
                      {exp.filterCriteria.fromDate && ` · from ${exp.filterCriteria.fromDate}`}
                      {exp.filterCriteria.toDate && ` · to ${exp.filterCriteria.toDate}`}
                    </td>
                    <td className="px-4 py-3">{exp.recordCount}</td>
                    <td className="px-4 py-3 text-gray-300">{formatBytes(exp.fileSizeBytes)}</td>
                    <td className="px-4 py-3">
                      {exp.isComplete ? (
                        <span className="text-green-400">Complete</span>
                      ) : (
                        <span className="text-yellow-400">Processing…</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {exp.isComplete && (
                        <a
                          href={getExportDownloadUrl(exp.id)}
                          className="text-indigo-400 hover:text-indigo-300"
                        >
                          Download
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
