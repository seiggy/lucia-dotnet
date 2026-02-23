import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { Sparkles, ArrowRight } from 'lucide-react'

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
    <div className="flex min-h-screen items-center justify-center bg-observatory px-4">
      {/* Ambient glow */}
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute left-1/2 top-1/3 h-[500px] w-[500px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-amber/[0.03] blur-[100px]" />
      </div>

      <div className="relative w-full max-w-sm">
        {/* Logo */}
        <div className="mb-8 text-center">
          <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-amber/10 glow-amber">
            <Sparkles className="h-7 w-7 text-amber" />
          </div>
          <h1 className="font-display text-2xl font-semibold tracking-tight text-light">
            Lucia
          </h1>
          <p className="mt-1.5 text-sm text-fog">
            Enter your API key to continue
          </p>
        </div>

        {/* Card */}
        <div className="glass-panel rounded-2xl p-6 glow-amber-sm">
          <form onSubmit={handleSubmit} className="space-y-5">
            <div>
              <label htmlFor="apiKey" className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">
                API Key
              </label>
              <input
                id="apiKey"
                type="password"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder="lk_..."
                className="w-full rounded-xl border border-stone bg-basalt px-4 py-3 text-sm text-light placeholder-dust/60 input-focus transition-colors"
                autoFocus
                required
              />
            </div>

            {error && (
              <div className="rounded-xl border border-ember/30 bg-ember/10 px-4 py-2.5 text-sm text-rose">
                {error}
              </div>
            )}

            <button
              type="submit"
              disabled={loading || !apiKey.trim()}
              className="group flex w-full items-center justify-center gap-2 rounded-xl bg-amber px-4 py-3 text-sm font-semibold text-void transition-all hover:bg-amber-glow disabled:cursor-not-allowed disabled:opacity-40"
            >
              {loading ? (
                <span className="flex items-center gap-2">
                  <span className="h-4 w-4 animate-spin rounded-full border-2 border-void/30 border-t-void" />
                  Signing in...
                </span>
              ) : (
                <>
                  Sign In
                  <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
                </>
              )}
            </button>
          </form>
        </div>
      </div>
    </div>
  )
}
