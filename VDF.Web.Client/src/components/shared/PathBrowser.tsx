import { useState, useCallback, useEffect } from 'react'
import { apiRequest } from '../../api/client'

interface PathBrowserProps {
  open: boolean
  onSelect: (path: string) => void
  onClose: () => void
}

interface DirEntry {
  name: string
  path: string
  isDirectory: boolean
}

export function PathBrowser({ open, onSelect, onClose }: PathBrowserProps) {
  const [currentPath, setCurrentPath] = useState('')
  const [entries, setEntries] = useState<DirEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const browse = useCallback(async (path: string) => {
    setLoading(true)
    setError(null)
    try {
      const data = await apiRequest<DirEntry[]>('/browse', {
        method: 'POST',
        body: { path },
      })
      setEntries(data)
      setCurrentPath(path)
    } catch {
      setError('Failed to list directory')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (open && !currentPath) {
      browse('/')
    }
  }, [open, currentPath, browse])

  if (!open) return null

  return (
    <div style={{
      position: 'fixed',
      inset: 0,
      zIndex: 1000,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'rgba(0,0,0,0.6)',
      backdropFilter: 'blur(4px)',
    }}
      onClick={onClose}
    >
      <div
        style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--border-default)',
          borderRadius: 'var(--radius-xl)',
          width: 520,
          maxHeight: '80vh',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: 'var(--shadow-lg)',
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{
          padding: '1rem 1.25rem',
          borderBottom: '1px solid var(--border-subtle)',
          display: 'flex',
          alignItems: 'center',
          gap: '0.75rem',
        }}>
          <h3 style={{
            fontFamily: 'var(--font-display)',
            fontSize: '1rem',
            fontWeight: 600,
            flex: 1,
          }}>
            Browse Folder
          </h3>
          <button
            onClick={onClose}
            style={{
              background: 'none',
              border: 'none',
              color: 'var(--text-muted)',
              cursor: 'pointer',
              fontSize: 18,
              padding: 0,
              lineHeight: 1,
            }}
          >
            &times;
          </button>
        </div>

        <div style={{
          padding: '0.75rem 1.25rem',
          borderBottom: '1px solid var(--border-subtle)',
        }}>
          <input
            value={currentPath}
            onChange={e => setCurrentPath(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') browse(currentPath) }}
            placeholder="Enter path..."
            style={{
              width: '100%',
              padding: '0.4rem 0.6rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-md)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: '0.85rem',
              fontFamily: 'var(--font-mono)',
            }}
          />
        </div>

        <div style={{
          flex: 1,
          overflowY: 'auto',
          padding: '0.5rem 0',
          minHeight: 200,
        }}>
          {loading && (
            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--text-muted)' }}>
              Loading...
            </div>
          )}
          {error && (
            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--accent-danger-text)' }}>
              {error}
            </div>
          )}
          {!loading && !error && entries.length === 0 && (
            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--text-dim)' }}>
              No subdirectories found
            </div>
          )}
          {entries.filter(e => e.isDirectory).map(entry => (
            <button
              key={entry.path}
              onClick={() => browse(entry.path)}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.5rem',
                width: '100%',
                padding: '0.4rem 1.25rem',
                border: 'none',
                background: 'transparent',
                color: 'var(--text-secondary)',
                cursor: 'pointer',
                fontSize: '0.85rem',
                textAlign: 'left',
                fontFamily: 'var(--font-sans)',
              }}
              onMouseEnter={e => { e.currentTarget.style.background = 'var(--bg-hover)' }}
              onMouseLeave={e => { e.currentTarget.style.background = 'transparent' }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" opacity="0.5">
                <path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z" />
              </svg>
              {entry.name}
            </button>
          ))}
        </div>

        <div style={{
          padding: '0.75rem 1.25rem',
          borderTop: '1px solid var(--border-subtle)',
          display: 'flex',
          justifyContent: 'flex-end',
          gap: '0.5rem',
        }}>
          <button
            onClick={onClose}
            style={{
              padding: '0.45rem 1rem',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-button)',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '0.85rem',
              fontFamily: 'var(--font-sans)',
            }}
          >
            Cancel
          </button>
          <button
            onClick={() => { onSelect(currentPath); onClose() }}
            style={{
              padding: '0.45rem 1rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: 'var(--accent-primary)',
              color: '#fff',
              cursor: 'pointer',
              fontSize: '0.85rem',
              fontWeight: 500,
              fontFamily: 'var(--font-sans)',
            }}
          >
            Select This Folder
          </button>
        </div>
      </div>
    </div>
  )
}
