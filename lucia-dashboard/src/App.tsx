import { NavLink, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
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

function App() {
  return (
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  )
}

function AppRoutes() {
  const { authenticated, setupComplete, loading, logout } = useAuth()

  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gray-900">
        <div className="text-gray-400">Loading...</div>
      </div>
    )
  }

  // Setup not complete → show setup wizard
  if (!setupComplete) {
    return (
      <Routes>
        <Route path="/setup" element={<SetupPage />} />
        <Route path="*" element={<Navigate to="/setup" replace />} />
      </Routes>
    )
  }

  // Not authenticated → show login
  if (!authenticated) {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    )
  }

  // Authenticated → full dashboard
  return (
    <div className="min-h-screen bg-gray-900 text-white">
      <nav className="border-b border-gray-700 bg-gray-800">
        <div className="mx-auto flex max-w-7xl items-center gap-6 px-4 py-3">
          <span className="text-lg font-bold text-indigo-400">Lucia Dashboard</span>
          <NavLink
            to="/"
            end
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Traces
          </NavLink>
          <NavLink
            to="/exports"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Exports
          </NavLink>
          <NavLink
            to="/configuration"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Configuration
          </NavLink>
          <NavLink
            to="/agent-dashboard"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Agents
          </NavLink>
          <NavLink
            to="/prompt-cache"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Prompt Cache
          </NavLink>
          <NavLink
            to="/mcp-servers"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            MCP Servers
          </NavLink>
          <NavLink
            to="/agent-definitions"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Agent Definitions
          </NavLink>
          <NavLink
            to="/model-providers"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Model Providers
          </NavLink>
          <NavLink
            to="/tasks"
            className={({ isActive }) =>
              `text-sm font-medium ${isActive ? 'text-white' : 'text-gray-400 hover:text-gray-200'}`
            }
          >
            Tasks
          </NavLink>
          <div className="ml-auto">
            <button
              onClick={logout}
              className="rounded px-3 py-1 text-sm text-gray-400 transition hover:bg-gray-700 hover:text-white"
            >
              Sign Out
            </button>
          </div>
        </div>
      </nav>

      <main className="mx-auto max-w-7xl px-4 py-6">
        <Routes>
          <Route path="/" element={<TraceListPage />} />
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
  )
}

export default App
