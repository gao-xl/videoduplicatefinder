import { useEffect, useRef, useState } from 'react'
import type { ScanProgressResponse } from '../api/scan'

interface UseSSEReturn {
  connected: boolean
  state: string | null
  progress: ScanProgressResponse | null
  fileOpProgress: { current: number; max: number; verb: string } | null
}

export function useSSE(): UseSSEReturn {
  const [connected, setConnected] = useState(false)
  const [state, setState] = useState<string | null>(null)
  const [progress, setProgress] = useState<ScanProgressResponse | null>(null)
  const [fileOpProgress, setFileOpProgress] = useState<{ current: number; max: number; verb: string } | null>(null)
  const esRef = useRef<EventSource | null>(null)

  useEffect(() => {
    const token = localStorage.getItem('vdf-access-token')
    const url = `/api/scan/events${token ? `?access_token=${encodeURIComponent(token)}` : ''}`
    const es = new EventSource(url)
    esRef.current = es

    es.onopen = () => setConnected(true)
    es.onerror = () => setConnected(false)

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

    return () => {
      es.close()
      esRef.current = null
    }
  }, [])

  return { connected, state, progress, fileOpProgress }
}
