import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { startScan, stopScan, pauseScan, resumeScan } from '../../api/scan'
import { useScanState } from '../../contexts/ScanStateContext'

interface ToolBarProps {
  showFilterBar: boolean
  onToggleFilter: () => void
}

export function ToolBar({ showFilterBar: _showFilterBar, onToggleFilter: _onToggleFilter }: ToolBarProps) {
  const location = useLocation()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { connected, transport, state: scanState } = useScanState()
  const [error, setError] = useState<string | null>(null)

  const isScanning = scanState === 'Scanning' || scanState === 'Comparing'
  const isPaused = scanState === 'Paused'

  const startScanMutation = useMutation({
    mutationFn: startScan,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scan-progress'] }),
  })

  const handlePause = async () => {
    try {
      setError(null)
      await pauseScan()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to pause scan')
      setTimeout(() => setError(null), 3000)
    }
  }

  const handleStop = async () => {
    try {
      setError(null)
      await stopScan()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to stop scan')
      setTimeout(() => setError(null), 3000)
    }
  }

  const handleResume = async () => {
    try {
      setError(null)
      await resumeScan()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to resume scan')
      setTimeout(() => setError(null), 3000)
    }
  }

  const currentPage = location.pathname === '/results' ? 'results'
    : location.pathname === '/settings' ? 'settings'
    : 'scanner'

  return (
    <div style={{
      height: 'var(--toolbar-height)',
      background: 'var(--bg-toolbar)',
      borderBottom: '1px solid var(--border-subtle)',
      display: 'flex',
      alignItems: 'center',
      padding: '0 0.5rem',
      gap: '0.25rem',
      flexShrink: 0,
      overflowX: 'auto',
    }}>
      {/* Scanner toolbar */}
      {currentPage === 'scanner' && (
        <>
          {!isScanning && !isPaused && (
            <button
              className="toolbar-btn primary"
              onClick={() => startScanMutation.mutate()}
              disabled={startScanMutation.isPending}
              title="Start Scan"
            >
              <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor">
                <polygon points="5,3 19,12 5,21" />
              </svg>
              Start Scan
            </button>
          )}
          {isScanning && (
            <>
              <button className="toolbar-btn" onClick={handlePause} title="Pause">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor">
                  <rect x="6" y="4" width="4" height="16" rx="1" />
                  <rect x="14" y="4" width="4" height="16" rx="1" />
                </svg>
                Pause
              </button>
              <button className="toolbar-btn danger" onClick={handleStop} title="Stop">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor">
                  <rect x="4" y="4" width="16" height="16" rx="2" />
                </svg>
                Stop
              </button>
            </>
          )}
          {isPaused && (
            <>
              <button className="toolbar-btn primary" onClick={handleResume} title="Resume">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor">
                  <polygon points="5,3 19,12 5,21" />
                </svg>
                Resume
              </button>
              <button className="toolbar-btn danger" onClick={handleStop} title="Stop">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="currentColor">
                  <rect x="4" y="4" width="16" height="16" rx="2" />
                </svg>
                Stop
              </button>
            </>
          )}
        </>
      )}

      {/* Results toolbar */}
      {currentPage === 'results' && (
        <>
          <button className="toolbar-btn" onClick={() => navigate('/')} title="New Scan">
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="11" cy="11" r="8" />
              <path d="m21 21-4.3-4.3" />
            </svg>
            Scanner
          </button>
        </>
      )}

      {/* Settings toolbar */}
      {currentPage === 'settings' && (
        <>
          <button className="toolbar-btn" onClick={() => navigate('/')} title="Back to Scanner">
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="11" cy="11" r="8" />
              <path d="m21 21-4.3-4.3" />
            </svg>
            Scanner
          </button>
        </>
      )}

      {/* Right side spacer */}
      <div style={{ flex: 1 }} />

      {/* Connection indicator */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 4,
        fontSize: 10,
        color: connected ? 'var(--accent-success)' : 'var(--text-dim)',
        fontFamily: 'var(--font-mono)',
        paddingRight: 4,
      }}>
        <div style={{
          width: 6,
          height: 6,
          borderRadius: '50%',
          background: connected ? 'var(--accent-success)' : 'var(--text-dim)',
        }} />
        {transport === 'signalr' ? 'SignalR' : transport === 'sse' ? 'SSE' : 'Offline'}
      </div>

      {/* Error notification */}
      {error && (
        <div style={{
          position: 'absolute',
          bottom: 'var(--toolbar-height)',
          right: 8,
          background: 'var(--accent-error)',
          color: '#fff',
          padding: '4px 8px',
          borderRadius: 'var(--radius-sm)',
          fontSize: 11,
          zIndex: 100,
          animation: 'fadeIn 0.15s ease',
        }}>
          {error}
        </div>
      )}
    </div>
  )
}
