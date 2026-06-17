import { useState, useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { getSettings, updateSettings } from '../api/settings'
import { resetScan, clearDatabase } from '../api/scan'
import { useSignalR } from '../hooks/useSignalR'
import { useSSE } from '../hooks/useSSE'
import { ProgressBar } from '../components/shared/ProgressBar'
import { ConfirmDialog } from '../components/shared/ConfirmDialog'
import { PathBrowser } from '../components/shared/PathBrowser'
import { LivePreviewPanel } from '../components/LivePreviewPanel'
import { formatDuration } from '../utils/format'

export function ScanPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const signalR = useSignalR()
  const sse = useSSE()

  const realtime = signalR.connected ? signalR : sse
  const scanState = realtime.state
  const scanProgress = realtime.progress

  const [showResetConfirm, setShowResetConfirm] = useState(false)
  const [showClearDbConfirm, setShowClearDbConfirm] = useState(false)
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
    const path = newPath.trim()
    const updated = [...localIncludePaths, path]
    setLocalIncludePaths(updated)
    updateSettingsMutation.mutate({ includeList: updated, blackList: localExcludePaths })
    setNewPath('')
  }, [newPath, localIncludePaths, localExcludePaths, updateSettingsMutation])

  const handleAddExcludePath = useCallback(() => {
    if (!newPath.trim()) return
    const path = newPath.trim()
    const updated = [...localExcludePaths, path]
    setLocalExcludePaths(updated)
    updateSettingsMutation.mutate({ includeList: localIncludePaths, blackList: updated })
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

  const isScanning = scanState === 'Scanning' || scanState === 'Comparing'
  const isPaused = scanState === 'Paused'
  const isDone = scanState === 'Done'
  const isError = scanState === 'Error'
  const isAborted = scanState === 'Aborted'

  return (
    <div style={{ animation: 'fadeIn 0.2s ease', display: 'flex', gap: '1rem' }}>
      {/* Main Content */}
      <div style={{ flex: 1, minWidth: 0 }}>
        {/* Scan Progress */}
      {(isScanning || isPaused) && scanProgress && (
        <div style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--border-default)',
          borderRadius: 'var(--radius-lg)',
          padding: '1rem',
          marginBottom: '1rem',
          animation: 'cardIn 0.2s ease both',
        }}>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            marginBottom: '0.75rem',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              {isPaused ? (
                <svg width="12" height="12" viewBox="0 0 24 24" fill="var(--accent-warning)">
                  <rect x="6" y="4" width="4" height="16" rx="1" />
                  <rect x="14" y="4" width="4" height="16" rx="1" />
                </svg>
              ) : (
                <div style={{
                  width: 8,
                  height: 8,
                  borderRadius: '50%',
                  background: 'var(--accent-primary)',
                  animation: 'pulse 1.5s infinite',
                }} />
              )}
              <span style={{ fontWeight: 600, fontSize: 12, color: isPaused ? 'var(--accent-warning)' : 'var(--text-primary)' }}>
                {isPaused ? 'Paused' : scanProgress.currentStage || scanState}
              </span>
              <span style={{ fontSize: 10, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                {scanState}
              </span>
            </div>
          </div>

          <ProgressBar value={scanProgress.current} max={scanProgress.max} height={6} />

          <div style={{
            display: 'flex',
            gap: '1.5rem',
            marginTop: '0.75rem',
            fontSize: 11,
            color: 'var(--text-muted)',
          }}>
            <span>
              Files: <strong style={{ color: 'var(--text-primary)', fontFamily: 'var(--font-mono)' }}>{scanProgress.current}/{scanProgress.max}</strong>
            </span>
            <span>
              Elapsed: <strong style={{ color: 'var(--text-primary)', fontFamily: 'var(--font-mono)' }}>{formatDuration(scanProgress.elapsedSeconds)}</strong>
            </span>
            {scanProgress.remainingSeconds > 0 && (
              <span>
                Remaining: <strong style={{ color: 'var(--text-primary)', fontFamily: 'var(--font-mono)' }}>{formatDuration(scanProgress.remainingSeconds)}</strong>
              </span>
            )}
          </div>

          {scanProgress.currentFile && (
            <div style={{
              marginTop: '0.5rem',
              fontSize: 10,
              color: 'var(--text-dim)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              fontFamily: 'var(--font-mono)',
            }}>
              {scanProgress.currentFile}
            </div>
          )}
        </div>
      )}

      {/* Scan Complete */}
      {(isDone || isAborted) && (
        <div style={{
          background: isDone ? 'var(--accent-success-bg)' : 'var(--bg-surface)',
          border: `1px solid ${isDone ? 'var(--accent-success-border)' : 'var(--border-default)'}`,
          borderRadius: 'var(--radius-lg)',
          padding: '0.85rem 1rem',
          marginBottom: '1rem',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          animation: 'cardIn 0.2s ease both',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            {isDone ? (
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--accent-success)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <polyline points="20 6 9 17 4 12" />
              </svg>
            ) : (
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="2">
                <circle cx="12" cy="12" r="10" />
                <line x1="15" y1="9" x2="9" y2="15" />
                <line x1="9" y1="9" x2="15" y2="15" />
              </svg>
            )}
            <span style={{ fontWeight: 600, fontSize: 12, color: isDone ? 'var(--accent-success-text)' : 'var(--text-primary)' }}>
              {isDone ? 'Scan Complete' : 'Scan Aborted'}
            </span>
          </div>
          <div style={{ display: 'flex', gap: '0.4rem' }}>
            {isDone && (
              <button
                className="toolbar-btn primary"
                onClick={() => navigate('/results')}
              >
                View Results
              </button>
            )}
            <button
              className="toolbar-btn"
              onClick={() => setShowResetConfirm(true)}
            >
              Reset
            </button>
            <button
              className="toolbar-btn"
              onClick={() => setShowClearDbConfirm(true)}
            >
              Clear Database
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
          padding: '0.75rem 1rem',
          marginBottom: '1rem',
          color: 'var(--accent-danger-text)',
          fontSize: 12,
          display: 'flex',
          alignItems: 'center',
          gap: '0.5rem',
          animation: 'cardIn 0.2s ease both',
        }}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
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
        padding: '1rem',
        marginBottom: '0.75rem',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', marginBottom: '0.65rem' }}>
          <h2 style={{ margin: 0, fontSize: 11, color: 'var(--accent-primary)', fontWeight: 600 }}>
            Include Paths
          </h2>
          <span style={{
            fontSize: 10,
            color: 'var(--text-dim)',
            background: 'var(--bg-surface-raised)',
            padding: '0.1rem 0.4rem',
            borderRadius: 'var(--radius-sm)',
            fontFamily: 'var(--font-mono)',
          }}>
            {localIncludePaths.length}
          </span>
        </div>

        <div style={{ display: 'flex', gap: '0.35rem', marginBottom: '0.6rem' }}>
          <input
            value={newPath}
            onChange={e => setNewPath(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') handleAddIncludePath() }}
            placeholder="Enter path to scan..."
            style={{
              flex: 1,
              padding: '0.4rem 0.6rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-sm)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: 11,
              fontFamily: 'var(--font-mono)',
              outline: 'none',
            }}
          />
          <button
            className="toolbar-btn"
            onClick={() => { setPathBrowserTarget('include'); setShowPathBrowser(true) }}
          >
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
            </svg>
            Browse
          </button>
          <button
            className="toolbar-btn primary"
            onClick={handleAddIncludePath}
            disabled={!newPath.trim()}
          >
            Add
          </button>
        </div>

        {localIncludePaths.length === 0 ? (
          <div style={{
            padding: '1rem',
            textAlign: 'center',
            color: 'var(--text-dim)',
            fontSize: 11,
            border: '1px dashed var(--border-subtle)',
            borderRadius: 'var(--radius-sm)',
          }}>
            No include paths configured. Add a folder to scan.
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {localIncludePaths.map(path => (
              <div
                key={path}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.4rem',
                  padding: '0.3rem 0.5rem',
                  background: 'var(--bg-surface-raised)',
                  borderRadius: 'var(--radius-sm)',
                  borderLeft: '2px solid var(--accent-primary)',
                }}
              >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="var(--accent-primary)" opacity="0.7">
                  <path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z" />
                </svg>
                <span style={{
                  flex: 1,
                  fontSize: 11,
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
        padding: '1rem',
        marginBottom: '0.75rem',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', marginBottom: '0.65rem' }}>
          <h2 style={{ margin: 0, fontSize: 11, color: 'var(--accent-danger-text)', fontWeight: 600 }}>
            Exclude Paths
          </h2>
          <span style={{
            fontSize: 10,
            color: 'var(--text-dim)',
            background: 'var(--bg-surface-raised)',
            padding: '0.1rem 0.4rem',
            borderRadius: 'var(--radius-sm)',
            fontFamily: 'var(--font-mono)',
          }}>
            {localExcludePaths.length}
          </span>
        </div>

        <div style={{ display: 'flex', gap: '0.35rem', marginBottom: '0.6rem' }}>
          <input
            value={newPath}
            onChange={e => setNewPath(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') handleAddExcludePath() }}
            placeholder="Enter path to exclude..."
            style={{
              flex: 1,
              padding: '0.4rem 0.6rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-sm)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: 11,
              fontFamily: 'var(--font-mono)',
              outline: 'none',
            }}
          />
          <button
            className="toolbar-btn"
            onClick={() => { setPathBrowserTarget('exclude'); setShowPathBrowser(true) }}
          >
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
            </svg>
            Browse
          </button>
          <button
            className="toolbar-btn primary"
            onClick={handleAddExcludePath}
            disabled={!newPath.trim()}
          >
            Add
          </button>
        </div>

        {localExcludePaths.length === 0 ? (
          <div style={{
            padding: '0.75rem',
            textAlign: 'center',
            color: 'var(--text-dim)',
            fontSize: 11,
            border: '1px dashed var(--border-subtle)',
            borderRadius: 'var(--radius-sm)',
          }}>
            No exclude paths configured
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {localExcludePaths.map(path => (
              <div
                key={path}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.4rem',
                  padding: '0.3rem 0.5rem',
                  background: 'var(--bg-surface-raised)',
                  borderRadius: 'var(--radius-sm)',
                  borderLeft: '2px solid var(--accent-danger-text)',
                }}
              >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="var(--accent-danger-text)" opacity="0.6">
                  <path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z" />
                </svg>
                <span style={{
                  flex: 1,
                  fontSize: 11,
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
            const updated = [...localIncludePaths, path]
            setLocalIncludePaths(updated)
            updateSettingsMutation.mutate({ includeList: updated, blackList: localExcludePaths })
          } else {
            const updated = [...localExcludePaths, path]
            setLocalExcludePaths(updated)
            updateSettingsMutation.mutate({ includeList: localIncludePaths, blackList: updated })
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

      {/* Clear Database Confirm */}
      <ConfirmDialog
        open={showClearDbConfirm}
        title="Clear Database"
        message="This will delete all cached scan data including file hashes and thumbnails. Next scan will re-process all files. This cannot be undone."
        confirmLabel="Clear Database"
        variant="danger"
        onConfirm={() => { clearDatabase(); setShowClearDbConfirm(false) }}
        onCancel={() => setShowClearDbConfirm(false)}
      />
      </div>

      {/* Live Preview Sidebar */}
      <div style={{ width: '320px', flexShrink: 0 }}>
        <LivePreviewPanel progress={scanProgress} />
      </div>
    </div>
  )
}
