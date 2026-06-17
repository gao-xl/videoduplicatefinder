import { useState, useEffect, useMemo } from 'react'
import { Spinner } from './shared/Spinner'
import type { ScanProgressResponse } from '../api/scan'

interface LivePreviewPanelProps {
  progress: ScanProgressResponse | null
}

export function LivePreviewPanel({ progress }: LivePreviewPanelProps) {
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [imageUrl, setImageUrl] = useState<string | null>(null)

  const thumbnailPath = useMemo(() => {
    if (!progress?.currentThumbnailPath) return null
    return progress.currentThumbnailPath
  }, [progress?.currentThumbnailPath])

  useEffect(() => {
    if (!thumbnailPath) {
      setImageUrl(null)
      setError(null)
      return
    }

    setIsLoading(true)
    setError(null)

    const encodedPath = encodeURIComponent(thumbnailPath)
    const url = `/api/thumbnail/hq?path=${encodedPath}&w=320&q=70`
    
    const img = new Image()
    img.onload = () => {
      setImageUrl(url)
      setIsLoading(false)
    }
    img.onerror = () => {
      setError('Failed to load thumbnail')
      setIsLoading(false)
    }
    img.src = url

    return () => {
      img.src = ''
    }
  }, [thumbnailPath])

  const isActive = progress?.state === 'Scanning' || progress?.state === 'Comparing'

  return (
    <div style={{
      background: 'var(--bg-surface)',
      border: '1px solid var(--border-default)',
      borderRadius: 'var(--radius-lg)',
      padding: 'var(--spacing-md)',
      height: '100%',
      display: 'flex',
      flexDirection: 'column',
    }}>
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-sm)',
        marginBottom: 'var(--spacing-md)',
      }}>
        <div style={{
          width: 4,
          height: 16,
          background: isActive ? 'var(--accent-primary)' : 'var(--text-muted)',
          borderRadius: 2,
        }} />
        <h3 style={{
          fontSize: 'var(--font-size-sm)',
          fontWeight: 600,
          color: 'var(--text-primary)',
        }}>
          Live Preview
        </h3>
        {isActive && (
          <div style={{
            fontSize: 10,
            color: 'var(--accent-success)',
            fontWeight: 500,
            padding: '2px 6px',
            background: 'var(--accent-success-bg)',
            borderRadius: 4,
          }}>
            SCANNING
          </div>
        )}
      </div>

      {/* Thumbnail Display Area */}
      <div style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--bg-default)',
        borderRadius: 'var(--radius-md)',
        overflow: 'hidden',
        minHeight: 200,
      }}>
        {isLoading && (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--spacing-md)' }}>
            <Spinner size={32} />
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>Loading thumbnail...</span>
          </div>
        )}

        {error && !isLoading && (
          <div style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 'var(--spacing-sm)',
            padding: 'var(--spacing-lg)',
            textAlign: 'center',
          }}>
            <div style={{
              width: 48,
              height: 48,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              background: 'var(--accent-error-bg)',
              borderRadius: '50%',
            }}>
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="var(--accent-error)" strokeWidth="2">
                <circle cx="12" cy="12" r="10" />
                <line x1="15" y1="9" x2="9" y2="15" />
                <line x1="9" y1="9" x2="15" y2="15" />
              </svg>
            </div>
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>{error}</span>
          </div>
        )}

        {imageUrl && !isLoading && !error && (
          <img
            src={imageUrl}
            alt="Current file thumbnail"
            style={{
              maxWidth: '100%',
              maxHeight: '100%',
              objectFit: 'contain',
            }}
          />
        )}

        {!thumbnailPath && !isLoading && !error && (
          <div style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 'var(--spacing-sm)',
            padding: 'var(--spacing-lg)',
            textAlign: 'center',
          }}>
            <div style={{
              width: 48,
              height: 48,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              background: 'var(--bg-surface)',
              borderRadius: '50%',
              border: '2px dashed var(--border-default)',
            }}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="1.5">
                <rect x="3" y="3" width="18" height="18" rx="2" />
                <circle cx="8.5" cy="8.5" r="1.5" />
                <polyline points="21 15 16 10 5 21" />
              </svg>
            </div>
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              {isActive ? 'Waiting for file...' : 'No active scan'}
            </span>
          </div>
        )}
      </div>

      {/* File Info */}
      {progress?.currentFile && (
        <div style={{ marginTop: 'var(--spacing-md)' }}>
          <div style={{
            fontSize: 11,
            color: 'var(--text-dim)',
            marginBottom: 4,
          }}>
            Current File
          </div>
          <div style={{
            fontSize: 12,
            color: 'var(--text-primary)',
            fontFamily: 'var(--font-mono)',
            wordBreak: 'break-all',
            lineHeight: 1.4,
            maxHeight: '48px',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
          }}>
            {progress.currentFile}
          </div>
        </div>
      )}

      {/* Progress Info */}
      {progress && (
        <div style={{
          marginTop: 'var(--spacing-md)',
          display: 'flex',
          justifyContent: 'space-between',
          fontSize: 11,
          color: 'var(--text-muted)',
        }}>
          <span>
            {progress.current} / {progress.max}
          </span>
          {progress.currentStage && (
            <span style={{
              padding: '2px 6px',
              background: 'var(--bg-default)',
              borderRadius: 4,
              color: 'var(--text-primary)',
            }}>
              {progress.currentStage}
              {progress.stageMax > 0 && ` ${progress.stageCurrent}/${progress.stageMax}`}
            </span>
          )}
        </div>
      )}
    </div>
  )
}