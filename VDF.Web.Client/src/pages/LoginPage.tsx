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
  const [focused, setFocused] = useState(false)

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
      width: '100vw',
      height: '100vh',
      background: '#0c0c0e',
      overflow: 'hidden',
      position: 'relative',
    }}>
      {/* Ambient glow orbs */}
      <div style={{
        position: 'absolute',
        width: 500,
        height: 500,
        top: '-10%',
        left: '30%',
        borderRadius: '50%',
        background: 'radial-gradient(circle, rgba(10,132,255,0.08) 0%, transparent 60%)',
        filter: 'blur(60px)',
        pointerEvents: 'none',
      }} />
      <div style={{
        position: 'absolute',
        width: 400,
        height: 400,
        bottom: '-5%',
        right: '20%',
        borderRadius: '50%',
        background: 'radial-gradient(circle, rgba(191,90,242,0.05) 0%, transparent 60%)',
        filter: 'blur(50px)',
        pointerEvents: 'none',
      }} />
      <div style={{
        position: 'absolute',
        width: 350,
        height: 350,
        top: '50%',
        left: '-5%',
        borderRadius: '50%',
        background: 'radial-gradient(circle, rgba(48,209,88,0.03) 0%, transparent 60%)',
        filter: 'blur(50px)',
        pointerEvents: 'none',
      }} />

      {/* Subtle noise texture */}
      <div style={{
        position: 'absolute',
        inset: 0,
        opacity: 0.025,
        backgroundImage: `url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E")`,
        pointerEvents: 'none',
      }} />

      {/* Login card */}
      <div style={{
        width: 340,
        position: 'relative',
        zIndex: 1,
        animation: 'loginCardIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) both',
      }}>
        {/* Window chrome */}
        <div style={{
          borderRadius: '12px 12px 0 0',
          background: 'rgba(30, 30, 32, 0.85)',
          backdropFilter: 'blur(40px) saturate(180%)',
          WebkitBackdropFilter: 'blur(40px) saturate(180%)',
          height: 'var(--titlebar-height)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          position: 'relative',
          borderBottom: '1px solid rgba(255,255,255,0.06)',
          userSelect: 'none',
        }}>
          {/* Traffic lights */}
          <div style={{ position: 'absolute', left: 14, display: 'flex', gap: 7 }}>
            <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#ff5f57', border: '0.5px solid rgba(0,0,0,0.12)' }} />
            <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#febc2e', border: '0.5px solid rgba(0,0,0,0.12)' }} />
            <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#28c840', border: '0.5px solid rgba(0,0,0,0.12)' }} />
          </div>
          <span style={{
            fontSize: 12,
            fontWeight: 500,
            color: 'rgba(255,255,255,0.5)',
            fontFamily: 'var(--font-sans)',
            letterSpacing: '0.01em',
          }}>
            Authentication Required
          </span>
        </div>

        {/* Card body */}
        <div style={{
          background: 'rgba(24, 24, 28, 0.92)',
          backdropFilter: 'blur(40px) saturate(180%)',
          WebkitBackdropFilter: 'blur(40px) saturate(180%)',
          borderRadius: '0 0 12px 12px',
          padding: '2rem 2rem 1.5rem',
          border: '1px solid rgba(255,255,255,0.06)',
          borderTop: 'none',
          boxShadow: '0 32px 64px rgba(0,0,0,0.5), 0 8px 16px rgba(0,0,0,0.3), inset 0 1px 0 rgba(255,255,255,0.04)',
        }}>
          {/* App icon */}
          <div style={{
            display: 'flex',
            justifyContent: 'center',
            marginBottom: '1.25rem',
            animation: 'loginIconIn 0.8s cubic-bezier(0.16, 1, 0.3, 1) 0.1s both',
          }}>
            <div style={{
              width: 72,
              height: 72,
              borderRadius: 18,
              background: 'linear-gradient(145deg, #1a1a2e, #16213e)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              position: 'relative',
              boxShadow: '0 8px 24px rgba(0,0,0,0.4), 0 2px 8px rgba(0,0,0,0.2), inset 0 1px 0 rgba(255,255,255,0.06)',
            }}>
              {/* Inner glow ring */}
              <div style={{
                position: 'absolute',
                inset: -1,
                borderRadius: 19,
                border: '1px solid rgba(255,255,255,0.08)',
                pointerEvents: 'none',
              }} />
              <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="url(#iconGrad)" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <defs>
                  <linearGradient id="iconGrad" x1="2" y1="2" x2="22" y2="22">
                    <stop offset="0%" stopColor="#5ac8fa" />
                    <stop offset="50%" stopColor="#0a84ff" />
                    <stop offset="100%" stopColor="#bf5af2" />
                  </linearGradient>
                </defs>
                <rect x="2" y="2" width="20" height="20" rx="2.18" ry="2.18" />
                <line x1="7" y1="2" x2="7" y2="22" />
                <line x1="17" y1="2" x2="17" y2="22" />
                <line x1="2" y1="12" x2="22" y2="12" />
              </svg>
            </div>
          </div>

          {/* App name */}
          <div style={{
            textAlign: 'center',
            marginBottom: '1.5rem',
            animation: 'loginTextIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.2s both',
          }}>
            <h1 style={{
              fontSize: '1.15rem',
              fontWeight: 600,
              color: 'rgba(255,255,255,0.92)',
              margin: 0,
              fontFamily: 'var(--font-display)',
              letterSpacing: '-0.01em',
            }}>
              Video Duplicate Finder
            </h1>
            <p style={{
              color: 'rgba(255,255,255,0.35)',
              fontSize: 12,
              marginTop: '0.35rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 400,
            }}>
              Enter your password to unlock
            </p>
          </div>

          <form onSubmit={handleSubmit}>
            {/* Password field with lock icon */}
            <div style={{
              marginBottom: '1rem',
              animation: 'loginTextIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.3s both',
            }}>
              <div style={{
                position: 'relative',
                display: 'flex',
                alignItems: 'center',
              }}>
                {/* Lock icon */}
                <div style={{
                  position: 'absolute',
                  left: 12,
                  display: 'flex',
                  alignItems: 'center',
                  pointerEvents: 'none',
                  color: focused ? 'rgba(10,132,255,0.7)' : 'rgba(255,255,255,0.2)',
                  transition: 'color 0.25s ease',
                  zIndex: 1,
                }}>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                    <path d="M7 11V7a5 5 0 0 1 10 0v4" />
                  </svg>
                </div>
                <input
                  type="password"
                  value={password}
                  onChange={e => { setPassword(e.target.value); setError(null) }}
                  autoFocus
                  placeholder="Password"
                  style={{
                    width: '100%',
                    padding: '0.7rem 0.85rem 0.7rem 2.4rem',
                    border: `1px solid ${
                      error ? 'rgba(255,69,58,0.5)'
                      : focused ? 'rgba(10,132,255,0.4)'
                      : 'rgba(255,255,255,0.08)'
                    }`,
                    borderRadius: 10,
                    background: error
                      ? 'rgba(255,69,58,0.06)'
                      : 'rgba(255,255,255,0.04)',
                    color: 'rgba(255,255,255,0.9)',
                    fontSize: 13,
                    fontFamily: 'var(--font-sans)',
                    outline: 'none',
                    transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
                    boxShadow: focused
                      ? '0 0 0 3px rgba(10,132,255,0.12), 0 2px 8px rgba(0,0,0,0.2)'
                      : '0 1px 4px rgba(0,0,0,0.15)',
                    letterSpacing: password ? '0.15em' : 'normal',
                  }}
                  onFocus={() => setFocused(true)}
                  onBlur={() => setFocused(false)}
                />
                {/* Eye indicator when typing */}
                {password && (
                  <div style={{
                    position: 'absolute',
                    right: 12,
                    color: 'rgba(255,255,255,0.15)',
                    pointerEvents: 'none',
                  }}>
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  </div>
                )}
              </div>
            </div>

            {/* Remember me */}
            <div style={{
              marginBottom: '1.25rem',
              animation: 'loginTextIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.35s both',
            }}>
              <label
                onClick={() => setRemember(!remember)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.45rem',
                  color: 'rgba(255,255,255,0.4)',
                  fontSize: 11,
                  cursor: 'pointer',
                  userSelect: 'none',
                  fontFamily: 'var(--font-sans)',
                  transition: 'color 0.2s ease',
                }}
                onMouseEnter={e => (e.currentTarget.style.color = 'rgba(255,255,255,0.6)')}
                onMouseLeave={e => (e.currentTarget.style.color = 'rgba(255,255,255,0.4)')}
              >
                <div style={{
                  width: 15,
                  height: 15,
                  borderRadius: 4,
                  border: `1.5px solid ${remember ? 'var(--accent-primary)' : 'rgba(255,255,255,0.15)'}`,
                  background: remember ? 'var(--accent-primary)' : 'transparent',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  transition: 'all 0.2s ease',
                  flexShrink: 0,
                }}>
                  {remember && (
                    <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                  )}
                </div>
                Remember me
              </label>
            </div>

            {/* Error message */}
            {error && (
              <div style={{
                color: '#ff6961',
                background: 'rgba(255,69,58,0.08)',
                border: '1px solid rgba(255,69,58,0.2)',
                borderRadius: 8,
                padding: '0.55rem 0.75rem',
                fontSize: 12,
                marginBottom: '1rem',
                animation: 'loginShake 0.4s cubic-bezier(0.36, 0.07, 0.19, 0.97)',
                display: 'flex',
                alignItems: 'center',
                gap: '0.4rem',
                fontFamily: 'var(--font-sans)',
              }}>
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="10" />
                  <line x1="12" y1="8" x2="12" y2="12" />
                  <line x1="12" y1="16" x2="12.01" y2="16" />
                </svg>
                {error}
              </div>
            )}

            {/* Submit button */}
            <div style={{
              animation: 'loginTextIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.4s both',
            }}>
              <button
                type="submit"
                disabled={loading || !password}
                className="login-submit-btn"
                style={{
                  width: '100%',
                  padding: '0.65rem',
                  fontSize: 13,
                  fontWeight: 600,
                  background: loading || !password
                    ? 'rgba(255,255,255,0.05)'
                    : 'linear-gradient(180deg, #0a84ff 0%, #0066cc 100%)',
                  border: loading || !password
                    ? '1px solid rgba(255,255,255,0.06)'
                    : '1px solid rgba(10,132,255,0.3)',
                  color: loading || !password ? 'rgba(255,255,255,0.2)' : '#fff',
                  borderRadius: 10,
                  cursor: loading || !password ? 'not-allowed' : 'pointer',
                  fontFamily: 'var(--font-sans)',
                  letterSpacing: '0.01em',
                  transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: '0.5rem',
                  boxShadow: loading || !password
                    ? 'none'
                    : '0 2px 8px rgba(10,132,255,0.25), 0 0 1px rgba(10,132,255,0.5)',
                }}
                onMouseEnter={e => {
                  if (!loading && password) {
                    e.currentTarget.style.boxShadow = '0 4px 16px rgba(10,132,255,0.35), 0 0 1px rgba(10,132,255,0.6)'
                    e.currentTarget.style.background = 'linear-gradient(180deg, #409cff 0%, #0a84ff 100%)'
                    e.currentTarget.style.transform = 'translateY(-1px)'
                  }
                }}
                onMouseLeave={e => {
                  if (!loading && password) {
                    e.currentTarget.style.boxShadow = '0 2px 8px rgba(10,132,255,0.25), 0 0 1px rgba(10,132,255,0.5)'
                    e.currentTarget.style.background = 'linear-gradient(180deg, #0a84ff 0%, #0066cc 100%)'
                    e.currentTarget.style.transform = 'translateY(0)'
                  }
                }}
                onMouseDown={e => {
                  if (!loading && password) e.currentTarget.style.transform = 'scale(0.98)'
                }}
                onMouseUp={e => {
                  if (!loading && password) e.currentTarget.style.transform = 'translateY(-1px)'
                }}
              >
                {loading && <Spinner size={12} />}
                {loading ? 'Signing in...' : 'Sign In'}
              </button>
            </div>
          </form>
        </div>

        {/* Footer hint */}
        <div style={{
          textAlign: 'center',
          marginTop: '1rem',
          animation: 'loginTextIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) 0.5s both',
        }}>
          <p style={{
            color: 'rgba(255,255,255,0.18)',
            fontSize: 10,
            lineHeight: 1.7,
            fontFamily: 'var(--font-sans)',
          }}>
            Password was printed to console when the app started.
          </p>
          <p style={{
            color: 'rgba(255,255,255,0.18)',
            fontSize: 10,
            fontFamily: 'var(--font-sans)',
            marginTop: 2,
          }}>
            Docker users:{' '}
            <code style={{
              background: 'rgba(255,255,255,0.04)',
              padding: '0.1rem 0.35rem',
              borderRadius: 4,
              fontFamily: 'var(--font-mono)',
              fontSize: 10,
              border: '1px solid rgba(255,255,255,0.06)',
            }}>docker logs vdf</code>
          </p>
        </div>
      </div>

      <style>{`
        @keyframes loginCardIn {
          from {
            opacity: 0;
            transform: translateY(20px) scale(0.96);
          }
          to {
            opacity: 1;
            transform: translateY(0) scale(1);
          }
        }
        @keyframes loginIconIn {
          from {
            opacity: 0;
            transform: translateY(8px) scale(0.9);
          }
          to {
            opacity: 1;
            transform: translateY(0) scale(1);
          }
        }
        @keyframes loginTextIn {
          from {
            opacity: 0;
            transform: translateY(6px);
          }
          to {
            opacity: 1;
            transform: translateY(0);
          }
        }
        @keyframes loginShake {
          10%, 90% { transform: translateX(-1px); }
          20%, 80% { transform: translateX(2px); }
          30%, 50%, 70% { transform: translateX(-3px); }
          40%, 60% { transform: translateX(3px); }
        }
        .login-submit-btn:focus-visible {
          outline: 2px solid rgba(10,132,255,0.5);
          outline-offset: 2px;
        }
      `}</style>
    </div>
  )
}
