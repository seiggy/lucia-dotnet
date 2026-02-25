import { useState, useEffect, useCallback } from 'react'
import {
  AlarmClock as AlarmClockIcon, Plus, Trash2, Pencil, Power, PowerOff,
  Volume2, Star, X, Clock, Speaker, Bell, BellOff, Pause, Music
} from 'lucide-react'
import type { AlarmClock, AlarmSound } from '../types'
import {
  fetchAlarms, createAlarm, updateAlarm, deleteAlarm, enableAlarm, disableAlarm,
  dismissAlarm, snoozeAlarm,
  fetchAlarmSounds, createAlarmSound, deleteAlarmSound, setDefaultAlarmSound,
} from '../api'

// ── Toast notifications ──

interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
}

let toastId = 0

function ToastContainer({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: number) => void }) {
  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={`flex items-center gap-3 rounded-lg px-4 py-3 text-sm font-medium shadow-lg backdrop-blur-md transition-all ${
            t.type === 'success'
              ? 'bg-sage/20 text-sage border border-sage/30'
              : 'bg-rose/20 text-rose border border-rose/30'
          }`}
        >
          <span>{t.message}</span>
          <button onClick={() => onDismiss(t.id)} className="ml-2 opacity-60 hover:opacity-100">
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  )
}

// ── Confirm dialog ──

