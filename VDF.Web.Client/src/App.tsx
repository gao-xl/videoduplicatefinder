import { lazy, Suspense, useEffect, useState, type ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from './contexts/ThemeProvider'
import { ScanStateProvider } from './contexts/ScanStateContext'
import { I18nProvider } from './i18n/I18nProvider'
import type { LanguageCode } from './i18n/i18n'
import { ErrorBoundary } from './components/ErrorBoundary'
import { MainLayout } from './components/Layout/MainLayout'
import { LoginPage } from './pages/LoginPage'
import { Spinner } from './components/shared/Spinner'
import { WelcomeGuide } from './components/WelcomeGuide'
import { checkAuth } from './api/auth'
import { getSettings } from './api/settings'

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
  const savedLang = localStorage.getItem('vdf-lang') as LanguageCode | null
  const [showGuide, setShowGuide] = useState(false)
  const [guideChecked, setGuideChecked] = useState(false)

  useEffect(() => {
    const hasSeenGuide = localStorage.getItem('vdf-has-seen-guide')
    if (hasSeenGuide) {
      setGuideChecked(true)
      return
    }

    getSettings()
      .then(settings => {
        if (settings.showWelcomeGuide !== false) {
          setShowGuide(true)
        }
        setGuideChecked(true)
      })
      .catch(() => {
        setGuideChecked(true)
      })
  }, [])

  const handleGuideComplete = () => {
    localStorage.setItem('vdf-has-seen-guide', 'true')
    setShowGuide(false)
  }

  if (!guideChecked) {
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
        Loading...
      </div>
    )
  }

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <ScanStateProvider>
            <I18nProvider initialLang={savedLang || 'zh-Hans'}>
              <BrowserRouter>
              <Routes>
                <Route path="/login" element={<LoginPage />} />
                <Route element={
                  <AuthGuard>
                    {showGuide ? (
                      <MainLayout>
                        <WelcomeGuide onComplete={handleGuideComplete} />
                      </MainLayout>
                    ) : (
                      <MainLayout />
                    )}
                  </AuthGuard>
                }>
                  <Route path="/" element={<LazyLoader><ScanPage /></LazyLoader>} />
                  <Route path="/results" element={<LazyLoader><ResultsPage /></LazyLoader>} />
                  <Route path="/settings" element={<LazyLoader><SettingsPage /></LazyLoader>} />
                </Route>
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </BrowserRouter>
            </I18nProvider>
          </ScanStateProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </ErrorBoundary>
  )
}

export default App
