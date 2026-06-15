import { lazy, Suspense, useEffect, useState, type ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from './contexts/ThemeProvider'
import { MainLayout } from './components/Layout/MainLayout'
import { LoginPage } from './pages/LoginPage'
import { Spinner } from './components/shared/Spinner'
import { checkAuth } from './api/auth'

const ScanPage = lazy(() => import('./pages/ScanPage').then(m => ({ default: m.ScanPage })))
const ResultsPage = lazy(() => import('./pages/ResultsPage').then(m => ({ default: m.ResultsPage })))
const SettingsPage = lazy(() => import('./pages/SettingsPage').then(m => ({ default: m.SettingsPage })))

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
})

function LazyLoader({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '4rem',
        color: 'var(--text-muted)',
        gap: '0.75rem',
      }}>
        <Spinner size={20} />
        Loading...
      </div>
    }>
      {children}
    </Suspense>
  )
}

function AuthGuard({ children }: { children: ReactNode }) {
  const location = useLocation()
  const [checking, setChecking] = useState(true)
  const [authenticated, setAuthenticated] = useState(false)

  useEffect(() => {
    const token = localStorage.getItem('vdf-access-token')
    if (!token) {
      setChecking(false)
      return
    }
    checkAuth()
      .then(res => setAuthenticated(res.authenticated))
      .catch(() => setAuthenticated(false))
      .finally(() => setChecking(false))
  }, [])

  if (checking) {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100vh',
        gap: '0.75rem',
        color: 'var(--text-muted)',
      }}>
        <Spinner size={20} />
      </div>
    )
  }

  if (!authenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <>{children}</>
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route element={
              <AuthGuard>
                <MainLayout />
              </AuthGuard>
            }>
              <Route path="/" element={<LazyLoader><ScanPage /></LazyLoader>} />
              <Route path="/results" element={<LazyLoader><ResultsPage /></LazyLoader>} />
              <Route path="/settings" element={<LazyLoader><SettingsPage /></LazyLoader>} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </ThemeProvider>
    </QueryClientProvider>
  )
}

export default App
