import { useEffect, useRef, useState, useCallback } from 'react'
import type { ScanProgressResponse } from '../api/scan'
import { getToken } from '../api/client'

interface UseSSEReturn {
  connected: boolean
  state: string | null
  progress: ScanProgressResponse | null
  fileOpProgress: { current: number; max: number; verb: string } | null
}

function createEventSource(): EventSource {
  const token = getToken()
  const url = `/api/scan/events${token ? `?access_token=${encodeURIComponent(token)}` : ''}`
  return new EventSource(url)
}

const RECONNECT_DELAY_MS = 3000

export function useSSE(): UseSSEReturn {
  const [connected, setConnected] = useState(false)
  const [state, setState] = useState<string | null>(null)
  const [progress, setProgress] = useState<ScanProgressResponse | null>(null)
  const [fileOpProgress, setFileOpProgress] = useState<{ current: number; max: number; verb: string } | null>(null)
  const esRef = useRef<EventSource | null>(null)
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const connectRef = useRef<() => void>(() => {})

  const connect = useCallback(() => {
    // Close existing connection
    if (esRef.current) {
      esRef.current.close()
    }
    // Clear any pending reconnect timer
    if (reconnectTimerRef.current) {
      clearTimeout(reconnectTimerRef.current)
      reconnectTimerRef.current = null
    }

    // createEventSource() reads the latest token from localStorage via getToken()
    const es = createEventSource()
    esRef.current = es

    es.onopen = () => setConnected(true)
    es.onerror = () => {
      setConnected(false)
      es.close()
      // Reconnect with a fresh token after a short delay
      reconnectTimerRef.current = setTimeout(() => {
        connectRef.current()
      }, RECONNECT_DELAY_MS)
    }

    es.addEventListener('state', (e: MessageEvent) => {
      try {
        const data = JSON.parse(e.data)
        setState(data.state ?? data)
      } catch {
        setState(e.data)
      }
    })

    es.addEventListener('progress', (e: MessageEvent) => {
      try {
        const data: ScanProgressResponse = JSON.parse(e.data)
        setProgress(data)
        setState(data.state)
      } catch { /* ignore parse errors */ }
    })

    es.addEventListener('fileop', (e: MessageEvent) => {
      try {
        const data = JSON.parse(e.data)
        setFileOpProgress({ current: data.current, max: data.max, verb: data.verb })
      } catch { /* ignore */ }
    })
  }, [])

  useEffect(() => {
    // Keep the ref in sync so the reconnect timer always calls the latest connect
    connectRef.current = connect
    connect()

    return () => {
      if (reconnectTimerRef.current) {
        clearTimeout(reconnectTimerRef.current)
        reconnectTimerRef.current = null
      }
      if (esRef.current) {
        esRef.current.close()
        esRef.current = null
      }
    }
  }, [connect])

  return { connected, state, progress, fileOpProgress }
}
