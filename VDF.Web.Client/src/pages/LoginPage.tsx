import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { login } from '../api/auth'
import { ApiError } from '../api/client'
import { Spinner } from '../components/shared/Spinner'

export function LoginPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const returnUrl = searchParams.get('returnUrl') || '/'

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await login({ password, remember })
      navigate(returnUrl, { replace: true })
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setError('Invalid password')
      } else {
        setError('Login failed. Please try again.')
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: '100vh',
      background: 'var(--bg-body)',
      padding: '1rem',
      position: 'relative',
      overflow: 'hidden',
    }}>
      {/* Grid / scanline overlay */}
      <div style={{
        position: 'absolute',
        inset: 0,
        backgroundImage:
          'linear-gradient(rgba(14,165,233,0.03) 1px, transparent 1px), linear-gradient(90deg, rgba(14,165,233,0.03) 1px, transparent 1px)',
        backgroundSize: '60px 60px',
        pointerEvents: 'none',
        zIndex: 0,
      }} />

      {/* Scanline effect */}
      <div style={{
        position: 'absolute',
        inset: 0,
        backgroundImage: 'repeating-linear-gradient(0deg, transparent, transparent 2px, rgba(0,0,0,0.04) 2px, rgba(0,0,0,0.04) 4px)',
        pointerEvents: 'none',
        zIndex: 0,
      }} />

      {/* Primary gradient orb - cyan */}
      <div style={{
        position: 'absolute',
        top: '-15%',
        left: '50%',
        transform: 'translateX(-50%)',
        width: 800,
        height: 500,
        background: 'radial-gradient(ellipse, rgba(14,165,233,0.12) 0%, transparent 65%)',
        pointerEvents: 'none',
        zIndex: 0,
      }} />

      {/* Secondary gradient orb - purple, offset */}
      <div style={{
        position: 'absolute',
        bottom: '-10%',
        right: '-5%',
        width: 600,
        height: 400,
        background: 'radial-gradient(ellipse, rgba(139,92,246,0.08) 0%, transparent 65%)',
        pointerEvents: 'none',
        zIndex: 0,
      }} />

      {/* Tertiary gradient orb - teal, left */}
      <div style={{
        position: 'absolute',
        bottom: '5%',
        left: '-8%',
        width: 500,
        height: 350,
        background: 'radial-gradient(ellipse, rgba(20,184,166,0.06) 0%, transparent 65%)',
        pointerEvents: 'none',
        zIndex: 0,
      }} />

      <div style={{
        animation: 'fadeInUp2 0.6s ease both',
        position: 'relative',
        zIndex: 1,
      }}>
        <div style={{
          background: 'rgba(15, 17, 23, 0.7)',
          backdropFilter: 'blur(24px)',
          WebkitBackdropFilter: 'blur(24px)',
          border: '1px solid rgba(14,165,233,0.12)',
          borderRadius: 'var(--radius-xl)',
          padding: '3rem 2.5rem 2.5rem',
          maxWidth: 420,
          width: '100%',
          boxShadow: 'var(--shadow-lg), 0 0 60px rgba(14,165,233,0.06), inset 0 1px 0 rgba(255,255,255,0.03)',
          position: 'relative',
          overflow: 'hidden',
        }}>
          {/* Top flowing gradient accent line */}
          <div style={{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            height: 2,
            background: 'linear-gradient(90deg, transparent 0%, #0ea5e9 25%, #8b5cf6 50%, #0ea5e9 75%, transparent 100%)',
            backgroundSize: '200% 100%',
            animation: 'gradientFlow 3s linear infinite',
          }} />

          {/* Subtle inner glow at top */}
          <div style={{
            position: 'absolute',
            top: 0,
            left: '20%',
            right: '20%',
            height: 80,
            background: 'radial-gradient(ellipse at top, rgba(14,165,233,0.06) 0%, transparent 70%)',
            pointerEvents: 'none',
          }} />

          {/* Logo */}
          <div style={{ textAlign: 'center', marginBottom: '2rem' }}>
            <div style={{
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: 64,
              height: 64,
              borderRadius: 'var(--radius-lg)',
              background: 'linear-gradient(135deg, #0ea5e9, #6366f1)',
              marginBottom: '1.25rem',
              boxShadow: '0 4px 24px rgba(14,165,233,0.3), 0 0 40px rgba(14,165,233,0.1)',
              animation: 'glowPulse 3s ease-in-out infinite',
              position: 'relative',
            }}>
              <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <rect x="2" y="2" width="20" height="20" rx="2.18" ry="2.18" />
                <line x1="7" y1="2" x2="7" y2="22" />
                <line x1="17" y1="2" x2="17" y2="22" />
                <line x1="2" y1="12" x2="22" y2="12" />
                <line x1="2" y1="7" x2="7" y2="7" />
                <line x1="2" y1="17" x2="7" y2="17" />
                <line x1="17" y1="7" x2="22" y2="7" />
                <line x1="17" y1="17" x2="22" y2="17" />
              </svg>
            </div>
            <h1 style={{
              fontFamily: 'var(--font-display)',
              fontSize: '1.5rem',
              fontWeight: 700,
              color: 'var(--text-primary)',
              letterSpacing: '0.04em',
              margin: 0,
              textTransform: 'uppercase',
            }}>
              Video Duplicate Finder
            </h1>
            <p style={{
              color: 'var(--text-muted)',
              fontSize: '0.8rem',
              marginTop: '0.4rem',
              lineHeight: 1.5,
              fontFamily: 'var(--font-sans)',
              letterSpacing: '0.02em',
            }}>
              Enter password to continue
            </p>
          </div>

          <form onSubmit={handleSubmit}>
            <div style={{ marginBottom: '1rem' }}>
              <label
                htmlFor="password"
                style={{
                  display: 'block',
                  marginBottom: '0.4rem',
                  color: 'var(--text-secondary)',
                  fontSize: '0.75rem',
                  fontWeight: 600,
                  fontFamily: 'var(--font-sans)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.08em',
                }}
              >
                Password
              </label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                autoFocus
                placeholder="Enter password..."
                style={{
                  width: '100%',
                  padding: '0.7rem 0.85rem',
                  border: `1px solid ${error ? 'var(--input-invalid)' : 'var(--border-input)'}`,
                  borderRadius: 'var(--radius-md)',
                  background: 'var(--bg-input)',
                  color: 'var(--text-primary)',
                  fontSize: '0.95rem',
                  fontFamily: 'var(--font-sans)',
                  transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
                  outline: 'none',
                  boxShadow: error ? '0 0 12px rgba(220,38,38,0.15)' : 'none',
                }}
                onFocus={e => {
                  if (!error) {
                    e.currentTarget.style.borderColor = 'var(--accent-primary)'
                    e.currentTarget.style.boxShadow = '0 0 0 3px var(--accent-primary-glow), 0 0 20px rgba(14,165,233,0.08)'
                  }
                }}
                onBlur={e => {
                  if (!error) {
                    e.currentTarget.style.borderColor = 'var(--border-input)'
                    e.currentTarget.style.boxShadow = 'none'
                  }
                }}
              />
            </div>

            <div style={{ marginBottom: '1.25rem' }}>
              <label style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.5rem',
                color: 'var(--text-muted)',
                fontSize: '0.8rem',
                cursor: 'pointer',
                userSelect: 'none',
                fontFamily: 'var(--font-sans)',
              }}>
                <input
                  type="checkbox"
                  checked={remember}
                  onChange={e => setRemember(e.target.checked)}
                  style={{ accentColor: 'var(--accent-primary)' }}
                />
                Remember me
              </label>
            </div>

            {error && (
              <div style={{
                color: 'var(--accent-danger-text)',
                background: 'var(--accent-error-bg)',
                border: '1px solid var(--accent-error-border)',
                borderRadius: 'var(--radius-md)',
                padding: '0.55rem 0.85rem',
                fontSize: '0.8rem',
                marginBottom: '1rem',
                animation: 'shake 0.3s ease',
                boxShadow: '0 0 16px rgba(220,38,38,0.1)',
                fontFamily: 'var(--font-sans)',
              }}>
                {error}
              </div>
            )}

            <button
              type="submit"
              disabled={loading || !password}
              style={{
                width: '100%',
                padding: '0.7rem',
                fontSize: '0.9rem',
                fontWeight: 600,
                background: loading || !password
                  ? 'var(--bg-button)'
                  : 'linear-gradient(135deg, #0ea5e9, #0284c7)',
                border: loading || !password ? '1px solid var(--border-default)' : 'none',
                color: loading || !password ? 'var(--text-dim)' : '#fff',
                borderRadius: 'var(--radius-md)',
                cursor: loading || !password ? 'not-allowed' : 'pointer',
                fontFamily: 'var(--font-sans)',
                letterSpacing: '0.03em',
                transition: 'background var(--transition-fast), box-shadow var(--transition-fast), transform var(--transition-fast)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '0.5rem',
                boxShadow: loading || !password ? 'none' : '0 2px 12px rgba(14,165,233,0.25)',
              }}
              onMouseEnter={e => {
                if (!loading && password) {
                  e.currentTarget.style.boxShadow = '0 4px 24px rgba(14,165,233,0.4), 0 0 40px rgba(14,165,233,0.15)'
                  e.currentTarget.style.transform = 'translateY(-1px)'
                }
              }}
              onMouseLeave={e => {
                if (!loading && password) {
                  e.currentTarget.style.boxShadow = '0 2px 12px rgba(14,165,233,0.25)'
                  e.currentTarget.style.transform = 'translateY(0)'
                }
              }}
            >
              {loading && <Spinner size={14} />}
              {loading ? 'Signing in...' : 'Sign in'}
            </button>
          </form>

          <p style={{
            textAlign: 'center',
            color: 'var(--text-dim)',
            fontSize: '0.68rem',
            marginTop: '1.5rem',
            lineHeight: 1.6,
            fontFamily: 'var(--font-sans)',
            opacity: 0.7,
          }}>
            Set via <code style={{
              background: 'rgba(14,165,233,0.08)',
              padding: '0.1rem 0.4rem',
              borderRadius: 'var(--radius-sm)',
              fontSize: '0.68rem',
              color: 'var(--text-muted)',
              fontFamily: 'var(--font-mono)',
              border: '1px solid rgba(14,165,233,0.1)',
            }}>VDF_WEB_PASSWORD</code> env var
          </p>
        </div>
      </div>
    </div>
  )
}
