import { useState, useEffect, useCallback, useRef } from 'react'
import type { LiveEvent } from '../types'
import { usePageVisible } from './usePageVisible'

const MIN_COUNT_INTERVAL_MS = 5_000

/** Connection state of the SSE activity stream. */
export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected'

/**
 * React hook for consuming real-time orchestration events via Server-Sent Events.
 *
 * Connects to `/api/activity/live` and provides the latest {@link LiveEvent},
 * the current {@link ConnectionState}, and a manual `reconnect` function.
 *
 * Automatically reconnects on error with exponential backoff (up to 10 retries,
 * max 30 second delay). Cleans up the EventSource on unmount and while the
 * browser tab is hidden, so background tabs do not hold a connection slot.
 * The server does not replay events missed while disconnected, so connectionCount, which counts
 * successful (re)connections, tells consumers when to drop state built from earlier events.
 */
export function useActivityStream() {
  const visible = usePageVisible()
  const [lastEvent, setLastEvent] = useState<LiveEvent | null>(null)
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

    const source = new EventSource('/api/activity/live')
    sourceRef.current = source

    source.onopen = () => {
      setConnectionState('connected')
      countConnection()
      retriesRef.current = 0
    }

    source.onmessage = (e) => {
      try {
        const evt: LiveEvent = JSON.parse(e.data)
        if (evt.type === 'connected') {
          // ACK from server — connection is confirmed live
          setConnectionState('connected')
          retriesRef.current = 0
          return
        }
        setLastEvent(evt)
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

  return { lastEvent, connectionState, connectionCount, reconnect }
}
