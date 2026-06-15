import { useSignalR } from '../../hooks/useSignalR'
import { useSSE } from '../../hooks/useSSE'

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)}s`
  const m = Math.floor(seconds / 60)
  const s = Math.round(seconds % 60)
  if (m < 60) return `${m}m ${s}s`
  const h = Math.floor(m / 60)
  return `${h}h ${m % 60}m`
}

export function StatusBar() {
  const signalR = useSignalR()
  const sse = useSSE()
  const realtime = signalR.connected ? signalR : sse
  const scanState = realtime.state
  const scanProgress = realtime.progress

  const isScanning = scanState === 'Scanning' || scanState === 'Comparing'
  const isPaused = scanState === 'Paused'
  const isDone = scanState === 'Done'

  return (
    <div style={{
      height: 'var(--statusbar-height)',
      background: 'var(--bg-toolbar)',
      borderTop: '1px solid var(--border-subtle)',
      display: 'flex',
      alignItems: 'center',
      padding: '0 0.75rem',
      gap: '1rem',
      fontSize: 11,
      color: 'var(--text-muted)',
      fontFamily: 'var(--font-sans)',
      flexShrink: 0,
      userSelect: 'none',
    }}>
      {/* Scan state */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 4,
      }}>
        {(isScanning || isPaused) && (
          <div style={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            background: isPaused ? 'var(--accent-warning)' : 'var(--accent-primary)',
            animation: isPaused ? 'none' : 'pulse 1.5s infinite',
          }} />
        )}
        {isDone && (
          <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="var(--accent-success)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="20 6 9 17 4 12" />
          </svg>
        )}
        <span>{scanState || 'Ready'}</span>
      </div>

      {/* Divider */}
      {scanProgress && (isScanning || isPaused) && (
        <div style={{
          width: 1,
          height: 14,
          background: 'var(--border-divider)',
        }} />
      )}

      {/* Progress info */}
      {scanProgress && (isScanning || isPaused) && (
        <>
          <span style={{ fontFamily: 'var(--font-mono)' }}>
            {scanProgress.current}/{scanProgress.max}
          </span>
          {scanProgress.elapsedSeconds > 0 && (
            <span>
              Elapsed: {formatDuration(scanProgress.elapsedSeconds)}
            </span>
          )}
          {scanProgress.remainingSeconds > 0 && (
            <span>
              Remaining: {formatDuration(scanProgress.remainingSeconds)}
            </span>
          )}
          {scanProgress.currentFile && (
            <span style={{
              flex: 1,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              fontFamily: 'var(--font-mono)',
              fontSize: 10,
            }}>
              {scanProgress.currentFile}
            </span>
          )}
        </>
      )}

      {/* Spacer */}
      <div style={{ flex: 1 }} />

      {/* Version / info */}
      <span style={{ color: 'var(--text-dim)', fontSize: 10 }}>
        VDF Web UI
      </span>
    </div>
  )
}
