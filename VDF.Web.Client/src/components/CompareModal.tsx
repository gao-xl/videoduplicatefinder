import { useState, useRef, useCallback } from 'react'
import { thumbnailUrl, type DuplicateItemDto } from '../api/results'

interface CompareModalProps {
  items: DuplicateItemDto[]
  onClose: () => void
}

type CompareMode = 'sideBySide' | 'swipe'

export function CompareModal({ items, onClose }: CompareModalProps) {
  const [mode, setMode] = useState<CompareMode>('sideBySide')
  const [leftIdx, setLeftIdx] = useState(0)
  const [rightIdx, setRightIdx] = useState(1)
  const [swipePos, setSwipePos] = useState(50)
  const dragging = useRef(false)
  const containerRef = useRef<HTMLDivElement>(null)

  const handleMouseDown = useCallback(() => { dragging.current = true }, [])
  const handleMouseUp = useCallback(() => { dragging.current = false }, [])
  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    if (!dragging.current || !containerRef.current) return
    const rect = containerRef.current.getBoundingClientRect()
    const pct = ((e.clientX - rect.left) / rect.width) * 100
    setSwipePos(Math.max(5, Math.min(95, pct)))
  }, [])

  if (items.length < 2) return null

  const leftItem = items[leftIdx]
  const rightItem = items[rightIdx]

  return (
    <div style={{
      position: 'fixed',
      inset: 0,
      zIndex: 1200,
      background: 'rgba(0,0,0,0.85)',
      backdropFilter: 'blur(8px)',
      display: 'flex',
      flexDirection: 'column',
      animation: 'fadeIn 0.2s ease',
    }}
      onClick={e => { if (e.target === e.currentTarget) onClose() }}
    >
      {/* Top bar */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        padding: '0.75rem 1.25rem',
        borderBottom: '1px solid rgba(255,255,255,0.1)',
        gap: '1rem',
        flexShrink: 0,
      }}>
        <h3 style={{
          fontFamily: 'var(--font-display)',
          fontSize: '1rem',
          fontWeight: 600,
          color: '#fff',
          margin: 0,
        }}>
          Compare
        </h3>

        {/* Mode toggle */}
        <div style={{
          display: 'flex',
          background: 'rgba(255,255,255,0.08)',
          borderRadius: 'var(--radius-md)',
          padding: 2,
        }}>
          <button
            onClick={() => setMode('sideBySide')}
            style={{
              padding: '0.3rem 0.65rem',
              borderRadius: 'var(--radius-sm)',
              border: 'none',
              background: mode === 'sideBySide' ? 'rgba(255,255,255,0.15)' : 'transparent',
              color: mode === 'sideBySide' ? '#fff' : 'rgba(255,255,255,0.5)',
              cursor: 'pointer',
              fontSize: '0.75rem',
              fontFamily: 'var(--font-sans)',
            }}
          >
            Side by side
          </button>
          <button
            onClick={() => setMode('swipe')}
            style={{
              padding: '0.3rem 0.65rem',
              borderRadius: 'var(--radius-sm)',
              border: 'none',
              background: mode === 'swipe' ? 'rgba(255,255,255,0.15)' : 'transparent',
              color: mode === 'swipe' ? '#fff' : 'rgba(255,255,255,0.5)',
              cursor: 'pointer',
              fontSize: '0.75rem',
              fontFamily: 'var(--font-sans)',
            }}
          >
            Swipe
          </button>
        </div>

        {/* Item selectors */}
        <div style={{ display: 'flex', gap: '0.5rem', flex: 1 }}>
          <select
            value={leftIdx}
            onChange={e => setLeftIdx(Number(e.target.value))}
            style={{
              flex: 1,
              padding: '0.3rem 0.5rem',
              border: '1px solid rgba(255,255,255,0.15)',
              borderRadius: 'var(--radius-sm)',
              background: 'rgba(255,255,255,0.08)',
              color: '#fff',
              fontSize: '0.75rem',
              fontFamily: 'var(--font-mono)',
              cursor: 'pointer',
            }}
          >
            {items.map((item, i) => (
              <option key={item.path} value={i} style={{ background: '#1a1a2e', color: '#fff' }}>
                {item.path.split(/[/\\]/).pop()}
              </option>
            ))}
          </select>
          <select
            value={rightIdx}
            onChange={e => setRightIdx(Number(e.target.value))}
            style={{
              flex: 1,
              padding: '0.3rem 0.5rem',
              border: '1px solid rgba(255,255,255,0.15)',
              borderRadius: 'var(--radius-sm)',
              background: 'rgba(255,255,255,0.08)',
              color: '#fff',
              fontSize: '0.75rem',
              fontFamily: 'var(--font-mono)',
              cursor: 'pointer',
            }}
          >
            {items.map((item, i) => (
              <option key={item.path} value={i} style={{ background: '#1a1a2e', color: '#fff' }}>
                {item.path.split(/[/\\]/).pop()}
              </option>
            ))}
          </select>
        </div>

        <button
          onClick={onClose}
          style={{
            background: 'none',
            border: 'none',
            color: 'rgba(255,255,255,0.6)',
            cursor: 'pointer',
            fontSize: 20,
            padding: 0,
            lineHeight: 1,
          }}
        >
          &times;
        </button>
      </div>

      {/* Content */}
      <div style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '1.5rem',
        overflow: 'hidden',
      }}>
        {mode === 'sideBySide' ? (
          <div style={{
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: '1rem',
            width: '100%',
            maxWidth: 1200,
            height: '100%',
          }}>
            <ComparePane item={leftItem} label="A" />
            <ComparePane item={rightItem} label="B" />
          </div>
        ) : (
          <div
            ref={containerRef}
            style={{
              position: 'relative',
              width: '100%',
              maxWidth: 900,
              aspectRatio: '16/9',
              overflow: 'hidden',
              borderRadius: 'var(--radius-lg)',
              cursor: 'col-resize',
              userSelect: 'none',
            }}
            onMouseDown={handleMouseDown}
            onMouseUp={handleMouseUp}
            onMouseLeave={handleMouseUp}
            onMouseMove={handleMouseMove}
          >
            {/* Right image (full width, behind) */}
            <img
              src={thumbnailUrl(rightItem.path, 960, 90)}
              alt=""
              style={{
                position: 'absolute',
                inset: 0,
                width: '100%',
                height: '100%',
                objectFit: 'contain',
                background: '#000',
              }}
            />
            {/* Left image (clipped) */}
            <div style={{
              position: 'absolute',
              inset: 0,
              width: `${swipePos}%`,
              overflow: 'hidden',
            }}>
              <img
                src={thumbnailUrl(leftItem.path, 960, 90)}
                alt=""
                style={{
                  width: containerRef.current ? containerRef.current.offsetWidth : 900,
                  height: '100%',
                  objectFit: 'contain',
                  background: '#000',
                }}
              />
            </div>
            {/* Divider */}
            <div style={{
              position: 'absolute',
              top: 0,
              bottom: 0,
              left: `${swipePos}%`,
              width: 3,
              background: '#fff',
              transform: 'translateX(-1.5px)',
              boxShadow: '0 0 8px rgba(0,0,0,0.5)',
            }}>
              <div style={{
                position: 'absolute',
                top: '50%',
                left: '50%',
                transform: 'translate(-50%, -50%)',
                width: 28,
                height: 28,
                borderRadius: '50%',
                background: '#fff',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                boxShadow: '0 2px 8px rgba(0,0,0,0.4)',
              }}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#333" strokeWidth="2.5">
                  <polyline points="15 18 9 12 15 6" />
                </svg>
              </div>
            </div>
            {/* Labels */}
            <div style={{
              position: 'absolute',
              top: 8,
              left: 8,
              background: 'rgba(0,0,0,0.7)',
              color: '#fff',
              padding: '0.15rem 0.4rem',
              borderRadius: 'var(--radius-sm)',
              fontSize: 10,
              fontWeight: 600,
            }}>A</div>
            <div style={{
              position: 'absolute',
              top: 8,
              right: 8,
              background: 'rgba(0,0,0,0.7)',
              color: '#fff',
              padding: '0.15rem 0.4rem',
              borderRadius: 'var(--radius-sm)',
              fontSize: 10,
              fontWeight: 600,
            }}>B</div>
          </div>
        )}
      </div>

      <style>{`
        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }
      `}</style>
    </div>
  )
}

