import { useState } from 'react'
import { NavLink, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
import {
  Activity, Download, Settings, Bot, Database, Server,
  Layers, Boxes, ListTodo, Menu, X, LogOut, Sparkles, BarChart3
} from 'lucide-react'
import LoginPage from './pages/LoginPage'
import SetupPage from './pages/SetupPage'
import TraceListPage from './pages/TraceListPage'
import TraceDetailPage from './pages/TraceDetailPage'
import ExportPage from './pages/ExportPage'
import ConfigurationPage from './pages/ConfigurationPage'
import AgentsPage from './pages/AgentsPage'
import PromptCachePage from './pages/PromptCachePage'
import TasksPage from './pages/TasksPage'
import McpServersPage from './pages/McpServersPage'
import AgentDefinitionsPage from './pages/AgentDefinitionsPage'
import ModelProvidersPage from './pages/ModelProvidersPage'
import ActivityPage from './pages/ActivityPage'

const NAV_ITEMS = [
  { to: '/', label: 'Activity', icon: BarChart3, end: true },
  { to: '/traces', label: 'Traces', icon: Activity },
  { to: '/agent-dashboard', label: 'Agents', icon: Bot },
  { to: '/agent-definitions', label: 'Definitions', icon: Layers },
  { to: '/model-providers', label: 'Providers', icon: Boxes },
  { to: '/mcp-servers', label: 'MCP Servers', icon: Server },
  { to: '/prompt-cache', label: 'Prompt Cache', icon: Database },
  { to: '/tasks', label: 'Tasks', icon: ListTodo },
  { to: '/exports', label: 'Exports', icon: Download },
  { to: '/configuration', label: 'Configuration', icon: Settings },
]

function App() {
  return (
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  )
}

function AppRoutes() {
  const { authenticated, setupComplete, loading, logout } = useAuth()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-observatory">
        <div className="flex items-center gap-3 text-fog">
          <Sparkles className="h-5 w-5 animate-pulse text-amber" />
          <span className="font-display text-sm tracking-wide">Loading...</span>
        </div>
      </div>
    )
  }

  if (!setupComplete) {
    return (
      <Routes>
        <Route path="/setup" element={<SetupPage />} />
        <Route path="*" element={<Navigate to="/setup" replace />} />
      </Routes>
    )
  }

  if (!authenticated) {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    )
  }

  return (
    <div className="min-h-screen bg-observatory">
      {/* ── Mobile overlay ── */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/50 md:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* ── Sidebar ── */}
      <aside className={`
        fixed inset-y-0 left-0 z-50 flex w-64 flex-col
        border-r border-stone/40 bg-obsidian
        sidebar-transition
        ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
        md:translate-x-0
      `}>
        {/* Logo area */}
        <div className="flex h-16 items-center justify-between px-5 border-b border-stone/40">
          <div className="flex items-center gap-2.5">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-amber/10">
              <Sparkles className="h-4 w-4 text-amber" />
            </div>
            <span className="font-display text-lg font-semibold tracking-tight text-light">
              Lucia
            </span>
          </div>
          <button
            onClick={() => setSidebarOpen(false)}
            className="rounded-md p-1 text-dust hover:text-cloud md:hidden"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Navigation */}
        <nav className="flex-1 overflow-y-auto px-3 py-4">
          <ul className="space-y-0.5">
            {NAV_ITEMS.map(({ to, label, icon: Icon, end }) => (
              <li key={to}>
                <NavLink
                  to={to}
                  end={end}
                  onClick={() => setSidebarOpen(false)}
                  className={({ isActive }) =>
                    `relative flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${
                      isActive
                        ? 'nav-active'
                        : 'text-fog hover:text-cloud hover:bg-stone/40'
                    }`
                  }
                >
                  <Icon className="h-[18px] w-[18px] shrink-0" />
                  {label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>

        {/* Footer */}
        <div className="border-t border-stone/40 px-3 py-3">
          <button
            onClick={logout}
            className="flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium text-fog transition-colors hover:text-cloud hover:bg-stone/40"
          >
            <LogOut className="h-[18px] w-[18px]" />
            Sign Out
          </button>
        </div>
      </aside>

      {/* ── Main content area ── */}
      <div className="md:pl-64">
        {/* Mobile top bar */}
        <header className="sticky top-0 z-30 flex h-14 items-center gap-4 border-b border-stone/40 bg-obsidian/80 px-4 backdrop-blur-md md:hidden">
          <button
            onClick={() => setSidebarOpen(true)}
            className="rounded-md p-1.5 text-fog hover:text-cloud"
          >
            <Menu className="h-5 w-5" />
          </button>
          <div className="flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-amber" />
            <span className="font-display text-sm font-semibold text-light">Lucia</span>
          </div>
        </header>

        <main className="flex-1 overflow-auto px-4 py-6 sm:px-6 lg:px-8">
          <Routes>
            <Route path="/" element={<ActivityPage />} />
            <Route path="/traces" element={<TraceListPage />} />
            <Route path="/traces/:id" element={<TraceDetailPage />} />
            <Route path="/exports" element={<ExportPage />} />
            <Route path="/configuration" element={<ConfigurationPage />} />
            <Route path="/agent-dashboard" element={<AgentsPage />} />
            <Route path="/prompt-cache" element={<PromptCachePage />} />
            <Route path="/tasks" element={<TasksPage />} />
            <Route path="/mcp-servers" element={<McpServersPage />} />
            <Route path="/agent-definitions" element={<AgentDefinitionsPage />} />
            <Route path="/model-providers" element={<ModelProvidersPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}

export default App
