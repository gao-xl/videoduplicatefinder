import { type ReactNode, useState, useEffect, useCallback } from 'react'

interface ConfirmDialogProps {
  open: boolean
  title: string
  message: string | ReactNode
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'warning' | 'default'
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  variant = 'default',
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    if (open) {
      setVisible(true)
    } else {
      const t = setTimeout(() => setVisible(false), 200)
      return () => clearTimeout(t)
    }
  }, [open])

  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape') onCancel()
  }, [onCancel])

  useEffect(() => {
    if (open) {
      document.addEventListener('keydown', handleKeyDown)
      return () => document.removeEventListener('keydown', handleKeyDown)
    }
  }, [open, handleKeyDown])

  if (!visible && !open) return null

  const confirmBg = variant === 'danger' ? 'var(--accent-danger-hover)'
    : variant === 'warning' ? 'var(--accent-warning)'
    : 'var(--accent-primary)'

  return (
    <div style={{
      position: 'fixed',
      inset: 0,
      zIndex: 1000,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: 'rgba(0,0,0,0.7)',
      backdropFilter: 'blur(8px)',
      opacity: open ? 1 : 0,
      transition: 'opacity 0.2s ease',
    }}
      onClick={onCancel}
    >
      <div
        style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--border-default)',
          borderRadius: 'var(--radius-xl)',
          padding: '1.75rem',
          maxWidth: 420,
          width: '90%',
          boxShadow: 'var(--shadow-lg)',
          transform: open ? 'scale(1)' : 'scale(0.95)',
          transition: 'transform 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
        }}
        onClick={e => e.stopPropagation()}
      >
        <h3 style={{
          fontFamily: 'var(--font-display)',
          fontSize: '1.1rem',
          fontWeight: 700,
          marginBottom: '0.75rem',
          color: variant === 'danger' ? 'var(--accent-danger-text)' : 'var(--text-primary)',
        }}>
          {title}
        </h3>
        <div style={{
          color: 'var(--text-secondary)',
          fontSize: '0.9rem',
          lineHeight: 1.6,
          marginBottom: '1.5rem',
        }}>
          {message}
        </div>
        <div style={{
          display: 'flex',
          gap: '0.5rem',
          justifyContent: 'flex-end',
        }}>
          <button
            onClick={onCancel}
            style={{
              padding: '0.5rem 1.1rem',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-button)',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '0.85rem',
              fontFamily: 'var(--font-sans)',
              transition: 'background var(--transition-fast)',
            }}
          >
            {cancelLabel}
          </button>
          <button
            onClick={onConfirm}
            style={{
              padding: '0.5rem 1.1rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: confirmBg,
              color: '#fff',
              cursor: 'pointer',
              fontSize: '0.85rem',
              fontWeight: 600,
              fontFamily: 'var(--font-sans)',
              transition: 'opacity var(--transition-fast)',
            }}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
