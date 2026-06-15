import { Outlet } from 'react-router-dom'
import { useState } from 'react'
import { TitleBar } from './TitleBar'
import { ToolBar } from './ToolBar'
import { Sidebar } from './Sidebar'
import { StatusBar } from './StatusBar'

export function MainLayout({ children }: { children?: React.ReactNode }) {
  const [showFilterBar, setShowFilterBar] = useState(false)

  return (
    <div style={{
      width: '100vw',
      height: '100vh',
      display: 'flex',
      flexDirection: 'column',
      border: '1px solid var(--border-window)',
      borderRadius: 8,
      overflow: 'hidden',
      boxShadow: '0 24px 80px rgba(0,0,0,0.6), 0 0 1px rgba(0,0,0,0.5)',
      background: 'var(--bg-content)',
    }}>
      {/* Window Title Bar */}
      <TitleBar />

      {/* Toolbar / Menu Bar */}
      <ToolBar
        showFilterBar={showFilterBar}
        onToggleFilter={() => setShowFilterBar(!showFilterBar)}
      />

      {/* Main area: Sidebar + Content */}
      <div style={{
        flex: 1,
        display: 'flex',
        overflow: 'hidden',
      }}>
        {/* Left Sidebar Navigation */}
        <Sidebar />

        {/* Content Area */}
        <main style={{
          flex: 1,
          overflow: 'auto',
          background: 'var(--bg-content)',
          padding: '1rem 1.25rem',
        }}>
          {children || <Outlet />}
        </main>
      </div>

      {/* Status Bar */}
      <StatusBar />
    </div>
  )
}
