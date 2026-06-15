import { NavLink } from 'react-router-dom'
import { ThemeToggle } from './ThemeToggle'

const navItems = [
  { to: '/', label: 'Scan', icon: 'scan' },
  { to: '/results', label: 'Results', icon: 'results' },
  { to: '/settings', label: 'Settings', icon: 'settings' },
]

function NavIcon({ type }: { type: string }) {
  const size = 15
  switch (type) {
    case 'scan':
      return (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="4 7 4 4 20 4 20 7" />
          <line x1="9" y1="20" x2="15" y2="20" />
          <line x1="12" y1="4" x2="12" y2="20" />
        </svg>
      )
    case 'results':
      return (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="3" y="3" width="7" height="7" rx="1" />
          <rect x="14" y="3" width="7" height="7" rx="1" />
          <rect x="3" y="14" width="7" height="7" rx="1" />
          <rect x="14" y="14" width="7" height="7" rx="1" />
        </svg>
      )
    case 'settings':
      return (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" />
        </svg>
      )
    default:
      return null
  }
}

export function TopBar() {
  return (
    <header
      style={{
        display: 'flex',
        alignItems: 'center',
        height: 56,
        padding: '0 1.25rem',
        background: 'var(--bg-header)',
        borderBottom: '1px solid var(--border-subtle)',
        position: 'sticky',
        top: 0,
        zIndex: 50,
        flexShrink: 0,
        gap: '0.75rem',
        boxShadow: 'var(--shadow-sm)',
      }}
    >
      {/* Brand */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: '0.6rem',
        flexShrink: 0,
        marginRight: '0.5rem',
      }}>
        <div style={{
          width: 30,
          height: 30,
          borderRadius: 'var(--radius-md)',
          background: 'linear-gradient(135deg, #0ea5e9, #6366f1)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          boxShadow: '0 2px 8px rgba(14, 165, 233, 0.25)',
        }}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <rect x="2" y="2" width="20" height="20" rx="2.18" ry="2.18" />
            <line x1="7" y1="2" x2="7" y2="22" />
            <line x1="17" y1="2" x2="17" y2="22" />
            <line x1="2" y1="12" x2="22" y2="12" />
          </svg>
        </div>
        <span
          style={{
            fontFamily: 'var(--font-display)',
            fontWeight: 700,
            fontSize: '1rem',
            color: 'var(--text-brand)',
            letterSpacing: '0.04em',
          }}
        >
          VDF
        </span>
      </div>

      {/* Divider */}
      <div style={{
        width: 1,
        height: 24,
        background: 'var(--border-default)',
        flexShrink: 0,
      }} />

      {/* Navigation */}
      <nav
        style={{
          display: 'flex',
          gap: '0.15rem',
          flex: 1,
        }}
      >
        {navItems.map(item => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            style={({ isActive }) => ({
              padding: '0.4rem 0.85rem',
              borderRadius: 'var(--radius-md)',
              color: isActive ? 'var(--accent-primary)' : 'var(--text-muted)',
              fontSize: 13,
              fontWeight: isActive ? 600 : 500,
              transition: 'background var(--transition-fast), color var(--transition-fast)',
              position: 'relative',
              textDecoration: 'none',
              display: 'flex',
              alignItems: 'center',
              gap: '0.4rem',
            })}
            className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}
          >
            <NavIcon type={item.icon} />
            {item.label}
          </NavLink>
        ))}
      </nav>

      {/* Right side */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', flexShrink: 0 }}>
        <ThemeToggle />
      </div>
    </header>
  )
}
