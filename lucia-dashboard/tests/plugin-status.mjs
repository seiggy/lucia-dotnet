import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { chromium } from 'playwright'
import { preview } from 'vite'

let server
let browser
let baseUrl

before(async () => {
  server = await preview({
    preview: { host: '127.0.0.1', port: 0, strictPort: true, open: false },
  })
  baseUrl = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch()
})

after(async () => {
  await browser?.close()
  await new Promise((resolve, reject) => server.httpServer.close(error => error ? reject(error) : resolve()))
})

const installedPlugins = [
  {
    id: 'status-plugin',
    name: 'Status Plugin',
    version: '1.0.0',
    source: 'bundled',
    pluginPath: '/plugins/status-plugin',
    enabled: true,
    installedAt: new Date().toISOString(),
    status: 'All systems nominal',
  },
  {
    id: 'plain-plugin',
    name: 'Plain Plugin',
    version: '2.1.0',
    source: 'bundled',
    pluginPath: '/plugins/plain-plugin',
    enabled: true,
    installedAt: new Date().toISOString(),
  },
]

async function openPluginsPage() {
  const context = await browser.newContext({ viewport: { width: 1280, height: 1000 } })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/api/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    const reply = (json, status = 200) => route.fulfill({ json, status })
    if (path === '/api/auth/status') {
      return reply({ authenticated: true, setupComplete: true })
    }
    if (path === '/api/system/version') {
      return reply({ version: 'test' })
    }
    if (path === '/api/system/restart-required') {
      return reply({ restartRequired: false })
    }
    if (path === '/api/appliance/capabilities') {
      return reply({ enabled: false })
    }
    if (path === '/api/tasks/background/stream') {
      return route.fulfill({ contentType: 'text/event-stream', body: 'event: snapshot\ndata: []\n\n' })
    }
    if (path === '/api/tasks/background') {
      return reply([])
    }
    if (path === '/api/plugins/installed') {
      return reply(installedPlugins)
    }
    if (path === '/api/plugins/available' || path.startsWith('/api/plugins/available')) {
      return reply([])
    }
    errors.push(`Unexpected API request: ${request.method()} ${path}`)
    return reply({ error: 'Unexpected test request' }, 500)
  })

  await page.goto(`${baseUrl}/plugins`)
  await page.getByText('Status Plugin').waitFor()
  await page.getByText('Plain Plugin').waitFor()
  return { context, page, errors }
}

test('installed plugins show a live status line only when the plugin reports one', async () => {
  const { context, page, errors } = await openPluginsPage()
  try {
    const statusLines = page.getByTestId('plugin-status')
    assert.equal(await statusLines.count(), 1)
    assert.equal((await statusLines.first().innerText()).trim(), 'All systems nominal')
    assert.deepEqual(errors, [])
  } finally {
    await context.close()
  }
})
