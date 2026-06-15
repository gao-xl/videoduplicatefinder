import type { CSSProperties, ReactNode } from 'react'

interface BadgeProps {
  children: ReactNode
  variant?: 'best' | 'clip' | 'success' | 'warning' | 'danger' | 'default'
  style?: CSSProperties
}

const variantStyles: Record<string, CSSProperties> = {
  best: {
    background: 'var(--badge-best-bg)',
    color: '#fff',
    boxShadow: '0 0 8px rgba(14, 165, 233, 0.3)',
  },
  clip: {
    background: 'var(--badge-clip-bg)',
    color: '#fff',
    boxShadow: '0 0 8px rgba(139, 92, 246, 0.3)',
  },
  success: {
    background: 'var(--accent-success-bg)',
    color: 'var(--accent-success-text)',
    border: '1px solid var(--accent-success-border)',
  },
  warning: {
    background: 'rgba(245, 158, 11, 0.12)',
    color: 'var(--accent-warning)',
    border: '1px solid rgba(245, 158, 11, 0.3)',
  },
  danger: {
    background: 'var(--accent-error-bg)',
    color: 'var(--accent-danger-text)',
    border: '1px solid var(--accent-error-border)',
  },
  default: {
    background: 'var(--bg-hover)',
    color: 'var(--text-muted)',
    border: '1px solid var(--border-subtle)',
  },
}

export function Badge({ children, variant = 'default', style }: BadgeProps) {
  return (
    <span style={{
      display: 'inline-flex',
      alignItems: 'center',
      padding: '0.15rem 0.5rem',
      borderRadius: 'var(--radius-sm)',
      fontSize: 10,
      fontWeight: 700,
      letterSpacing: '0.04em',
      lineHeight: 1.4,
      whiteSpace: 'nowrap',
      fontFamily: 'var(--font-display)',
      textTransform: 'uppercase',
      ...variantStyles[variant],
      ...style,
    }}>
      {children}
    </span>
  )
}
