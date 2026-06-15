import { Outlet } from 'react-router-dom'
import { TopBar } from './TopBar'

export function MainLayout() {
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        minHeight: '100vh',
        background: 'var(--bg-body)',
      }}
    >
      <TopBar />
      <main
        style={{
          flex: 1,
          padding: '2rem 2.5rem',
          maxWidth: 1440,
          width: '100%',
          margin: '0 auto',
          overflowY: 'auto',
          animation: 'fadeIn 0.3s ease',
        }}
      >
        <Outlet />
      </main>
    </div>
  )
}
