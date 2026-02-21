import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'

export default function LoginPage() {
  const { login } = useAuth()
  const [apiKey, setApiKey] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      await login(apiKey)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Login failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-900">
      <div className="w-full max-w-md rounded-xl border border-gray-700 bg-gray-800 p-8 shadow-2xl">
        <div className="mb-6 text-center">
          <h1 className="text-2xl font-bold text-indigo-400">Lucia</h1>
          <p className="mt-1 text-sm text-gray-400">Enter your API key to sign in</p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="apiKey" className="mb-1 block text-sm font-medium text-gray-300">
              API Key
            </label>
            <input
              id="apiKey"
              type="password"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="lk_..."
              className="w-full rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
              autoFocus
              required
            />
          </div>

          {error && (
            <div className="rounded-lg border border-red-800 bg-red-900/30 px-3 py-2 text-sm text-red-300">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={loading || !apiKey.trim()}
            className="w-full rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {loading ? 'Signing in...' : 'Sign In'}
          </button>
        </form>
      </div>
    </div>
  )
}
