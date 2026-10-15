import { useState, useEffect, useCallback, useRef } from 'react'
import type { CommandTrace } from '../types'
import { usePageVisible } from './usePageVisible'

export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected'

const MIN_COUNT_INTERVAL_MS = 5_000

/**
 * SSE hook for real-time command trace updates.
 * Connects to `/api/command-traces/live` and emits new traces as they arrive.
 * Disconnects while the browser tab is hidden so background tabs do not hold a connection slot.
 * connectionCount counts successful (re)connections.
 */
export function useCommandTraceStream() {
  const visible = usePageVisible()
  const [lastTrace, setLastTrace] = useState<CommandTrace | null>(null)
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected')
  const [connectionCount, setConnectionCount] = useState(0)
  const lastCountedAtRef = useRef(0)
  const countTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const sourceRef = useRef<EventSource | null>(null)
  const connectRef = useRef<(() => void) | null>(null)
  const retriesRef = useRef(0)
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const maxRetries = 10

  // A stream that keeps dropping and reopening would make every consumer refetch each time, so count at most once per interval.
  // A reconnection inside the interval still counts, once the interval has passed, so missed events are never skipped.
  const countConnection = useCallback(() => {
    const wait = lastCountedAtRef.current + MIN_COUNT_INTERVAL_MS - Date.now()
    if (wait <= 0) {
      lastCountedAtRef.current = Date.now()
      setConnectionCount(count => count + 1)
      return
    }
    if (countTimerRef.current) return
    countTimerRef.current = setTimeout(() => {
      countTimerRef.current = null
      lastCountedAtRef.current = Date.now()
      setConnectionCount(count => count + 1)
    }, wait)
  }, [])

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
      countConnection()
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
  }, [countConnection])

  useEffect(() => {
    connectRef.current = connect
  }, [connect])

  const reconnect = useCallback(() => {
    retriesRef.current = 0
    connectRef.current?.()
  }, [])

  useEffect(() => {
    if (!visible) return
    retriesRef.current = 0
    connect()
    return () => {
      if (countTimerRef.current) {
        clearTimeout(countTimerRef.current)
        countTimerRef.current = null
      }
      if (retryTimerRef.current) {
        clearTimeout(retryTimerRef.current)
        retryTimerRef.current = null
      }
      sourceRef.current?.close()
      sourceRef.current = null
    }
  }, [connect, visible])

  // The server does not replay events missed while disconnected, so consumers refetch when this grows past 1.
  return { lastTrace, connectionState, connectionCount, reconnect }
}
