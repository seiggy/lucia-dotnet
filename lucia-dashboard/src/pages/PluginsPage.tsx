import { useEffect, useState } from 'react'
import {
  Puzzle,
  Store,
  Search,
  Settings,
  Download,
  Power,
  PowerOff,
  Trash2,
} from 'lucide-react'
import type { InstalledPlugin, AvailablePlugin } from '../types'
import {
  fetchInstalledPlugins,
  fetchAvailablePlugins,
  enablePlugin,
  disablePlugin,
  uninstallPlugin,
  installPlugin,
} from '../api'
import RestartBanner from '../components/RestartBanner'
import PluginRepoDialog from '../components/PluginRepoDialog'

type Tab = 'installed' | 'store'

interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
}

export default function PluginsPage() {
  const [tab, setTab] = useState<Tab>('installed')
  const [repoDialogOpen, setRepoDialogOpen] = useState(false)
  const [installed, setInstalled] = useState<InstalledPlugin[]>([])
  const [available, setAvailable] = useState<AvailablePlugin[]>([])
  const [search, setSearch] = useState('')
  const [loadingInstalled, setLoadingInstalled] = useState(true)
  const [loadingStore, setLoadingStore] = useState(false)
  const [busyIds, setBusyIds] = useState<Set<string>>(new Set())
  const [toasts, setToasts] = useState<Toast[]>([])

  const addToast = (message: string, type: 'success' | 'error') => {
    const id = Date.now()
    setToasts(prev => [...prev, { id, message, type }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 3000)
  }

  const markBusy = (id: string, busy: boolean) =>
    setBusyIds(prev => {
      const next = new Set(prev)
      busy ? next.add(id) : next.delete(id)
      return next
    })

  const loadInstalled = async () => {
    setLoadingInstalled(true)
    try {
      setInstalled(await fetchInstalledPlugins())
    } catch {
      addToast('Failed to load installed plugins', 'error')
    } finally {
      setLoadingInstalled(false)
    }
  }

  const loadStore = async () => {
    setLoadingStore(true)
    try {
      setAvailable(await fetchAvailablePlugins(search || undefined))
    } catch {
      addToast('Failed to load available plugins', 'error')
    } finally {
      setLoadingStore(false)
    }
  }

  useEffect(() => {
    loadInstalled()
    loadStore()
  }, [])

  useEffect(() => {
    if (tab === 'store') loadStore()
  }, [tab, search])

  const handleToggle = async (p: InstalledPlugin) => {
    markBusy(p.id, true)
    try {
      if (p.enabled) {
        await disablePlugin(p.id)
      } else {
        await enablePlugin(p.id)
      }
      await loadInstalled()
      addToast(`${p.name || p.id} ${p.enabled ? 'disabled' : 'enabled'}`, 'success')
    } catch {
      addToast(`Failed to toggle ${p.id}`, 'error')
    } finally {
      markBusy(p.id, false)
    }
  }

  const handleUninstall = async (p: InstalledPlugin) => {
    markBusy(p.id, true)
    try {
      await uninstallPlugin(p.id)
      await loadInstalled()
      addToast(`${p.name || p.id} uninstalled`, 'success')
    } catch {
      addToast(`Failed to uninstall ${p.id}`, 'error')
    } finally {
      markBusy(p.id, false)
    }
  }

  const handleInstall = async (p: AvailablePlugin) => {
    markBusy(p.id, true)
    try {
      await installPlugin(p.id)
      await loadStore()
      await loadInstalled()
      addToast(`${p.name} installed`, 'success')
    } catch {
      addToast(`Failed to install ${p.name}`, 'error')
    } finally {
      markBusy(p.id, false)
    }
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-light">Plugins</h1>
          <p className="text-sm text-fog">Manage plugins and repositories</p>
        </div>
        <button
          onClick={() => setRepoDialogOpen(true)}
          className="flex items-center gap-1.5 rounded-lg border border-stone/40 px-3 py-2 text-sm text-fog hover:bg-stone/40 hover:text-light"
        >
          <Settings className="h-4 w-4" />
          Repositories
        </button>
      </div>

      <RestartBanner />

      {/* Tabs */}
      <div className="flex gap-1 rounded-lg border border-stone/40 bg-obsidian p-1">
        {(
          [
            { id: 'installed' as Tab, label: 'Installed', icon: Puzzle, count: installed.length },
            { id: 'store' as Tab, label: 'Store', icon: Store, count: available.length },
          ] as const
        ).map(({ id, label, icon: Icon, count }) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`flex flex-1 items-center justify-center gap-2 rounded-md px-4 py-2.5 text-sm font-medium transition-colors ${
              tab === id
                ? 'bg-amber/10 text-amber'
                : 'text-fog hover:text-cloud hover:bg-stone/40'
            }`}
          >
            <Icon className="h-4 w-4" />
            {label}
            <span className="rounded-full bg-stone/40 px-2 py-0.5 text-xs">{count}</span>
          </button>
        ))}
      </div>

      {/* Installed Tab */}
      {tab === 'installed' && (
        <div className="space-y-3">
          {loadingInstalled ? (
            <p className="py-8 text-center text-sm text-fog">Loading…</p>
          ) : installed.length === 0 ? (
            <p className="py-8 text-center text-sm text-fog">No plugins installed</p>
          ) : (
            installed.map(p => (
              <div
                key={p.id}
                className="flex items-center justify-between rounded-lg border border-stone/40 bg-obsidian px-4 py-3"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-light">{p.name || p.id}</span>
                    <span className="rounded bg-stone/40 px-1.5 py-0.5 text-xs text-fog">
                      {p.version}
                    </span>
                    {p.source === 'bundled' && (
                      <span className="rounded bg-amber/20 px-1.5 py-0.5 text-xs text-amber">
                        bundled
                      </span>
                    )}
                  </div>
                  {p.description && <p className="mt-0.5 text-xs text-fog">{p.description}</p>}
                </div>
                <div className="ml-4 flex shrink-0 items-center gap-2">
                  <button
                    onClick={() => handleToggle(p)}
                    disabled={busyIds.has(p.id)}
                    className={`rounded p-1.5 transition-colors ${
                      p.enabled
                        ? 'text-sage hover:bg-sage/20'
                        : 'text-fog hover:bg-stone/40'
                    } disabled:opacity-50`}
                    title={p.enabled ? 'Disable' : 'Enable'}
                  >
                    {p.enabled ? <Power className="h-4 w-4" /> : <PowerOff className="h-4 w-4" />}
                  </button>
                  {p.source !== 'bundled' && (
                    <button
                      onClick={() => handleUninstall(p)}
                      disabled={busyIds.has(p.id)}
                      className="rounded p-1.5 text-fog hover:bg-rose/20 hover:text-rose disabled:opacity-50"
                      title="Uninstall"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
      )}

      {/* Store Tab */}
      {tab === 'store' && (
        <div className="space-y-4">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fog" />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search plugins…"
              className="w-full rounded-lg border border-stone/40 bg-obsidian py-2 pl-10 pr-3 text-sm text-light placeholder:text-fog/50 focus:border-amber/50 focus:outline-none"
            />
          </div>

          {loadingStore ? (
            <p className="py-8 text-center text-sm text-fog">Loading…</p>
          ) : available.length === 0 ? (
            <p className="py-8 text-center text-sm text-fog">
              No plugins found. Try adding a repository first.
            </p>
          ) : (
            <div className="space-y-3">
              {available.map(p => (
                <div
                  key={`${p.repositoryId}-${p.id}`}
                  className="flex items-center justify-between rounded-lg border border-stone/40 bg-obsidian px-4 py-3"
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium text-light">{p.name}</span>
                      <span className="rounded bg-stone/40 px-1.5 py-0.5 text-xs text-fog">
                        {p.version}
                      </span>
                    </div>
                    <p className="mt-0.5 text-xs text-fog">{p.description}</p>
                    <div className="mt-1 flex flex-wrap gap-1">
                      {p.tags.map(t => (
                        <span
                          key={t}
                          className="rounded bg-stone/30 px-1.5 py-0.5 text-[10px] text-fog"
                        >
                          {t}
                        </span>
                      ))}
                      <span className="text-[10px] text-fog/60">by {p.author}</span>
                    </div>
                  </div>
                  <div className="ml-4 shrink-0">
                    {installed.some(i => i.id === p.id) ? (
                      <span className="rounded-lg bg-sage/20 px-3 py-1.5 text-xs font-medium text-sage">
                        Installed
                      </span>
                    ) : (
                      <button
                        onClick={() => handleInstall(p)}
                        disabled={busyIds.has(p.id)}
                        className="flex items-center gap-1.5 rounded-lg bg-amber/20 px-3 py-1.5 text-sm font-medium text-amber hover:bg-amber/30 disabled:opacity-50"
                      >
                        <Download className="h-3.5 w-3.5" />
                        Install
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      <PluginRepoDialog open={repoDialogOpen} onClose={() => setRepoDialogOpen(false)} />

      {/* Toasts */}
      <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
        {toasts.map(t => (
          <div
            key={t.id}
            className={`rounded-lg px-4 py-3 text-sm shadow-lg ${
              t.type === 'success' ? 'bg-sage/20 text-light' : 'bg-rose/20 text-light'
            }`}
          >
            {t.message}
          </div>
        ))}
      </div>
    </div>
  )
}
