import { type CSSProperties } from 'react'

interface ProgressBarProps {
  value: number
  max: number
  label?: string
  showPercent?: boolean
  height?: number
  animated?: boolean
  style?: CSSProperties
}

export function ProgressBar({ value, max, label, showPercent = true, height = 6, animated = true, style }: ProgressBarProps) {
  const pct = max > 0 ? Math.min(100, (value / max) * 100) : 0

  return (
    <div style={{ width: '100%', ...style }}>
      {(label || showPercent) && (
        <div style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 6,
          fontSize: 12,
          color: 'var(--text-muted)',
        }}>
          {label && <span>{label}</span>}
          {showPercent && (
            <span style={{
              fontFamily: 'var(--font-mono)',
              fontSize: 11,
              color: 'var(--accent-primary)',
              fontWeight: 600,
            }}>
              {pct.toFixed(1)}%
            </span>
          )}
        </div>
      )}
      <div style={{
        width: '100%',
        height,
        background: 'var(--progress-track)',
        borderRadius: height / 2,
        overflow: 'hidden',
        position: 'relative',
      }}>
        <div style={{
          width: `${pct}%`,
          height: '100%',
          background: 'var(--progress-fill-gradient)',
          borderRadius: height / 2,
          transition: animated ? 'width 0.5s cubic-bezier(0.4, 0, 0.2, 1)' : 'none',
          position: 'relative',
          ...(pct > 0 && pct < 100 ? {
            backgroundImage: 'linear-gradient(90deg, transparent 0%, rgba(255,255,255,0.15) 50%, transparent 100%)',
            backgroundSize: '200% 100%',
            animation: 'shimmer 2s infinite',
          } : {}),
        }} />
        {/* Glow effect on active progress */}
        {pct > 0 && pct < 100 && (
          <div style={{
            position: 'absolute',
            right: 0,
            top: -2,
            bottom: -2,
            width: 20,
            background: 'radial-gradient(ellipse at right, var(--accent-primary-glow), transparent)',
            borderRadius: '50%',
            pointerEvents: 'none',
          }} />
        )}
      </div>
    </div>
  )
}
