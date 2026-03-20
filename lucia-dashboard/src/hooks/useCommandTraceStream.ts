import { useState, useEffect, useCallback, useRef } from 'react'
import type { CommandTrace } from '../types'

export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected'

/**
 * SSE hook for real-time command trace updates.
 * Connects to `/api/command-traces/live` and emits new traces as they arrive.
 */
export function useCommandTraceStream() {
  const [lastTrace, setLastTrace] = useState<CommandTrace | null>(null)
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected')
  const sourceRef = useRef<EventSource | null>(null)
  const connectRef = useRef<(() => void) | null>(null)
  const retriesRef = useRef(0)
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const maxRetries = 10

  const connect = useCallback(() => {
    if (retryTimerRef.current) {
      clearTimeout(retryTimerRef.current)
      retryTimerRef.current = null
    }
    if (sourceRef.current) {
      sourceRef.current.close()
    }

    const source = new EventSource('/api/command-traces/live')
    sourceRef.current = source

    source.onopen = () => {
      setConnectionState('connected')
      retriesRef.current = 0
    }

    source.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data)
        if (data.type === 'connected') {
          setConnectionState('connected')
          retriesRef.current = 0
          return
        }
        setLastTrace(data as CommandTrace)
      } catch { /* ignore */ }
    }

    source.onerror = () => {
      source.close()
      sourceRef.current = null

      if (retriesRef.current < maxRetries) {
        setConnectionState('reconnecting')
        const delay = Math.min(1000 * Math.pow(2, retriesRef.current), 30000)
        retriesRef.current++
        retryTimerRef.current = setTimeout(() => connectRef.current?.(), delay)
      } else {
        setConnectionState('disconnected')
      }
    }
  }, [])

  useEffect(() => {
    connectRef.current = connect
  }, [connect])

  const reconnect = useCallback(() => {
    retriesRef.current = 0
    connectRef.current?.()
  }, [])

  useEffect(() => {
    connect()
    return () => {
      if (retryTimerRef.current) {
        clearTimeout(retryTimerRef.current)
        retryTimerRef.current = null
      }
      sourceRef.current?.close()
      sourceRef.current = null
    }
  }, [connect])

  return { lastTrace, connectionState, reconnect }
}
