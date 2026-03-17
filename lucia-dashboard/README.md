# Lucia Dashboard

The Lucia Dashboard is a React management UI for the [Lucia](../README.md) home assistant AI platform. It provides real-time monitoring, agent configuration, trace inspection, and full system administration through a dark-themed, mobile-responsive web interface.

## Tech Stack

- **React 19** with TypeScript 5.9
- **Vite 7** for development and bundling
- **Tailwind CSS 4** for styling
- **TanStack Query 5** for server-state management
- **React Router 7** for client-side routing
- **XYFlow (React Flow)** for agent mesh graph visualization
- **Lucide React** for icons
- **Playwright** for E2E testing

## Prerequisites

- **Node.js 22+**
- A running Lucia AgentHost backend (default: `http://localhost:5151`)

## Getting Started

```bash
# Install dependencies
npm install

# Start development server
npm run dev
```

The dev server starts at **http://localhost:7233** by default and proxies API requests to the AgentHost backend.

### Running via Aspire

When launched through the .NET Aspire AppHost (`dotnet run --project lucia.AppHost`), the dashboard starts automatically. Aspire injects the AgentHost URL via environment variables so no manual proxy configuration is needed.

## Available Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start Vite dev server with HMR |
| `npm run build` | Type-check with `tsc` then build for production |
| `npm run lint` | Run ESLint across the project |
| `npm run preview` | Preview the production build locally |

## Project Structure

```
src/
├── api.ts              # Typed API client (50+ functions)
├── types.ts            # Shared TypeScript interfaces
├── App.tsx             # Root component, routing, sidebar navigation
├── auth/
│   └── AuthContext.tsx  # Authentication provider and route guards
├── pages/              # 20 page components (one per route)
│   ├── ActivityPage.tsx          # Real-time agent mesh & event stream
│   ├── AgentDefinitionsPage.tsx  # Create/edit agent definitions
│   ├── AgentsPage.tsx            # Agent registry & test chat
│   ├── AlarmsPage.tsx            # Alarm clock management
│   ├── ConfigurationPage.tsx     # System configuration editor
│   ├── EntityLocationPage.tsx    # Entity-to-area mapping
│   ├── ExportPage.tsx            # JSONL dataset export for fine-tuning
│   ├── ListsPage.tsx             # List management
│   ├── LoginPage.tsx             # API key login
│   ├── MatcherDebugPage.tsx      # Entity matcher diagnostics
│   ├── McpServersPage.tsx        # MCP tool server management
│   ├── ModelProvidersPage.tsx    # LLM provider configuration
│   ├── PluginsPage.tsx           # Plugin installation & management
│   ├── PresencePage.tsx          # Presence sensor configuration
│   ├── PromptCachePage.tsx       # Routing/chat cache management
│   ├── SetupPage.tsx             # Guided onboarding wizard
│   ├── SkillOptimizerPage.tsx    # Skill threshold tuning
│   ├── TasksPage.tsx             # Active & archived task viewer
│   ├── TraceDetailPage.tsx       # Individual trace inspection
│   └── TraceListPage.tsx         # Trace browsing with filters
├── components/         # Shared reusable components
│   ├── CustomSelect.tsx          # Styled dropdown select
│   ├── EntityMultiSelect.tsx     # Multi-entity picker
│   ├── MeshGraph.tsx             # React Flow agent mesh visualization
│   ├── PluginConfigTab.tsx       # Plugin configuration panel
│   ├── PluginRepoDialog.tsx      # Plugin repository browser
│   ├── RestartBanner.tsx         # Service restart notification
│   ├── SkillConfigEditor.tsx     # Skill parameter editor
│   └── SpanTimeline.tsx          # Trace span timeline view
└── hooks/
    └── useActivityStream.ts      # SSE hook for live activity events
```

## API Proxy

The Vite dev server proxies API requests to the AgentHost backend. The proxy target is resolved in this order:

1. `services__lucia-agenthost__https__0` — set by Aspire
2. `services__lucia-agenthost__http__0` — set by Aspire
3. `http://localhost:5151` — default fallback

Proxied route prefixes: `/api`, `/agents`, `/a2a`, `/agent` (POST only).

## Authentication

The dashboard uses API key authentication with HMAC-signed sessions:

1. **First run** — The setup wizard at `/setup` generates a dashboard API key.
2. **Login** — Users authenticate at `/login` with their API key.
3. **Route guards** — `AuthContext` redirects unauthenticated users to login and users who haven't completed setup to the wizard.

## Building for Production

```bash
npm run build
```

Output is written to `dist/`. The production build is a static SPA that expects the AgentHost API to be available at the same origin (or behind a reverse proxy).
