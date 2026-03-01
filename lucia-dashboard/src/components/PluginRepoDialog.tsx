import { useEffect, useState } from 'react'
import { X, Plus, Trash2, RefreshCw } from 'lucide-react'
import type { PluginRepository } from '../types'
import { fetchPluginRepos, addPluginRepo, deletePluginRepo, syncPluginRepo } from '../api'

interface Props {
  open: boolean
  onClose: () => void
}

export default function PluginRepoDialog({ open, onClose }: Props) {
  const [repos, setRepos] = useState<PluginRepository[]>([])
  const [url, setUrl] = useState('')
  const [loading, setLoading] = useState(false)
  const [syncing, setSyncing] = useState<string | null>(null)

  const load = async () => {
    try {
      setRepos(await fetchPluginRepos())
    } catch {
      /* ignore */
    }
  }

  useEffect(() => {
    if (open) load()
  }, [open])

  if (!open) return null

  const handleAdd = async () => {
    if (!url.trim()) return
    setLoading(true)
    try {
      await addPluginRepo({ url: url.trim() })
      setUrl('')
      await load()
    } catch {
      /* ignore */
    } finally {
      setLoading(false)
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await deletePluginRepo(id)
      await load()
    } catch {
      /* ignore */
    }
  }

  const handleSync = async (id: string) => {
    setSyncing(id)
    try {
      await syncPluginRepo(id)
      await load()
    } catch {
      /* ignore */
    } finally {
      setSyncing(null)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="w-full max-w-lg rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl">
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-light">Plugin Repositories</h3>
          <button onClick={onClose} className="text-fog hover:text-light">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="mt-4 flex gap-2">
          <input
            type="text"
            value={url}
            onChange={e => setUrl(e.target.value)}
            placeholder="https://github.com/owner/repo"
            className="flex-1 rounded-lg border border-stone/40 bg-midnight px-3 py-2 text-sm text-light placeholder:text-fog/50 focus:border-amber/50 focus:outline-none"
            onKeyDown={e => e.key === 'Enter' && handleAdd()}
          />
          <button
            onClick={handleAdd}
            disabled={loading || !url.trim()}
            className="flex items-center gap-1.5 rounded-lg bg-amber/20 px-3 py-2 text-sm font-medium text-amber hover:bg-amber/30 disabled:opacity-50"
          >
            <Plus className="h-4 w-4" />
            Add
          </button>
        </div>

        <div className="mt-4 max-h-64 space-y-2 overflow-y-auto">
          {repos.length === 0 && (
            <p className="py-4 text-center text-sm text-fog">No repositories configured</p>
          )}
          {repos.map(repo => (
            <div
              key={repo.id}
              className="flex items-center justify-between rounded-lg border border-stone/40 bg-midnight px-3 py-2.5"
            >
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-light">{repo.name || repo.url}</p>
                <p className="truncate text-xs text-fog">{repo.url}</p>
                {repo.lastSyncedAt && (
                  <p className="text-xs text-fog/60">
                    Synced {new Date(repo.lastSyncedAt).toLocaleDateString()}
                  </p>
                )}
              </div>
              <div className="ml-3 flex shrink-0 gap-1">
                <button
                  onClick={() => handleSync(repo.id)}
                  disabled={syncing === repo.id}
                  className="rounded p-1.5 text-fog hover:bg-stone/40 hover:text-light disabled:opacity-50"
                  title="Sync"
                >
                  <RefreshCw className={`h-4 w-4 ${syncing === repo.id ? 'animate-spin' : ''}`} />
                </button>
                <button
                  onClick={() => handleDelete(repo.id)}
                  className="rounded p-1.5 text-fog hover:bg-rose/20 hover:text-rose"
                  title="Remove"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
