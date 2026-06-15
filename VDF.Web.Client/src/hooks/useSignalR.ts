import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import { getToken } from '../api/client'
import type { ScanProgressResponse } from '../api/scan'

interface UseSignalRReturn {
  connected: boolean
  state: string | null
  progress: ScanProgressResponse | null
  fileOpProgress: { current: number; max: number; verb: string } | null
}

export function useSignalR(): UseSignalRReturn {
  const connectionRef = useRef<HubConnection | null>(null)
  const [connected, setConnected] = useState(false)
  const [state, setState] = useState<string | null>(null)
  const [progress, setProgress] = useState<ScanProgressResponse | null>(null)
  const [fileOpProgress, setFileOpProgress] = useState<{ current: number; max: number; verb: string } | null>(null)

  useEffect(() => {
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
    connection.onreconnected(() => setConnected(true))
    connection.onclose(() => setConnected(false))

    connection.start()
      .then(() => setConnected(true))
      .catch(() => setConnected(false))

    connectionRef.current = connection

    return () => {
      connection.stop()
      connectionRef.current = null
    }
  }, [])

  return { connected, state, progress, fileOpProgress }
}
