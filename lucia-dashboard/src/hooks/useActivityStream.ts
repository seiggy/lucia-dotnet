import { useState, useEffect, useCallback, useRef } from 'react'
import type { LiveEvent } from '../types'

/** Connection state of the SSE activity stream. */
export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected'

/**
 * React hook for consuming real-time orchestration events via Server-Sent Events.
 *
 * Connects to `/api/activity/live` and provides the latest {@link LiveEvent},
 * the current {@link ConnectionState}, and a manual `reconnect` function.
 *
 * Automatically reconnects on error with exponential backoff (up to 10 retries,
 * max 30 second delay). Cleans up the EventSource on unmount.
 */
export function useActivityStream() {
  const [lastEvent, setLastEvent] = useState<LiveEvent | null>(null)
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected')
  const sourceRef = useRef<EventSource | null>(null)
  const connectRef = useRef<(() => void) | null>(null)
  const retriesRef = useRef(0)
  const maxRetries = 10

  const connect = useCallback(() => {
    if (sourceRef.current) {
      sourceRef.current.close()
    }

    const source = new EventSource('/api/activity/live')
    sourceRef.current = source

    source.onopen = () => {
      setConnectionState('connected')
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
        setTimeout(() => connectRef.current?.(), delay)
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
      sourceRef.current?.close()
      sourceRef.current = null
    }
  }, [connect])

  return { lastEvent, connectionState, reconnect }
}
