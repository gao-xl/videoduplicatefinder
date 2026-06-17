import { useState, useEffect, useCallback } from 'react'

interface QueuedAction {
  id: string
  type: string
  payload: unknown
  timestamp: number
}

const STORAGE_KEY = 'vdf-offline-queue'

export function useOfflineQueue() {
  const [isOnline, setIsOnline] = useState(navigator.onLine)
  const [queue, setQueue] = useState<QueuedAction[]>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY)
      return stored ? JSON.parse(stored) : []
    } catch {
      return []
    }
  })

  // Listen for online/offline events
  useEffect(() => {
    const handleOnline = () => setIsOnline(true)
    const handleOffline = () => setIsOnline(false)

    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)

    return () => {
      window.removeEventListener('online', handleOnline)
      window.removeEventListener('offline', handleOffline)
    }
  }, [])

  // Persist queue to localStorage
  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(queue))
  }, [queue])

  // Process queue when coming back online
  useEffect(() => {
    if (isOnline && queue.length > 0) {
      processQueue()
    }
  }, [isOnline])

  const addToQueue = useCallback((type: string, payload: unknown) => {
    const action: QueuedAction = {
      id: crypto.randomUUID(),
      type,
      payload,
      timestamp: Date.now(),
    }
    setQueue((prev) => [...prev, action])
  }, [])

  const processQueue = useCallback(async () => {
    const itemsToProcess = [...queue]
    setQueue([])

    for (const item of itemsToProcess) {
      try {
        // Process the action based on type
        console.log('Processing queued action:', item.type, item.payload)
        // In a real implementation, this would call the appropriate API
      } catch (error) {
        console.error('Failed to process queued action:', error)
        // Re-queue failed items
        setQueue((prev) => [...prev, item])
      }
    }
  }, [queue])

  const clearQueue = useCallback(() => {
    setQueue([])
    localStorage.removeItem(STORAGE_KEY)
  }, [])

  return {
    isOnline,
    queueLength: queue.length,
    addToQueue,
    clearQueue,
  }
}
