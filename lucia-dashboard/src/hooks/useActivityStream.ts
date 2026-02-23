import { useState, useEffect, useCallback, useRef } from 'react'
import type { LiveEvent } from '../types'

export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected'

export function useActivityStream() {
  const [lastEvent, setLastEvent] = useState<LiveEvent | null>(null)
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected')
  const sourceRef = useRef<EventSource | null>(null)
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
        setTimeout(connect, delay)
      } else {
        setConnectionState('disconnected')
      }
    }
  }, [])

  const reconnect = useCallback(() => {
    retriesRef.current = 0
    connect()
  }, [connect])

  useEffect(() => {
    connect()
    return () => {
      sourceRef.current?.close()
      sourceRef.current = null
    }
  }, [connect])

  return { lastEvent, connectionState, reconnect }
}