function ConfirmDialog({
  open, title, message, onConfirm, onCancel
}: {
  open: boolean; title: string; message: string;
  onConfirm: () => void; onCancel: () => void
}) {
  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="w-full max-w-sm rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl">
        <h3 className="text-base font-semibold text-light">{title}</h3>
        <p className="mt-2 text-sm text-fog">{message}</p>
        <div className="mt-5 flex justify-end gap-3">
          <button onClick={onCancel} className="rounded-lg px-4 py-2 text-sm text-fog hover:text-cloud hover:bg-stone/40">
            Cancel
          </button>
          <button onClick={onConfirm} className="rounded-lg bg-rose/20 px-4 py-2 text-sm font-medium text-rose hover:bg-rose/30">
            Delete
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Tab type ──

type Tab = 'alarms' | 'sounds'

// ── CRON helpers ──

const CRON_PRESETS = [
  { label: 'Every day', cron: '0 {h} * * *' },
  { label: 'Weekdays', cron: '0 {h} * * 1-5' },
  { label: 'Weekends', cron: '0 {h} * * 0,6' },
  { label: 'Custom', cron: '' },
] as const

function describeCron(cron: string): string {
  const parts = cron.split(' ')
  if (parts.length !== 5) return cron

  const [min, hour, , , dow] = parts
  const time = `${hour.padStart(2, '0')}:${min.padStart(2, '0')}`

  if (dow === '*') return `Daily at ${time}`
  if (dow === '1-5') return `Weekdays at ${time}`
  if (dow === '0,6' || dow === '6,0') return `Weekends at ${time}`

  const dayNames: Record<string, string> = { '0': 'Sun', '1': 'Mon', '2': 'Tue', '3': 'Wed', '4': 'Thu', '5': 'Fri', '6': 'Sat' }
  const days = dow.split(',').map(d => dayNames[d] || d).join(', ')
  return `${days} at ${time}`
}

function formatTimeSpan(ts: string): string {
  // Handles .NET TimeSpan format like "00:00:30" or "00:10:00"
  const match = ts.match(/^(\d+):(\d+):(\d+)/)
  if (!match) return ts
  const [, h, m, s] = match.map(Number)
  const parts: string[] = []
  if (h > 0) parts.push(`${h}h`)
  if (m > 0) parts.push(`${m}m`)
  if (s > 0) parts.push(`${s}s`)
  return parts.join(' ') || '0s'
}

function formatNextFire(dateStr: string | null): string {
  if (!dateStr) return 'Not scheduled'
  const d = new Date(dateStr)
  const now = new Date()
  const diffMs = d.getTime() - now.getTime()

  if (diffMs < 0) return 'Overdue'
  if (diffMs < 60_000) return 'In less than a minute'
  if (diffMs < 3600_000) return `In ${Math.round(diffMs / 60_000)}m`
  if (diffMs < 86400_000) {
    const h = Math.floor(diffMs / 3600_000)
    const m = Math.round((diffMs % 3600_000) / 60_000)
    return `In ${h}h ${m}m`
  }
  return d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

// ── Main page ──

export default function AlarmsPage() {
  const [tab, setTab] = useState<Tab>('alarms')
  const [alarms, setAlarms] = useState<AlarmClock[]>([])
  const [sounds, setSounds] = useState<AlarmSound[]>([])
  const [loading, setLoading] = useState(true)
  const [toasts, setToasts] = useState<Toast[]>([])

  // Alarm form state
  const [showAlarmForm, setShowAlarmForm] = useState(false)
  const [editingAlarm, setEditingAlarm] = useState<AlarmClock | null>(null)
  const [alarmForm, setAlarmForm] = useState({
    name: '', targetEntity: '', alarmSoundId: '',
    scheduleType: 'cron' as 'cron' | 'once',
    cronPreset: 'Every day',
    cronHour: '07', cronMinute: '00', cronCustom: '',
    nextFireAt: '',
    playbackIntervalSec: 30, autoDismissMin: 10,
  })

  // Sound form state
  const [showSoundForm, setShowSoundForm] = useState(false)
  const [soundForm, setSoundForm] = useState({ name: '', mediaSourceUri: '', isDefault: false })

  // Delete confirmation
  const [confirmDelete, setConfirmDelete] = useState<{ type: 'alarm' | 'sound'; id: string; name: string } | null>(null)

  const addToast = useCallback((message: string, type: 'success' | 'error') => {
    const id = ++toastId
    setToasts(prev => [...prev, { id, message, type }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 4000)
  }, [])

  const loadData = useCallback(async () => {
    try {
      const [a, s] = await Promise.all([fetchAlarms(), fetchAlarmSounds()])
      setAlarms(a)
      setSounds(s)
    } catch (err) {
      addToast(`Failed to load data: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => { loadData() }, [loadData])

  // ── Alarm CRUD ──

  function openAlarmForm(alarm?: AlarmClock) {
    if (alarm) {
      setEditingAlarm(alarm)
      const isCron = !!alarm.cronSchedule
      let preset = 'Custom'
      let hour = '07', minute = '00', custom = alarm.cronSchedule || ''
      if (alarm.cronSchedule) {
        const p = alarm.cronSchedule.split(' ')
        if (p.length === 5) {
          minute = p[0]
          hour = p[1]
          if (p[4] === '*') preset = 'Every day'
          else if (p[4] === '1-5') preset = 'Weekdays'
          else if (p[4] === '0,6' || p[4] === '6,0') preset = 'Weekends'
        }
      }

      // Parse playbackInterval from TimeSpan format
      const piMatch = alarm.playbackInterval?.match(/^(\d+):(\d+):(\d+)/)
      const piSec = piMatch ? Number(piMatch[1]) * 3600 + Number(piMatch[2]) * 60 + Number(piMatch[3]) : 30

      // Parse autoDismissAfter
      const adMatch = alarm.autoDismissAfter?.match(/^(\d+):(\d+):(\d+)/)
      const adMin = adMatch ? Math.round((Number(adMatch[1]) * 3600 + Number(adMatch[2]) * 60 + Number(adMatch[3])) / 60) : 10

      setAlarmForm({
        name: alarm.name, targetEntity: alarm.targetEntity,
        alarmSoundId: alarm.alarmSoundId || '',
        scheduleType: isCron ? 'cron' : 'once',
        cronPreset: preset,
        cronHour: hour.padStart(2, '0'), cronMinute: minute.padStart(2, '0'),
        cronCustom: custom,
        nextFireAt: alarm.nextFireAt ? new Date(alarm.nextFireAt).toISOString().slice(0, 16) : '',
        playbackIntervalSec: piSec, autoDismissMin: adMin,
      })
    } else {
      setEditingAlarm(null)
      setAlarmForm({
        name: '', targetEntity: '', alarmSoundId: '',
        scheduleType: 'cron', cronPreset: 'Every day',
        cronHour: '07', cronMinute: '00', cronCustom: '',
        nextFireAt: '', playbackIntervalSec: 30, autoDismissMin: 10,
      })
    }
    setShowAlarmForm(true)
  }

  async function saveAlarm() {
    const { name, targetEntity, alarmSoundId, scheduleType, cronPreset, cronHour, cronMinute, cronCustom, nextFireAt, playbackIntervalSec, autoDismissMin } = alarmForm

    if (!name.trim() || !targetEntity.trim()) {
      addToast('Name and target entity are required', 'error')
      return
    }

    let cronSchedule: string | null = null
    let fireAt: string | null = null

    if (scheduleType === 'cron') {
      if (cronPreset === 'Custom') {
        cronSchedule = cronCustom
      } else {
        const preset = CRON_PRESETS.find(p => p.label === cronPreset)
        if (preset) {
          cronSchedule = preset.cron
            .replace('{h}', String(Number(cronHour)))
          cronSchedule = `${Number(cronMinute)} ${cronSchedule.split(' ').slice(1).join(' ')}`
        }
      }
    } else {
      fireAt = nextFireAt ? new Date(nextFireAt).toISOString() : null
    }

    if (!cronSchedule && !fireAt) {
      addToast('A schedule (CRON or one-time) is required', 'error')
      return
    }

    try {
      const body = {
        name, targetEntity,
        alarmSoundId: alarmSoundId || null,
        cronSchedule, nextFireAt: fireAt,
        playbackInterval: `00:${String(Math.floor(playbackIntervalSec / 60)).padStart(2, '0')}:${String(playbackIntervalSec % 60).padStart(2, '0')}`,
        autoDismissAfter: `00:${String(autoDismissMin).padStart(2, '0')}:00`,
      }

      if (editingAlarm) {
        await updateAlarm(editingAlarm.id, body)
        addToast('Alarm updated', 'success')
      } else {
        await createAlarm({ ...body, isEnabled: true })
        addToast('Alarm created', 'success')
      }
      setShowAlarmForm(false)
      await loadData()
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function toggleAlarm(alarm: AlarmClock) {
    try {
      if (alarm.isEnabled) {
        await disableAlarm(alarm.id)
        addToast(`${alarm.name} disabled`, 'success')
      } else {
        await enableAlarm(alarm.id)
        addToast(`${alarm.name} enabled`, 'success')
      }
      await loadData()
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function handleDismiss(id: string) {
    try {
      await dismissAlarm(id)
      addToast('Alarm dismissed', 'success')
      await loadData()
    } catch (err) {
      addToast(`Failed to dismiss: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function handleSnooze(id: string) {
    try {
      await snoozeAlarm(id)
      addToast('Alarm snoozed for 9 minutes', 'success')
      await loadData()
    } catch (err) {
      addToast(`Failed to snooze: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  // ── Sound CRUD ──

  function openSoundForm() {
    setSoundForm({ name: '', mediaSourceUri: '', isDefault: false })
    setShowSoundForm(true)
  }

  async function saveSound() {
    if (!soundForm.name.trim() || !soundForm.mediaSourceUri.trim()) {
      addToast('Name and media source URI are required', 'error')
      return
    }
    try {
      await createAlarmSound(soundForm)
      addToast('Sound added', 'success')
      setShowSoundForm(false)
      await loadData()
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function handleSetDefault(id: string) {
    try {
      await setDefaultAlarmSound(id)
      addToast('Default sound updated', 'success')
      await loadData()
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  // ── Delete handler ──

  async function confirmDeleteHandler() {
    if (!confirmDelete) return
    try {
      if (confirmDelete.type === 'alarm') {
        await deleteAlarm(confirmDelete.id)
      } else {
        await deleteAlarmSound(confirmDelete.id)
      }
      addToast(`${confirmDelete.name} deleted`, 'success')
      setConfirmDelete(null)
      await loadData()
    } catch (err) {
      addToast(`Failed to delete: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
      setConfirmDelete(null)
    }
  }

  function getSoundName(soundId: string | null): string {
    if (!soundId) return 'TTS Fallback'
    return sounds.find(s => s.id === soundId)?.name || 'Unknown'
  }

  // ── Render ──

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <div className="flex items-center gap-3 text-fog">
          <AlarmClockIcon className="h-5 w-5 animate-pulse text-amber" />
          <span className="text-sm">Loading alarms...</span>
        </div>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-2xl font-bold text-light">Alarm Clocks</h1>
          <p className="mt-1 text-sm text-fog">Manage alarms and alarm sounds</p>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 rounded-lg border border-stone/40 bg-obsidian p-1">
        {([
          { id: 'alarms' as Tab, label: 'Alarms', icon: Bell, count: alarms.length },
          { id: 'sounds' as Tab, label: 'Sounds', icon: Music, count: sounds.length },
        ]).map(({ id, label, icon: Icon, count }) => (
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
            <span className={`ml-1 rounded-full px-2 py-0.5 text-xs ${
              tab === id ? 'bg-amber/20 text-amber' : 'bg-stone/60 text-dust'
            }`}>
              {count}
            </span>
          </button>
        ))}
      </div>

      {/* Alarms Tab */}
      {tab === 'alarms' && (
        <div className="space-y-4">
          <div className="flex justify-end">
            <button
              onClick={() => openAlarmForm()}
              className="flex items-center gap-2 rounded-lg bg-amber/10 px-4 py-2.5 text-sm font-medium text-amber transition-colors hover:bg-amber/20"
            >
              <Plus className="h-4 w-4" />
              New Alarm
            </button>
          </div>

          {alarms.length === 0 ? (
            <div className="flex flex-col items-center justify-center rounded-xl border border-stone/40 bg-basalt py-16">
              <BellOff className="h-10 w-10 text-stone" />
              <p className="mt-3 text-sm text-fog">No alarms configured</p>
              <button
                onClick={() => openAlarmForm()}
                className="mt-4 flex items-center gap-2 rounded-lg bg-amber/10 px-4 py-2 text-sm text-amber hover:bg-amber/20"
              >
                <Plus className="h-4 w-4" /> Create your first alarm
              </button>
            </div>
          ) : (
            <div className="space-y-3">
              {alarms.map(alarm => (
                <div key={alarm.id} className={`group rounded-xl border bg-basalt p-4 transition-colors ${
                  alarm.isEnabled ? 'border-stone/40' : 'border-stone/20 opacity-60'
                }`}>
                  <div className="flex items-start justify-between gap-4">
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-3">
                        <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${
                          alarm.isEnabled ? 'bg-amber/10' : 'bg-stone/40'
                        }`}>
                          <Bell className={`h-4.5 w-4.5 ${alarm.isEnabled ? 'text-amber' : 'text-dust'}`} />
                        </div>
                        <div className="min-w-0">
                          <h3 className="text-sm font-semibold text-light truncate">{alarm.name}</h3>
                          <div className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-fog">
                            <span className="flex items-center gap-1">
                              <Speaker className="h-3 w-3" />
                              {alarm.targetEntity === 'presence' ? 'Presence-based' : alarm.targetEntity}
                            </span>
                            <span className="flex items-center gap-1">
                              <Volume2 className="h-3 w-3" />
                              {getSoundName(alarm.alarmSoundId)}
                            </span>
                          </div>
                        </div>
                      </div>

                      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-dust">
                        <span className="flex items-center gap-1">
                          <Clock className="h-3 w-3" />
                          {alarm.cronSchedule
                            ? describeCron(alarm.cronSchedule)
                            : 'One-time'}
                        </span>
                        <span>Next: {formatNextFire(alarm.nextFireAt)}</span>
                        <span>Repeat: {formatTimeSpan(alarm.playbackInterval)}</span>
                        <span>Auto-dismiss: {formatTimeSpan(alarm.autoDismissAfter)}</span>
                      </div>
                    </div>

                    <div className="flex shrink-0 items-center gap-1.5">
                      <button
                        onClick={() => handleDismiss(alarm.id)}
                        title="Dismiss"
                        className="rounded-md p-2 text-fog opacity-0 transition-all hover:bg-stone/40 hover:text-cloud group-hover:opacity-100"
                      >
                        <BellOff className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleSnooze(alarm.id)}
                        title="Snooze"
                        className="rounded-md p-2 text-fog opacity-0 transition-all hover:bg-stone/40 hover:text-cloud group-hover:opacity-100"
                      >
                        <Pause className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => openAlarmForm(alarm)}
                        title="Edit"
                        className="rounded-md p-2 text-fog opacity-0 transition-all hover:bg-stone/40 hover:text-cloud group-hover:opacity-100"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => toggleAlarm(alarm)}
                        title={alarm.isEnabled ? 'Disable' : 'Enable'}
                        className={`rounded-md p-2 transition-colors ${
                          alarm.isEnabled
                            ? 'text-sage hover:bg-sage/10'
                            : 'text-dust hover:bg-stone/40 hover:text-fog'
                        }`}
                      >
                        {alarm.isEnabled ? <Power className="h-4 w-4" /> : <PowerOff className="h-4 w-4" />}
                      </button>
                      <button
                        onClick={() => setConfirmDelete({ type: 'alarm', id: alarm.id, name: alarm.name })}
                        title="Delete"
                        className="rounded-md p-2 text-fog opacity-0 transition-all hover:bg-rose/10 hover:text-rose group-hover:opacity-100"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Sounds Tab */}
      {tab === 'sounds' && (
        <div className="space-y-4">
          <div className="flex justify-end">
            <button
              onClick={openSoundForm}
              className="flex items-center gap-2 rounded-lg bg-amber/10 px-4 py-2.5 text-sm font-medium text-amber transition-colors hover:bg-amber/20"
            >
              <Plus className="h-4 w-4" />
              Add Sound
            </button>
          </div>

          {sounds.length === 0 ? (
            <div className="flex flex-col items-center justify-center rounded-xl border border-stone/40 bg-basalt py-16">
              <Volume2 className="h-10 w-10 text-stone" />
              <p className="mt-3 text-sm text-fog">No alarm sounds configured</p>
              <p className="mt-1 text-xs text-dust">Alarms will use TTS announcement as fallback</p>
              <button
                onClick={openSoundForm}
                className="mt-4 flex items-center gap-2 rounded-lg bg-amber/10 px-4 py-2 text-sm text-amber hover:bg-amber/20"
              >
                <Plus className="h-4 w-4" /> Add a sound
              </button>
            </div>
          ) : (
            <div className="overflow-hidden rounded-xl border border-stone/40">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-stone/40 bg-obsidian">
                    <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-dust">Name</th>
                    <th className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-dust">Media Source</th>
                    <th className="px-4 py-3 text-center text-xs font-medium uppercase tracking-wider text-dust">Default</th>
                    <th className="px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-dust">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {sounds.map((sound, i) => (
                    <tr key={sound.id} className={`${i > 0 ? 'border-t border-stone/20' : ''} bg-basalt hover:bg-stone/20 transition-colors`}>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-3">
                          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-amber/10">
                            <Music className="h-4 w-4 text-amber" />
                          </div>
                          <span className="font-medium text-light">{sound.name}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-fog">
                        <code className="rounded bg-stone/40 px-2 py-0.5 text-xs">{sound.mediaSourceUri}</code>
                      </td>
                      <td className="px-4 py-3 text-center">
                        {sound.isDefault ? (
                          <span className="inline-flex items-center gap-1 rounded-full bg-amber/10 px-2.5 py-1 text-xs font-medium text-amber">
                            <Star className="h-3 w-3" /> Default
                          </span>
                        ) : (
                          <button
                            onClick={() => handleSetDefault(sound.id)}
                            className="text-xs text-dust hover:text-amber transition-colors"
                          >
                            Set default
                          </button>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <button
                          onClick={() => setConfirmDelete({ type: 'sound', id: sound.id, name: sound.name })}
                          className="rounded-md p-1.5 text-fog hover:bg-rose/10 hover:text-rose transition-colors"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Alarm Form Modal */}
      {showAlarmForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="w-full max-w-lg rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between mb-5">
              <h3 className="text-base font-semibold text-light">
                {editingAlarm ? 'Edit Alarm' : 'New Alarm'}
              </h3>
              <button onClick={() => setShowAlarmForm(false)} className="rounded-md p-1.5 text-fog hover:text-cloud hover:bg-stone/40">
                <X className="h-4.5 w-4.5" />
              </button>
            </div>

            <div className="space-y-4">
              {/* Name */}
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-dust mb-1.5">Name</label>
                <input
                  value={alarmForm.name}
                  onChange={e => setAlarmForm(f => ({ ...f, name: e.target.value }))}
                  placeholder="Morning Wake Up"
                  className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light placeholder:text-stone input-focus"
                />
              </div>

              {/* Target Entity */}
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-dust mb-1.5">Target Entity</label>
                <input
                  value={alarmForm.targetEntity}
                  onChange={e => setAlarmForm(f => ({ ...f, targetEntity: e.target.value }))}
                  placeholder='media_player.bedroom or "presence"'
                  className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light placeholder:text-stone input-focus"
                />
                <p className="mt-1 text-xs text-dust">Use "presence" for automatic room detection</p>
              </div>

              {/* Alarm Sound */}
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-dust mb-1.5">Alarm Sound</label>
                <select
                  value={alarmForm.alarmSoundId}
                  onChange={e => setAlarmForm(f => ({ ...f, alarmSoundId: e.target.value }))}
                  className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light input-focus"
                >
                  <option value="">TTS Announcement (default)</option>
                  {sounds.map(s => (
                    <option key={s.id} value={s.id}>{s.name}{s.isDefault ? ' ★' : ''}</option>
                  ))}
                </select>
              </div>

              {/* Schedule Type */}
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-dust mb-1.5">Schedule</label>
                <div className="flex gap-2">
                  <button
                    onClick={() => setAlarmForm(f => ({ ...f, scheduleType: 'cron' }))}
                    className={`flex-1 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                      alarmForm.scheduleType === 'cron' ? 'bg-amber/10 text-amber' : 'bg-stone/30 text-fog hover:text-cloud'
                    }`}
                  >
                    Recurring
                  </button>
                  <button
                    onClick={() => setAlarmForm(f => ({ ...f, scheduleType: 'once' }))}
                    className={`flex-1 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                      alarmForm.scheduleType === 'once' ? 'bg-amber/10 text-amber' : 'bg-stone/30 text-fog hover:text-cloud'
                    }`}
                  >
                    One-time
                  </button>
                </div>
              </div>

              {/* CRON schedule */}
              {alarmForm.scheduleType === 'cron' && (
                <div className="space-y-3">
                  <div className="flex gap-2">
                    {CRON_PRESETS.map(p => (
                      <button
                        key={p.label}
                        onClick={() => setAlarmForm(f => ({ ...f, cronPreset: p.label }))}
                        className={`rounded-lg px-3 py-1.5 text-xs font-medium transition-colors ${
                          alarmForm.cronPreset === p.label ? 'bg-amber/10 text-amber' : 'bg-stone/30 text-fog hover:text-cloud'
                        }`}
                      >
                        {p.label}
                      </button>
                    ))}
                  </div>
                  {alarmForm.cronPreset !== 'Custom' ? (
                    <div className="flex items-center gap-2">
                      <span className="text-sm text-fog">Time:</span>
                      <input
                        type="number" min="0" max="23"
                        value={alarmForm.cronHour}
                        onChange={e => setAlarmForm(f => ({ ...f, cronHour: e.target.value.padStart(2, '0') }))}
                        className="w-16 rounded-lg border border-stone/40 bg-basalt px-2 py-2 text-center text-sm text-light input-focus"
                      />
                      <span className="text-lg text-fog">:</span>
                      <input
                        type="number" min="0" max="59"
                        value={alarmForm.cronMinute}
                        onChange={e => setAlarmForm(f => ({ ...f, cronMinute: e.target.value.padStart(2, '0') }))}
                        className="w-16 rounded-lg border border-stone/40 bg-basalt px-2 py-2 text-center text-sm text-light input-focus"
                      />
                    </div>
                  ) : (
                    <input
                      value={alarmForm.cronCustom}
                      onChange={e => setAlarmForm(f => ({ ...f, cronCustom: e.target.value }))}
                      placeholder="0 7 * * 1-5"
                      className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light placeholder:text-stone input-focus font-mono"
                    />
                  )}
                </div>
              )}

              {/* One-time schedule */}
              {alarmForm.scheduleType === 'once' && (
                <input
                  type="datetime-local"
                  value={alarmForm.nextFireAt}
                  onChange={e => setAlarmForm(f => ({ ...f, nextFireAt: e.target.value }))}
                  className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light input-focus"
                />
              )}

              {/* Advanced options */}
              <details className="group">
                <summary className="cursor-pointer text-xs font-medium uppercase tracking-wider text-dust hover:text-fog">
                  Advanced options
                </summary>
                <div className="mt-3 grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-xs text-dust mb-1">Repeat interval (sec)</label>
                    <input
                      type="number" min="10" max="300"
                      value={alarmForm.playbackIntervalSec}
                      onChange={e => setAlarmForm(f => ({ ...f, playbackIntervalSec: Number(e.target.value) }))}
                      className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2 text-sm text-light input-focus"
                    />
                  </div>
                  <div>
                    <label className="block text-xs text-dust mb-1">Auto-dismiss (min)</label>
                    <input
                      type="number" min="1" max="60"
                      value={alarmForm.autoDismissMin}
                      onChange={e => setAlarmForm(f => ({ ...f, autoDismissMin: Number(e.target.value) }))}
                      className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2 text-sm text-light input-focus"
                    />
                  </div>
                </div>
              </details>
            </div>

            {/* Form actions */}
            <div className="mt-6 flex justify-end gap-3">
              <button
                onClick={() => setShowAlarmForm(false)}
                className="rounded-lg px-4 py-2.5 text-sm text-fog hover:text-cloud hover:bg-stone/40"
              >
                Cancel
              </button>
              <button
                onClick={saveAlarm}
                className="rounded-lg bg-amber/10 px-5 py-2.5 text-sm font-medium text-amber hover:bg-amber/20"
              >
                {editingAlarm ? 'Save Changes' : 'Create Alarm'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Sound Form Modal */}
      {showSoundForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl">
            <div className="flex items-center justify-between mb-5">
              <h3 className="text-base font-semibold text-light">Add Alarm Sound</h3>
              <button onClick={() => setShowSoundForm(false)} className="rounded-md p-1.5 text-fog hover:text-cloud hover:bg-stone/40">
                <X className="h-4.5 w-4.5" />
              </button>
            </div>

            <div className="space-y-4">
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-dust mb-1.5">Name</label>
                <input
                  value={soundForm.name}
                  onChange={e => setSoundForm(f => ({ ...f, name: e.target.value }))}
                  placeholder="Gentle Chime"
                  className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light placeholder:text-stone input-focus"
                />
              </div>
              <div>
                <label className="block text-xs font-medium uppercase tracking-wider text-dust mb-1.5">Media Source URI</label>
                <input
                  value={soundForm.mediaSourceUri}
                  onChange={e => setSoundForm(f => ({ ...f, mediaSourceUri: e.target.value }))}
                  placeholder="media-source://media_source/local/alarm-gentle.mp3"
                  className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2.5 text-sm text-light placeholder:text-stone input-focus font-mono text-xs"
                />
                <p className="mt-1 text-xs text-dust">Home Assistant media-source:// URI</p>
              </div>
              <label className="flex items-center gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={soundForm.isDefault}
                  onChange={e => setSoundForm(f => ({ ...f, isDefault: e.target.checked }))}
                  className="h-4 w-4 rounded border-stone/40 bg-basalt accent-amber"
                />
                <span className="text-sm text-fog">Set as default sound</span>
              </label>
            </div>

            <div className="mt-6 flex justify-end gap-3">
              <button
                onClick={() => setShowSoundForm(false)}
                className="rounded-lg px-4 py-2.5 text-sm text-fog hover:text-cloud hover:bg-stone/40"
              >
                Cancel
              </button>
              <button
                onClick={saveSound}
                className="rounded-lg bg-amber/10 px-5 py-2.5 text-sm font-medium text-amber hover:bg-amber/20"
              >
                Add Sound
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Confirm delete */}
      <ConfirmDialog
        open={!!confirmDelete}
        title={`Delete ${confirmDelete?.type === 'alarm' ? 'Alarm' : 'Sound'}`}
        message={`Are you sure you want to delete "${confirmDelete?.name}"? This action cannot be undone.`}
        onConfirm={confirmDeleteHandler}
        onCancel={() => setConfirmDelete(null)}
      />

      <ToastContainer toasts={toasts} onDismiss={id => setToasts(prev => prev.filter(t => t.id !== id))} />
    </div>
  )
}
