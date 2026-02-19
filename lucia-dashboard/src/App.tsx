import { NavLink, Routes, Route } from 'react-router-dom'
import TraceListPage from './pages/TraceListPage'
import TraceDetailPage from './pages/TraceDetailPage'
import ExportPage from './pages/ExportPage'

function App() {
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
        </div>
      </nav>

      <main className="mx-auto max-w-7xl px-4 py-6">
        <Routes>
          <Route path="/" element={<TraceListPage />} />
          <Route path="/traces/:id" element={<TraceDetailPage />} />
          <Route path="/exports" element={<ExportPage />} />
        </Routes>
      </main>
    </div>
  )
}

export default App
