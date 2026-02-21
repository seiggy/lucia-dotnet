import { createContext, useContext, useState, useEffect, useCallback } from 'react'
import type { ReactNode } from 'react'
import { fetchAuthStatus, login as apiLogin, logout as apiLogout } from '../api'

interface AuthContextValue {
  authenticated: boolean
  setupComplete: boolean
  loading: boolean
  login: (apiKey: string) => Promise<void>
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [authenticated, setAuthenticated] = useState(false)
  const [setupComplete, setSetupComplete] = useState(true)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    try {
      const status = await fetchAuthStatus()
      setAuthenticated(status.authenticated)
      setSetupComplete(status.setupComplete)
    } catch {
      setAuthenticated(false)
      setSetupComplete(false)
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
    <AuthContext.Provider value={{ authenticated, setupComplete, loading, login, logout, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