function ComparePane({ item, label }: { item: DuplicateItemDto; label: string }) {
  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      height: '100%',
      background: 'rgba(255,255,255,0.03)',
      borderRadius: 'var(--radius-lg)',
      overflow: 'hidden',
      border: '1px solid rgba(255,255,255,0.08)',
    }}>
      <div style={{
        padding: '0.4rem 0.65rem',
        borderBottom: '1px solid rgba(255,255,255,0.08)',
        display: 'flex',
        alignItems: 'center',
        gap: '0.5rem',
        flexShrink: 0,
      }}>
        <span style={{
          background: 'rgba(255,255,255,0.12)',
          color: '#fff',
          padding: '0.1rem 0.35rem',
          borderRadius: 'var(--radius-sm)',
          fontSize: 10,
          fontWeight: 600,
        }}>
          {label}
        </span>
        <span style={{
          fontSize: '0.75rem',
          color: 'rgba(255,255,255,0.7)',
          fontFamily: 'var(--font-mono)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}>
          {item.path.split(/[/\\]/).pop()}
        </span>
      </div>
      <div style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: '#000',
        overflow: 'hidden',
      }}>
        <img
          src={thumbnailUrl(item.path, 960, 90)}
          alt=""
          style={{
            maxWidth: '100%',
            maxHeight: '100%',
            objectFit: 'contain',
          }}
        />
      </div>
      <div style={{
        padding: '0.4rem 0.65rem',
        borderTop: '1px solid rgba(255,255,255,0.08)',
        display: 'flex',
        flexWrap: 'wrap',
        gap: '0.5rem',
        fontSize: '0.7rem',
        color: 'rgba(255,255,255,0.5)',
        fontFamily: 'var(--font-mono)',
        flexShrink: 0,
      }}>
        {item.frameSize && <span>{item.frameSize}</span>}
        <span>{item.bitRateKbs.toFixed(0)} kbps</span>
        <span>{(item.sizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
        {item.fps > 0 && <span>{item.fps.toFixed(0)} fps</span>}
      </div>
    </div>
  )
}
