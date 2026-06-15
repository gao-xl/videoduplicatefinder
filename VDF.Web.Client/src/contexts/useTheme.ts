import { useContext } from 'react'
import { ThemeContext, type Theme } from './ThemeContext'

export type { Theme }

export function useTheme(): { theme: Theme; toggleTheme: () => void } {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider')
  return ctx
}
