import { createContext, useContext, useState, useEffect, useCallback } from 'react'
import type { ReactNode } from 'react'
import { fetchAuthStatus, login as apiLogin, logout as apiLogout } from '../api'

interface AuthContextValue {
  authenticated: boolean
  setupComplete: boolean
  loading: boolean
  /** True when the auth status could not be read (server down, timed out, or an error response). */
  statusError: boolean
  login: (apiKey: string) => Promise<void>
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [authenticated, setAuthenticated] = useState(false)
  const [setupComplete, setSetupComplete] = useState(true)
  const [loading, setLoading] = useState(true)
  const [statusError, setStatusError] = useState(false)

  const refresh = useCallback(async () => {
    try {
      const status = await fetchAuthStatus()
      setAuthenticated(status.authenticated)
      setSetupComplete(status.setupComplete)
      setStatusError(false)
    } catch {
      // A failed read says nothing about setup state; sending an existing install to the
      // first-run wizard here would be wrong, so surface the error and let the user retry.
      setAuthenticated(false)
      setStatusError(true)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    refresh()
  }, [refresh])

  const login = useCallback(async (apiKey: string) => {
    const result = await apiLogin(apiKey)
    if (result.authenticated) {
      setAuthenticated(true)
    }
  }, [])

  const logout = useCallback(async () => {
    await apiLogout()
    setAuthenticated(false)
  }, [])

  return (
    <AuthContext.Provider value={{ authenticated, setupComplete, loading, statusError, login, logout, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
