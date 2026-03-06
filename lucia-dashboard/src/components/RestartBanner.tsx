import { useEffect, useState } from 'react'
import { AlertTriangle, RotateCcw } from 'lucide-react'
import { fetchRestartRequired, triggerRestart } from '../api'

export default function RestartBanner() {
  const [needed, setNeeded] = useState(false)
  const [restarting, setRestarting] = useState(false)

  useEffect(() => {
    const poll = async () => {
      try {
        const { restartRequired } = await fetchRestartRequired()
        setNeeded(restartRequired)
      } catch {
        /* ignore */
      }
    }
    poll()
    const timer = setInterval(poll, 5000)
    return () => clearInterval(timer)
  }, [])

  if (!needed) return null

  const handleRestart = async () => {
    setRestarting(true)
    try {
      await triggerRestart()
    } catch {
      setRestarting(false)
    }
  }

  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border border-amber/30 bg-amber/10 px-4 py-3">
      <div className="flex items-center gap-2 text-sm text-amber">
        <AlertTriangle className="h-4 w-4 shrink-0" />
        <span>Plugin changes detected — a restart is required for them to take effect.</span>
      </div>
      <button
        onClick={handleRestart}
        disabled={restarting}
        className="flex shrink-0 items-center gap-1.5 rounded-lg bg-amber/20 px-3 py-1.5 text-sm font-medium text-amber hover:bg-amber/30 disabled:opacity-50"
      >
        <RotateCcw className={`h-3.5 w-3.5 ${restarting ? 'animate-spin' : ''}`} />
        {restarting ? 'Restarting…' : 'Restart Now'}
      </button>
    </div>
  )
}
