export function TitleBar() {
  return (
    <div
      style={{
        height: 'var(--titlebar-height)',
        background: 'var(--bg-titlebar)',
        borderBottom: '1px solid var(--border-subtle)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
        userSelect: 'none',
        WebkitAppRegion: 'drag',
        flexShrink: 0,
      } as React.CSSProperties}
    >
      {/* Window controls placeholder (left) */}
      <div style={{
        position: 'absolute',
        left: 12,
        display: 'flex',
        gap: 6,
      }}>
        <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#ff5f57' }} />
        <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#febc2e' }} />
        <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#28c840' }} />
      </div>

      {/* Title */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: '0.4rem',
      }}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--accent-primary)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="2" y="2" width="20" height="20" rx="2.18" ry="2.18" />
          <line x1="7" y1="2" x2="7" y2="22" />
          <line x1="17" y1="2" x2="17" y2="22" />
          <line x1="2" y1="12" x2="22" y2="12" />
        </svg>
        <span style={{
          fontFamily: 'var(--font-display)',
          fontSize: 12,
          fontWeight: 600,
          color: 'var(--text-secondary)',
          letterSpacing: '0.02em',
        }}>
          Video Duplicate Finder
        </span>
      </div>
    </div>
  )
}
