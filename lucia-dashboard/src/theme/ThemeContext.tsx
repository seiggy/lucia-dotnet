import { createContext, useContext, useEffect, useState, useSyncExternalStore } from 'react'
import type { ReactNode } from 'react'

export type ThemePreference = 'system' | 'light' | 'dark'

interface ThemeContextValue {
  preference: ThemePreference
  setPreference: (preference: ThemePreference) => void
}

const STORAGE_KEY = 'lucia-theme'
const DARK_MODE_QUERY = '(prefers-color-scheme: dark)'
const ThemeContext = createContext<ThemeContextValue | null>(null)

function getStoredPreference(): ThemePreference {
  const preference = localStorage.getItem(STORAGE_KEY)
  return preference === 'light' || preference === 'dark' || preference === 'system'
    ? preference
    : 'system'
}

function subscribeToSystemTheme(onChange: () => void): () => void {
  const mediaQuery = matchMedia(DARK_MODE_QUERY)
  mediaQuery.addEventListener('change', onChange)
  return () => mediaQuery.removeEventListener('change', onChange)
}

function getSystemTheme(): boolean {
  return matchMedia(DARK_MODE_QUERY).matches
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreference] = useState<ThemePreference>(getStoredPreference)
  const systemIsDark = useSyncExternalStore(subscribeToSystemTheme, getSystemTheme, () => false)

  useEffect(() => {
    const theme = preference === 'system' ? (systemIsDark ? 'dark' : 'light') : preference
    document.documentElement.dataset.theme = theme
    document.documentElement.style.colorScheme = theme
    localStorage.setItem(STORAGE_KEY, preference)
  }, [preference, systemIsDark])

  return (
    <ThemeContext.Provider value={{ preference, setPreference }}>
      {children}
    </ThemeContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useTheme must be used within ThemeProvider')
  return context
}