import { useState, useEffect, useCallback } from 'react'
import { RefreshCw, Eye, EyeOff, Wifi, WifiOff, Users, Trash2, Shield, ShieldOff } from 'lucide-react'
import {
  fetchPresenceSensors, fetchOccupiedAreas, fetchPresenceConfig,
  updatePresenceConfig, updatePresenceSensor, deletePresenceSensor, refreshPresenceSensors
} from '../api'
import type { PresenceSensorMapping, OccupiedArea, PresenceConfidence } from '../types'

const CONFIDENCE_LABELS: Record<PresenceConfidence, { label: string; color: string }> = {
  Highest: { label: 'Highest', color: 'text-green-400' },
  High: { label: 'High', color: 'text-emerald-400' },
  Medium: { label: 'Medium', color: 'text-amber' },
  Low: { label: 'Low', color: 'text-orange-400' },
  None: { label: 'None', color: 'text-dust' },
}

const CONFIDENCE_OPTIONS: PresenceConfidence[] = ['Highest', 'High', 'Medium', 'Low', 'None']

export default function PresencePage() {
  const [sensors, setSensors] = useState<PresenceSensorMapping[]>([])
  const [occupiedAreas, setOccupiedAreas] = useState<OccupiedArea[]>([])
  const [enabled, setEnabled] = useState(true)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [toasts, setToasts] = useState<{ id: number; msg: string; type: 'success' | 'error' }[]>([])
  const [filter, setFilter] = useState('')

  let toastId = 0
  function addToast(msg: string, type: 'success' | 'error' = 'success') {
    const id = ++toastId
    setToasts(t => [...t, { id, msg, type }])
    setTimeout(() => setToasts(t => t.filter(x => x.id !== id)), 3500)
  }

  const loadData = useCallback(async () => {
    try {
      const [s, o, c] = await Promise.all([
        fetchPresenceSensors(),
        fetchOccupiedAreas(),
        fetchPresenceConfig(),
      ])
      setSensors(s)
      setOccupiedAreas(o)
      setEnabled(c.enabled)
    } catch (err) {
      addToast(`Failed to load: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadData() }, [loadData])

  async function toggleEnabled() {
    try {
      const result = await updatePresenceConfig({ enabled: !enabled })
      setEnabled(result.enabled)
      addToast(result.enabled ? 'Presence detection enabled' : 'Presence detection disabled')
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function handleRefresh() {
    setRefreshing(true)
    try {
      const updated = await refreshPresenceSensors()
      setSensors(updated)
      addToast(`Refreshed — ${updated.length} sensors found`)
    } catch (err) {
      addToast(`Refresh failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    } finally {
      setRefreshing(false)
    }
  }

  async function toggleSensorDisabled(sensor: PresenceSensorMapping) {
    try {
      const updated = await updatePresenceSensor(sensor.entityId, { isDisabled: !sensor.isDisabled })
      setSensors(s => s.map(x => x.entityId === sensor.entityId ? updated : x))
      addToast(updated.isDisabled ? `${sensor.entityId} disabled` : `${sensor.entityId} enabled`)
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function changeConfidence(sensor: PresenceSensorMapping, confidence: PresenceConfidence) {
    try {
      const updated = await updatePresenceSensor(sensor.entityId, { confidence })
      setSensors(s => s.map(x => x.entityId === sensor.entityId ? updated : x))
      addToast(`Confidence updated to ${confidence}`)
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  async function removeSensor(entityId: string) {
    if (!confirm(`Remove sensor mapping for ${entityId}?`)) return
    try {
      await deletePresenceSensor(entityId)
      setSensors(s => s.filter(x => x.entityId !== entityId))
      addToast('Sensor mapping removed')
    } catch (err) {
      addToast(`Failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
    }
  }

  // Group sensors by area
  const filteredSensors = sensors.filter(s =>
    !filter || s.entityId.toLowerCase().includes(filter.toLowerCase())
    || (s.areaName ?? s.areaId).toLowerCase().includes(filter.toLowerCase())
  )

  const sensorsByArea = filteredSensors.reduce<Record<string, PresenceSensorMapping[]>>((acc, s) => {
    const key = s.areaName ?? s.areaId
    ;(acc[key] ??= []).push(s)
    return acc
  }, {})

  const sortedAreas = Object.keys(sensorsByArea).sort()

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <RefreshCw className="h-6 w-6 animate-spin text-amber" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Toast notifications */}
      <div className="fixed top-4 right-4 z-50 space-y-2">
        {toasts.map(t => (
          <div key={t.id} className={`rounded-lg px-4 py-2.5 text-sm shadow-lg backdrop-blur-sm ${
            t.type === 'error' ? 'bg-rose/20 text-rose border border-rose/30' : 'bg-sage/20 text-sage border border-sage/30'
          }`}>{t.msg}</div>
        ))}
      </div>

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-light">Presence Detection</h1>
          <p className="text-sm text-dust mt-1">
            {sensors.length} sensor{sensors.length !== 1 ? 's' : ''} mapped · {occupiedAreas.length} area{occupiedAreas.length !== 1 ? 's' : ''} occupied
          </p>
        </div>
        <div className="flex items-center gap-3">
          <button
            onClick={toggleEnabled}
            className={`flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition ${
              enabled
                ? 'bg-sage/10 text-sage hover:bg-sage/20'
                : 'bg-stone/40 text-dust hover:bg-stone/60'
            }`}
          >
            {enabled ? <Shield className="h-4 w-4" /> : <ShieldOff className="h-4 w-4" />}
            {enabled ? 'Enabled' : 'Disabled'}
          </button>
          <button
            onClick={handleRefresh}
            disabled={refreshing}
            className="flex items-center gap-2 rounded-lg bg-amber/10 px-4 py-2.5 text-sm font-medium text-amber hover:bg-amber/20 disabled:opacity-50"
          >
            <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} />
            Re-scan
          </button>
        </div>
      </div>

      {/* Occupied areas summary */}
      {occupiedAreas.length > 0 && (
        <div className="glass-panel p-4">
          <h2 className="text-sm font-medium text-fog mb-3 flex items-center gap-2">
            <Users className="h-4 w-4 text-amber" />
            Currently Occupied
          </h2>
          <div className="flex flex-wrap gap-2">
            {occupiedAreas.map(a => (
              <div key={a.areaId} className="rounded-lg bg-sage/10 border border-sage/20 px-3 py-1.5 text-sm">
                <span className="text-sage font-medium">{a.areaName}</span>
                {a.occupantCount != null && (
                  <span className="text-dust ml-2">({a.occupantCount} occupant{a.occupantCount !== 1 ? 's' : ''})</span>
                )}
                <span className={`ml-2 text-xs ${CONFIDENCE_LABELS[a.confidence].color}`}>
                  {CONFIDENCE_LABELS[a.confidence].label}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Search filter */}
      <div>
        <input
          type="text"
          placeholder="Filter by entity ID or area..."
          value={filter}
          onChange={e => setFilter(e.target.value)}
          className="w-full max-w-md rounded-lg border border-stone/40 bg-basalt px-4 py-2.5 text-sm text-light placeholder-dust input-focus"
        />
      </div>

      {/* Sensors grouped by area */}
      {sortedAreas.length === 0 ? (
        <div className="glass-panel p-8 text-center">
          <WifiOff className="h-10 w-10 text-dust mx-auto mb-3" />
          <p className="text-fog">No presence sensors found.</p>
          <p className="text-sm text-dust mt-1">Click "Re-scan" to discover sensors from Home Assistant.</p>
        </div>
      ) : (
        <div className="space-y-4">
          {sortedAreas.map(area => (
            <div key={area} className="glass-panel overflow-hidden">
              <div className="px-4 py-3 border-b border-stone/30 flex items-center justify-between">
                <h3 className="text-sm font-medium text-fog flex items-center gap-2">
                  <Wifi className="h-4 w-4 text-amber" />
                  {area}
                </h3>
                <span className="text-xs text-dust">
                  {sensorsByArea[area].length} sensor{sensorsByArea[area].length !== 1 ? 's' : ''}
                </span>
              </div>
              <div className="divide-y divide-stone/20">
                {sensorsByArea[area].map(sensor => (
                  <div key={sensor.entityId} className={`px-4 py-3 flex items-center gap-4 ${sensor.isDisabled ? 'opacity-50' : ''}`}>
                    {/* Entity ID */}
                    <div className="flex-1 min-w-0">
                      <p className="text-sm text-light truncate font-mono">{sensor.entityId}</p>
                      <div className="flex items-center gap-2 mt-0.5">
                        {sensor.isUserOverride && (
                          <span className="text-xs bg-amber/10 text-amber px-1.5 py-0.5 rounded">override</span>
                        )}
                      </div>
                    </div>

                    {/* Confidence selector */}
                    <select
                      value={sensor.confidence}
                      onChange={e => changeConfidence(sensor, e.target.value as PresenceConfidence)}
                      className="rounded-lg border border-stone/40 bg-basalt px-2 py-1.5 text-xs text-light input-focus"
                    >
                      {CONFIDENCE_OPTIONS.map(c => (
                        <option key={c} value={c}>{c}</option>
                      ))}
                    </select>

                    {/* Toggle enabled/disabled */}
                    <button
                      onClick={() => toggleSensorDisabled(sensor)}
                      title={sensor.isDisabled ? 'Enable sensor' : 'Disable sensor'}
                      className={`p-1.5 rounded-lg transition ${
                        sensor.isDisabled
                          ? 'text-dust hover:text-light hover:bg-stone/40'
                          : 'text-sage hover:text-sage hover:bg-sage/10'
                      }`}
                    >
                      {sensor.isDisabled ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>

                    {/* Delete (only user overrides) */}
                    {sensor.isUserOverride && (
                      <button
                        onClick={() => removeSensor(sensor.entityId)}
                        title="Remove override"
                        className="p-1.5 rounded-lg text-dust hover:text-rose hover:bg-rose/10 transition"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    )}
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
