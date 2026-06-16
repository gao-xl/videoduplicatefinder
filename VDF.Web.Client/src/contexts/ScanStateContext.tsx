import { createContext, useContext, useEffect, useRef, useState, useCallback, type ReactNode } from 'react'
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import { getToken } from '../api/client'
import type { ScanProgressResponse } from '../api/scan'

interface ScanState {
  connected: boolean
  transport: 'signalr' | 'sse' | 'none'
  state: string | null
  progress: ScanProgressResponse | null
  fileOpProgress: { current: number; max: number; verb: string } | null
}

const ScanStateContext = createContext<ScanState>({
  connected: false,
  transport: 'none',
  state: null,
  progress: null,
  fileOpProgress: null,
})

export function useScanState() {
  return useContext(ScanStateContext)
}

function createEventSource(): EventSource {
  const token = getToken()
  const url = `/api/scan/events${token ? `?access_token=${encodeURIComponent(token)}` : ''}`
  return new EventSource(url)
}

const SSE_RECONNECT_DELAY_MS = 3000

export function ScanStateProvider({ children }: { children: ReactNode }) {
  const [connected, setConnected] = useState(false)
  const [transport, setTransport] = useState<'signalr' | 'sse' | 'none'>('none')
  const [state, setState] = useState<string | null>(null)
  const [progress, setProgress] = useState<ScanProgressResponse | null>(null)
  const [fileOpProgress, setFileOpProgress] = useState<{ current: number; max: number; verb: string } | null>(null)

  // SignalR connection
  const signalrRef = useRef<HubConnection | null>(null)
  // SSE connection
  const esRef = useRef<EventSource | null>(null)
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const connectRef = useRef<() => void>(() => {})

  // Try SignalR first, fallback to SSE
  const connect = useCallback(() => {
    // Close existing connections
    if (signalrRef.current) {
      signalrRef.current.stop()
      signalrRef.current = null
    }
    if (esRef.current) {
      esRef.current.close()
      esRef.current = null
    }
    if (reconnectTimerRef.current) {
      clearTimeout(reconnectTimerRef.current)
      reconnectTimerRef.current = null
    }

    // Try SignalR
    const connection = new HubConnectionBuilder()
      .withUrl('/scanhub', {
        accessTokenFactory: () => getToken() ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('StateChanged', (newState: string) => {
      setState(newState)
    })

    connection.on('ProgressUpdate', (payload: ScanProgressResponse) => {
      setProgress(payload)
      setState(payload.state)
    })

    connection.on('FileOpProgress', (current: number, max: number, verb: string) => {
      setFileOpProgress({ current, max, verb })
    })

    connection.onreconnecting(() => setConnected(false))
    connection.onreconnected(() => {
      setConnected(true)
      setTransport('signalr')
    })
    connection.onclose(() => {
      setConnected(false)
      setTransport('none')
      // Fallback to SSE after SignalR fails
      setTimeout(() => connectSSE(), 1000)
    })

    connection.start()
      .then(() => {
        setConnected(true)
        setTransport('signalr')
      })
      .catch(() => {
        // SignalR failed, fallback to SSE
        connectSSE()
      })

    signalrRef.current = connection

    function connectSSE() {
      const es = createEventSource()
      esRef.current = es

      es.onopen = () => {
        setConnected(true)
        setTransport('sse')
      }

      es.onerror = () => {
        setConnected(false)
        setTransport('none')
        es.close()
        // Reconnect after delay
        reconnectTimerRef.current = setTimeout(() => {
          connectRef.current()
        }, SSE_RECONNECT_DELAY_MS)
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
    }
  }, [])

  useEffect(() => {
    connectRef.current = connect
    connect()

    return () => {
      if (signalrRef.current) {
        signalrRef.current.stop()
        signalrRef.current = null
      }
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

  return (
    <ScanStateContext.Provider value={{ connected, transport, state, progress, fileOpProgress }}>
      {children}
    </ScanStateContext.Provider>
  )
}
