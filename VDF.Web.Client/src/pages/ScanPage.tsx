import { useState, useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { getSettings, updateSettings } from '../api/settings'
import { startScan, stopScan, pauseScan, resumeScan, resetScan } from '../api/scan'
import { useSignalR } from '../hooks/useSignalR'
import { useSSE } from '../hooks/useSSE'
import { ProgressBar } from '../components/shared/ProgressBar'
import { Spinner } from '../components/shared/Spinner'
import { ConfirmDialog } from '../components/shared/ConfirmDialog'
import { PathBrowser } from '../components/shared/PathBrowser'

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)}s`
  const m = Math.floor(seconds / 60)
  const s = Math.round(seconds % 60)
  if (m < 60) return `${m}m ${s}s`
  const h = Math.floor(m / 60)
  return `${h}h ${m % 60}m`
}

export function ScanPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const signalR = useSignalR()
  const sse = useSSE()

  // Use SignalR if connected, fallback to SSE
  const realtime = signalR.connected ? signalR : sse
  const scanState = realtime.state
  const scanProgress = realtime.progress

  const [showResetConfirm, setShowResetConfirm] = useState(false)
  const [showPathBrowser, setShowPathBrowser] = useState(false)
  const [pathBrowserTarget, setPathBrowserTarget] = useState<'include' | 'exclude'>('include')
  const [newPath, setNewPath] = useState('')

  const { data: settings } = useQuery({
    queryKey: ['settings'],
    queryFn: getSettings,
  })

  const updateSettingsMutation = useMutation({
    mutationFn: (s: Partial<Parameters<typeof updateSettings>[0]>) => updateSettings(s),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })

  const startScanMutation = useMutation({
    mutationFn: startScan,
  })

  const [localIncludePaths, setLocalIncludePaths] = useState<string[]>([])
  const [localExcludePaths, setLocalExcludePaths] = useState<string[]>([])

  useEffect(() => {
    if (settings) {
      setLocalIncludePaths(settings.includeList)
      setLocalExcludePaths(settings.blackList)
    }
  }, [settings])

  const handleAddIncludePath = useCallback(() => {
    if (!newPath.trim()) return
    setLocalIncludePaths(prev => [...prev, newPath.trim()])
    updateSettingsMutation.mutate({ includeList: [...localIncludePaths, newPath.trim()], blackList: localExcludePaths })
    setNewPath('')
  }, [newPath, localIncludePaths, localExcludePaths, updateSettingsMutation])

  const handleAddExcludePath = useCallback(() => {
    if (!newPath.trim()) return
    setLocalExcludePaths(prev => [...prev, newPath.trim()])
    updateSettingsMutation.mutate({ includeList: localIncludePaths, blackList: [...localExcludePaths, newPath.trim()] })
    setNewPath('')
  }, [newPath, localIncludePaths, localExcludePaths, updateSettingsMutation])

  const handleRemoveIncludePath = useCallback((path: string) => {
    const updated = localIncludePaths.filter(p => p !== path)
    setLocalIncludePaths(updated)
    updateSettingsMutation.mutate({ includeList: updated, blackList: localExcludePaths })
  }, [localIncludePaths, localExcludePaths, updateSettingsMutation])

  const handleRemoveExcludePath = useCallback((path: string) => {
    const updated = localExcludePaths.filter(p => p !== path)
    setLocalExcludePaths(updated)
    updateSettingsMutation.mutate({ includeList: localIncludePaths, blackList: updated })
  }, [localIncludePaths, localExcludePaths, updateSettingsMutation])

  const handleStartScan = useCallback(() => {
    updateSettingsMutation.mutate(
      { includeList: localIncludePaths, blackList: localExcludePaths },
      { onSuccess: () => startScanMutation.mutate() },
    )
  }, [localIncludePaths, localExcludePaths, updateSettingsMutation, startScanMutation])

  const isScanning = scanState === 'Scanning' || scanState === 'Comparing'
  const isPaused = scanState === 'Paused'
  const isDone = scanState === 'Done'
  const isError = scanState === 'Error'
  const isAborted = scanState === 'Aborted'

  return (
    <div style={{ maxWidth: 720, animation: 'fadeInUp 0.4s ease both' }}>
      {/* Header */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: '1.75rem',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          <div style={{
            width: 36,
            height: 36,
            borderRadius: 'var(--radius-md)',
            background: 'var(--accent-primary-glow)',
            border: '1px solid rgba(14, 165, 233, 0.2)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="11" cy="11" r="8" />
              <path d="m21 21-4.3-4.3" />
            </svg>
          </div>
          <h1 style={{
            fontFamily: 'var(--font-display)',
            fontSize: '1.5rem',
            fontWeight: 700,
            margin: 0,
            color: 'var(--text-primary)',
            letterSpacing: '-0.02em',
          }}>
            Scan
          </h1>
        </div>
        {!isScanning && !isPaused && (
          <button
            onClick={handleStartScan}
            disabled={localIncludePaths.length === 0 || startScanMutation.isPending}
            style={{
              padding: '0.6rem 1.5rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: localIncludePaths.length === 0
                ? 'var(--bg-button)'
                : 'linear-gradient(135deg, #0ea5e9, #0284c7)',
              color: localIncludePaths.length === 0 ? 'var(--text-dim)' : '#fff',
              cursor: localIncludePaths.length === 0 ? 'not-allowed' : 'pointer',
              fontSize: '0.85rem',
              fontWeight: 600,
              fontFamily: 'var(--font-sans)',
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              transition: 'all var(--transition-base)',
              boxShadow: localIncludePaths.length === 0
                ? 'none'
                : '0 0 20px rgba(14, 165, 233, 0.25), 0 2px 8px rgba(0, 0, 0, 0.3)',
              letterSpacing: '0.02em',
            }}
          >
            {startScanMutation.isPending && <Spinner size={12} />}
            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" style={{ opacity: 0.9 }}>
              <polygon points="5,3 19,12 5,21" />
            </svg>
            Start Scan
          </button>
        )}
      </div>

      {/* Scan Progress */}
      {(isScanning || isPaused) && scanProgress && (
        <div style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--border-default)',
          borderRadius: 'var(--radius-lg)',
          padding: '1.5rem',
          marginBottom: '1.5rem',
          position: 'relative',
          overflow: 'hidden',
          boxShadow: isScanning ? 'var(--shadow-glow)' : 'var(--shadow-sm)',
          animation: 'cardIn 0.3s ease both',
        }}>
          {/* Glowing top border */}
          <div style={{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            height: 2,
            background: isPaused
              ? 'var(--accent-warning)'
              : 'linear-gradient(90deg, #0ea5e9, #38bdf8, #6366f1, #38bdf8, #0ea5e9)',
            backgroundSize: isPaused ? '100%' : '200% 100%',
            animation: isPaused ? 'none' : 'shimmer 3s linear infinite',
            boxShadow: isPaused
              ? '0 0 8px rgba(245, 158, 11, 0.3)'
              : '0 0 12px rgba(14, 165, 233, 0.4)',
          }} />

          {/* Stage header row */}
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            marginBottom: '1rem',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
              {/* Stage step indicator */}
              <div style={{
                width: 32,
                height: 32,
                borderRadius: 'var(--radius-md)',
                background: isPaused
                  ? 'rgba(245, 158, 11, 0.1)'
                  : 'var(--accent-primary-glow)',
                border: isPaused
                  ? '1px solid rgba(245, 158, 11, 0.25)'
                  : '1px solid rgba(14, 165, 233, 0.25)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}>
                {isPaused ? (
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="var(--accent-warning)">
                    <rect x="6" y="4" width="4" height="16" rx="1" />
                    <rect x="14" y="4" width="4" height="16" rx="1" />
                  </svg>
                ) : (
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2.5" strokeLinecap="round">
                    <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83" />
                  </svg>
                )}
              </div>
              <div>
                <div style={{
                  fontFamily: 'var(--font-display)',
                  fontWeight: 600,
                  fontSize: '0.95rem',
                  color: isPaused ? 'var(--accent-warning)' : 'var(--text-primary)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.5rem',
                }}>
                  {isPaused ? 'Paused' : scanProgress.currentStage || scanState}
                  {isScanning && (
                    <span style={{
                      width: 6,
                      height: 6,
                      borderRadius: '50%',
                      background: 'var(--accent-primary)',
                      animation: 'pulse 1.5s infinite',
                      boxShadow: '0 0 6px var(--accent-primary)',
                    }} />
                  )}
                </div>
                <div style={{
                  fontSize: '0.7rem',
                  color: 'var(--text-dim)',
                  marginTop: '0.15rem',
                  fontFamily: 'var(--font-sans)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.06em',
                }}>
                  {scanState}
                </div>
              </div>
            </div>
            <div style={{ display: 'flex', gap: '0.35rem' }}>
              {isScanning && (
                <button
                  onClick={() => pauseScan()}
                  style={{
                    padding: '0.35rem 0.75rem',
                    borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-button)',
                    color: 'var(--text-secondary)',
                    cursor: 'pointer',
                    fontSize: '0.75rem',
                    fontFamily: 'var(--font-sans)',
                    fontWeight: 500,
                    transition: 'all var(--transition-fast)',
                  }}
                >
                  Pause
                </button>
              )}
              {isPaused && (
                <button
                  onClick={() => resumeScan()}
                  style={{
                    padding: '0.35rem 0.75rem',
                    borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--accent-primary)',
                    background: 'linear-gradient(135deg, #0ea5e9, #0284c7)',
                    color: '#fff',
                    cursor: 'pointer',
                    fontSize: '0.75rem',
                    fontFamily: 'var(--font-sans)',
                    fontWeight: 500,
                    boxShadow: '0 0 12px rgba(14, 165, 233, 0.2)',
                    transition: 'all var(--transition-fast)',
                  }}
                >
                  Resume
                </button>
              )}
              <button
                onClick={() => stopScan()}
                style={{
                  padding: '0.35rem 0.75rem',
                  borderRadius: 'var(--radius-sm)',
                  border: '1px solid var(--accent-error-border)',
                  background: 'var(--accent-error-bg)',
                  color: 'var(--accent-danger-text)',
                  cursor: 'pointer',
                  fontSize: '0.75rem',
                  fontFamily: 'var(--font-sans)',
                  fontWeight: 500,
                  transition: 'all var(--transition-fast)',
                }}
              >
                Stop
              </button>
            </div>
          </div>

          <ProgressBar value={scanProgress.current} max={scanProgress.max} height={8} />

          {/* Stats grid */}
          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(3, 1fr)',
            gap: '0.75rem',
            marginTop: '1rem',
          }}>
            {[
              {
                icon: (
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                    <polyline points="14 2 14 8 20 8" />
                  </svg>
                ),
                label: 'Files',
                value: `${scanProgress.current} / ${scanProgress.max}`,
              },
              {
                icon: (
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="12" cy="12" r="10" />
                    <polyline points="12 6 12 12 16 14" />
                  </svg>
                ),
                label: 'Elapsed',
                value: formatDuration(scanProgress.elapsedSeconds),
              },
              {
                icon: (
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="12" cy="12" r="10" />
                    <polyline points="12 6 12 12 8 14" />
                  </svg>
                ),
                label: 'Remaining',
                value: scanProgress.remainingSeconds > 0 ? formatDuration(scanProgress.remainingSeconds) : '\u2014',
              },
            ].map(stat => (
              <div key={stat.label} style={{
                background: 'var(--bg-surface-raised)',
                borderRadius: 'var(--radius-md)',
                padding: '0.6rem 0.75rem',
                border: '1px solid var(--border-subtle)',
              }}>
                <div style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.35rem',
                  marginBottom: '0.3rem',
                }}>
                  {stat.icon}
                  <span style={{
                    fontSize: 10,
                    color: 'var(--text-dim)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.08em',
                    fontFamily: 'var(--font-sans)',
                  }}>
                    {stat.label}
                  </span>
                </div>
                <div style={{
                  fontFamily: 'var(--font-mono)',
                  fontSize: '0.9rem',
                  color: 'var(--text-primary)',
                  fontWeight: 500,
                }}>
                  {stat.value}
                </div>
              </div>
            ))}
          </div>

          {scanProgress.currentFile && (
            <div style={{
              marginTop: '0.75rem',
              fontSize: '0.72rem',
              color: 'var(--text-dim)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              fontFamily: 'var(--font-mono)',
              background: 'var(--bg-surface-raised)',
              padding: '0.4rem 0.65rem',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--border-subtle)',
            }}>
              {scanProgress.currentFile}
            </div>
          )}
        </div>
      )}

      {/* Scan Complete */}
      {(isDone || isAborted) && (
        <div style={{
          background: 'var(--bg-surface)',
          border: `1px solid ${isDone ? 'var(--accent-success-border)' : 'var(--border-default)'}`,
          borderRadius: 'var(--radius-lg)',
          padding: '1.25rem',
          marginBottom: '1.5rem',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          boxShadow: isDone ? '0 0 16px rgba(16, 185, 129, 0.08)' : 'var(--shadow-sm)',
          animation: 'cardIn 0.3s ease both',
          position: 'relative',
          overflow: 'hidden',
        }}>
          {isDone && (
            <div style={{
              position: 'absolute',
              top: 0,
              left: 0,
              right: 0,
              height: 2,
              background: 'linear-gradient(90deg, var(--accent-success-border), var(--accent-success))',
              boxShadow: '0 0 8px rgba(16, 185, 129, 0.3)',
            }} />
          )}
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
            <div style={{
              width: 32,
              height: 32,
              borderRadius: 'var(--radius-md)',
              background: isDone ? 'var(--accent-success-bg)' : 'var(--bg-surface-raised)',
              border: isDone ? '1px solid rgba(16, 185, 129, 0.2)' : '1px solid var(--border-subtle)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}>
              {isDone ? (
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--accent-success-text)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="20 6 9 17 4 12" />
                </svg>
              ) : (
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="10" />
                  <line x1="15" y1="9" x2="9" y2="15" />
                  <line x1="9" y1="9" x2="15" y2="15" />
                </svg>
              )}
            </div>
            <div>
              <div style={{
                fontFamily: 'var(--font-display)',
                fontWeight: 600,
                color: isDone ? 'var(--accent-success-text)' : 'var(--text-primary)',
                fontSize: '0.95rem',
              }}>
                {isDone ? 'Scan Complete' : 'Scan Aborted'}
              </div>
              {isDone && (
                <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)', marginTop: '0.15rem' }}>
                  View duplicate groups in the Results page
                </div>
              )}
            </div>
          </div>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            {isDone && (
              <button
                onClick={() => navigate('/results')}
                style={{
                  padding: '0.5rem 1.1rem',
                  borderRadius: 'var(--radius-md)',
                  border: 'none',
                  background: 'linear-gradient(135deg, #0ea5e9, #0284c7)',
                  color: '#fff',
                  cursor: 'pointer',
                  fontSize: '0.85rem',
                  fontWeight: 600,
                  fontFamily: 'var(--font-sans)',
                  boxShadow: '0 0 16px rgba(14, 165, 233, 0.2)',
                  transition: 'all var(--transition-base)',
                }}
              >
                View Results
              </button>
            )}
            <button
              onClick={() => setShowResetConfirm(true)}
              style={{
                padding: '0.5rem 1.1rem',
                borderRadius: 'var(--radius-md)',
                border: '1px solid var(--border-default)',
                background: 'var(--bg-button)',
                color: 'var(--text-secondary)',
                cursor: 'pointer',
                fontSize: '0.85rem',
                fontFamily: 'var(--font-sans)',
                transition: 'all var(--transition-fast)',
              }}
            >
              Reset
            </button>
          </div>
        </div>
      )}

      {/* Error */}
      {isError && (
        <div style={{
          background: 'var(--accent-error-bg)',
          border: '1px solid var(--accent-error-border)',
          borderRadius: 'var(--radius-lg)',
          padding: '1rem 1.25rem',
          marginBottom: '1.5rem',
          color: 'var(--accent-danger-text)',
          fontSize: '0.85rem',
          display: 'flex',
          alignItems: 'center',
          gap: '0.65rem',
          animation: 'cardIn 0.3s ease both',
        }}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
          Scan error: {scanProgress?.errorMessage || 'Unknown error'}
        </div>
      )}

      {/* Include Paths */}
      <div style={{
        background: 'var(--bg-surface)',
        border: '1px solid var(--border-default)',
        borderRadius: 'var(--radius-lg)',
        padding: '1.25rem',
        marginBottom: '1rem',
        boxShadow: 'var(--shadow-sm)',
        transition: 'border-color var(--transition-base), box-shadow var(--transition-base)',
        animation: 'fadeInUp 0.4s ease both',
        animationDelay: '0.05s',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.85rem' }}>
          <div style={{
            width: 3,
            height: 18,
            borderRadius: 2,
            background: 'var(--accent-primary)',
            boxShadow: '0 0 8px rgba(14, 165, 233, 0.3)',
          }} />
          <h2 style={{
            margin: 0,
            fontFamily: 'var(--font-display)',
            fontSize: '0.95rem',
            fontWeight: 600,
            color: 'var(--text-primary)',
          }}>
            Include Paths
          </h2>
          <span style={{
            fontSize: '0.7rem',
            color: 'var(--text-dim)',
            background: 'var(--bg-surface-raised)',
            padding: '0.15rem 0.5rem',
            borderRadius: 'var(--radius-sm)',
            fontFamily: 'var(--font-mono)',
            border: '1px solid var(--border-subtle)',
          }}>
            {localIncludePaths.length}
          </span>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '0.85rem' }}>
          <input
            value={newPath}
            onChange={e => setNewPath(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') handleAddIncludePath() }}
            placeholder="Enter path to scan..."
            style={{
              flex: 1,
              padding: '0.5rem 0.75rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-md)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: '0.85rem',
              fontFamily: 'var(--font-mono)',
              outline: 'none',
              transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
            }}
          />
          <button
            onClick={() => { setPathBrowserTarget('include'); setShowPathBrowser(true) }}
            style={{
              padding: '0.5rem 0.75rem',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-button)',
              color: 'var(--text-muted)',
              cursor: 'pointer',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              display: 'flex',
              alignItems: 'center',
              gap: '0.3rem',
              transition: 'all var(--transition-fast)',
            }}
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
            </svg>
            Browse
          </button>
          <button
            onClick={handleAddIncludePath}
            disabled={!newPath.trim()}
            style={{
              padding: '0.5rem 0.85rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: newPath.trim() ? 'var(--accent-primary)' : 'var(--bg-button)',
              color: newPath.trim() ? '#fff' : 'var(--text-dim)',
              cursor: newPath.trim() ? 'pointer' : 'not-allowed',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 500,
              transition: 'all var(--transition-fast)',
            }}
          >
            Add
          </button>
        </div>

        {localIncludePaths.length === 0 ? (
          <div style={{
            padding: '1.5rem',
            textAlign: 'center',
            color: 'var(--text-dim)',
            fontSize: '0.8rem',
            border: '1px dashed var(--border-subtle)',
            borderRadius: 'var(--radius-md)',
            fontFamily: 'var(--font-sans)',
          }}>
            No include paths configured. Add a folder to scan.
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
            {localIncludePaths.map(path => (
              <div
                key={path}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.5rem',
                  padding: '0.45rem 0.65rem',
                  background: 'var(--bg-surface-raised)',
                  borderRadius: 'var(--radius-md)',
                  border: '1px solid var(--border-subtle)',
                  borderLeft: '3px solid var(--accent-primary)',
                  transition: 'all var(--transition-fast)',
                }}
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="var(--accent-primary)" opacity="0.7">
                  <path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z" />
                </svg>
                <span style={{
                  flex: 1,
                  fontSize: '0.8rem',
                  fontFamily: 'var(--font-mono)',
                  color: 'var(--text-secondary)',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                }}>
                  {path}
                </span>
                <button
                  onClick={() => handleRemoveIncludePath(path)}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: 'var(--text-dim)',
                    cursor: 'pointer',
                    padding: '0.1rem 0.2rem',
                    fontSize: 14,
                    lineHeight: 1,
                    borderRadius: 'var(--radius-sm)',
                    transition: 'color var(--transition-fast)',
                  }}
                >
                  &times;
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Exclude Paths */}
      <div style={{
        background: 'var(--bg-surface)',
        border: '1px solid var(--border-default)',
        borderRadius: 'var(--radius-lg)',
        padding: '1.25rem',
        marginBottom: '1rem',
        boxShadow: 'var(--shadow-sm)',
        transition: 'border-color var(--transition-base), box-shadow var(--transition-base)',
        animation: 'fadeInUp 0.4s ease both',
        animationDelay: '0.1s',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.85rem' }}>
          <div style={{
            width: 3,
            height: 18,
            borderRadius: 2,
            background: 'var(--accent-danger-text)',
            boxShadow: '0 0 8px rgba(252, 165, 165, 0.2)',
          }} />
          <h2 style={{
            margin: 0,
            fontFamily: 'var(--font-display)',
            fontSize: '0.95rem',
            fontWeight: 600,
            color: 'var(--text-primary)',
          }}>
            Exclude Paths
          </h2>
          <span style={{
            fontSize: '0.7rem',
            color: 'var(--text-dim)',
            background: 'var(--bg-surface-raised)',
            padding: '0.15rem 0.5rem',
            borderRadius: 'var(--radius-sm)',
            fontFamily: 'var(--font-mono)',
            border: '1px solid var(--border-subtle)',
          }}>
            {localExcludePaths.length}
          </span>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '0.85rem' }}>
          <input
            value={newPath}
            onChange={e => setNewPath(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') handleAddExcludePath() }}
            placeholder="Enter path to exclude..."
            style={{
              flex: 1,
              padding: '0.5rem 0.75rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-md)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: '0.85rem',
              fontFamily: 'var(--font-mono)',
              outline: 'none',
              transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
            }}
          />
          <button
            onClick={() => { setPathBrowserTarget('exclude'); setShowPathBrowser(true) }}
            style={{
              padding: '0.5rem 0.75rem',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-button)',
              color: 'var(--text-muted)',
              cursor: 'pointer',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              display: 'flex',
              alignItems: 'center',
              gap: '0.3rem',
              transition: 'all var(--transition-fast)',
            }}
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
            </svg>
            Browse
          </button>
          <button
            onClick={handleAddExcludePath}
            disabled={!newPath.trim()}
            style={{
              padding: '0.5rem 0.85rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: newPath.trim() ? 'var(--accent-primary)' : 'var(--bg-button)',
              color: newPath.trim() ? '#fff' : 'var(--text-dim)',
              cursor: newPath.trim() ? 'pointer' : 'not-allowed',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 500,
              transition: 'all var(--transition-fast)',
            }}
          >
            Add
          </button>
        </div>

        {localExcludePaths.length === 0 ? (
          <div style={{
            padding: '1rem',
            textAlign: 'center',
            color: 'var(--text-dim)',
            fontSize: '0.8rem',
            border: '1px dashed var(--border-subtle)',
            borderRadius: 'var(--radius-md)',
            fontFamily: 'var(--font-sans)',
          }}>
            No exclude paths configured
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
            {localExcludePaths.map(path => (
              <div
                key={path}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.5rem',
                  padding: '0.45rem 0.65rem',
                  background: 'var(--bg-surface-raised)',
                  borderRadius: 'var(--radius-md)',
                  border: '1px solid var(--border-subtle)',
                  borderLeft: '3px solid var(--accent-danger-text)',
                  transition: 'all var(--transition-fast)',
                }}
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="var(--accent-danger-text)" opacity="0.6">
                  <path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z" />
                </svg>
                <span style={{
                  flex: 1,
                  fontSize: '0.8rem',
                  fontFamily: 'var(--font-mono)',
                  color: 'var(--text-secondary)',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                }}>
                  {path}
                </span>
                <button
                  onClick={() => handleRemoveExcludePath(path)}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: 'var(--text-dim)',
                    cursor: 'pointer',
                    padding: '0.1rem 0.2rem',
                    fontSize: 14,
                    lineHeight: 1,
                    borderRadius: 'var(--radius-sm)',
                    transition: 'color var(--transition-fast)',
                  }}
                >
                  &times;
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Path Browser */}
      <PathBrowser
        open={showPathBrowser}
        onSelect={(path) => {
          if (pathBrowserTarget === 'include') {
            setLocalIncludePaths(prev => [...prev, path])
            updateSettingsMutation.mutate({ includeList: [...localIncludePaths, path], blackList: localExcludePaths })
          } else {
            setLocalExcludePaths(prev => [...prev, path])
            updateSettingsMutation.mutate({ includeList: localIncludePaths, blackList: [...localExcludePaths, path] })
          }
        }}
        onClose={() => setShowPathBrowser(false)}
      />

      {/* Reset Confirm */}
      <ConfirmDialog
        open={showResetConfirm}
        title="Reset Scan"
        message="This will clear all scan results and reset the scan state. This cannot be undone."
        confirmLabel="Reset"
        variant="danger"
        onConfirm={() => { resetScan(); setShowResetConfirm(false) }}
        onCancel={() => setShowResetConfirm(false)}
      />
    </div>
  )
}
