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

const pluginProviders = [{
  serverId: 'plugin:Flight weather',
  name: 'Flight weather',
  description: 'Flying conditions and forecasts',
  tools: [
    { serverId: 'plugin:Flight weather', toolName: 'check_flight_conditions', description: 'Check whether conditions are flyable at a location.' },
    { serverId: 'plugin:Flight weather', toolName: 'forecast', description: 'Detailed forecast' },
  ],
}, {
  serverId: 'plugin:Broken',
  name: 'Broken plugin',
  description: 'Unavailable capability',
  errorMessage: 'Configuration missing',
  tools: [],
}]

async function openPage({ viewport, providers = pluginProviders, pluginStatus = 200, mcpStatus = 200, definitions = [], mcpId = 'search' } = {}) {
  const context = await browser.newContext({ viewport: viewport ?? { width: 1280, height: 1000 } })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  const writes = []
  const stored = [...definitions]
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
    if (path === '/api/appliance/capabilities') {
      return reply({ enabled: false })
    }
    if (path === '/api/tasks/background/stream') {
      return route.fulfill({ contentType: 'text/event-stream', body: 'event: snapshot\ndata: []\n\n' })
    }
    if (path === '/api/tasks/background') {
      return reply([])
    }
    if (path === '/api/agent-definitions/tools/plugins') {
      return reply(providers, pluginStatus)
    }
    if (path === '/api/agent-definitions' || request.method() === 'PATCH') {
      if (request.method() === 'GET') {
        return reply(stored)
      }
      const body = request.postDataJSON()
      writes.push(body)
      const index = stored.findIndex(definition => definition.id === body.id)
      if (index >= 0) {
        stored[index] = { ...stored[index], ...body }
      } else {
        stored.push({ ...body, isBuiltIn: false, isRemote: false, isOrchestrator: false })
      }
      return reply(body)
    }
    if (path.endsWith('/skill-config')) {
      return reply([])
    }
    if (path === '/api/model-providers') {
      return reply([])
    }
    if (path === '/api/mcp-servers/status') {
      return reply({ [mcpId]: { state: 'Connected', serverId: mcpId, serverName: 'Search' } }, mcpStatus)
    }
    if (path === '/api/mcp-servers') {
      return reply([{ id: mcpId, name: 'Search', enabled: true }], mcpStatus)
    }
    if (decodeURIComponent(path) === `/api/mcp-servers/${mcpId}/tools`) {
      return reply([{ serverId: mcpId, toolName: 'web_search', description: 'Search the web' }])
    }
    errors.push(`Unexpected API request: ${request.method()} ${path}`)
    return reply({ error: 'Unexpected test request' }, 500)
  })

  await page.goto(`${baseUrl}/agent-definitions`)
  await page.getByRole('button', { name: '+ New Agent', exact: true }).waitFor()
  return { context, page, errors, writes }
}

test('a plugin-prefixed MCP server remains separate from the plugin provider', async () => {
  const mcpId = 'plugin:Flight weather'
  const mcpReference = { serverId: mcpId, toolName: 'web_search' }
  const { context, page, writes } = await openPage({
    mcpId,
    definitions: [{
      id: 'existing', name: 'existing', displayName: 'Existing', description: '', instructions: '',
      enabled: true, tools: [mcpReference], isBuiltIn: false,
    }],
  })
  try {
    await page.getByRole('button', { name: 'Edit', exact: true }).click()
    assert.equal(await page.getByRole('checkbox', { name: /web_search/ }).isChecked(), true)
    assert.equal(await page.getByRole('heading', { name: 'Unavailable selected plugin tools' }).count(), 0)
    await page.getByRole('checkbox', { name: /check_flight_conditions/ }).check()
    await page.getByRole('button', { name: 'Update Agent', exact: true }).click()
    assert.deepEqual(writes[0].tools, [
      mcpReference,
      { serverId: mcpId, toolName: 'check_flight_conditions', source: 'plugin' },
    ])
  } finally {
    await context.close()
  }
})

for (const viewport of [{ width: 1280, height: 1000 }, { width: 390, height: 844 }]) {
  test(`create and edit a custom agent with plugin and MCP tools at ${viewport.width}px`, async () => {
    const { context, page, errors, writes } = await openPage({ viewport })
    try {
      await page.getByRole('button', { name: '+ New Agent', exact: true }).click()
      await page.getByRole('textbox', { name: /^Display Name/ }).fill('Flight helper')
      await page.getByLabel('Instructions (System Prompt Override)', { exact: true }).fill('Answer briefly.')
      await page.getByRole('checkbox', { name: /check_flight_conditions/ }).check()
      await page.getByRole('checkbox', { name: /web_search/ }).check()
      assert.equal(await page.getByRole('checkbox', { name: /forecast Detailed/ }).isChecked(), false)
      await page.getByText('Tools unavailable: Configuration missing.', { exact: false }).waitFor()
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true)
      if (process.env.LUCIA_UI_SCREENSHOTS) {
        await page.screenshot({ path: `${process.env.LUCIA_UI_SCREENSHOTS}\\plugin-tools-${viewport.width}.png`, fullPage: true })
      }
      await page.getByRole('button', { name: 'Create Agent', exact: true }).click()
      await page.getByRole('button', { name: 'Edit', exact: true }).waitFor()
      assert.deepEqual(writes[0].tools, [
        { serverId: 'plugin:Flight weather', toolName: 'check_flight_conditions', source: 'plugin' },
        { serverId: 'search', toolName: 'web_search' },
      ])
      assert.equal(writes[0].instructions, 'Answer briefly.')
      await page.getByRole('button', { name: 'Edit', exact: true }).click()
      const pluginCheckbox = page.getByRole('checkbox', { name: /check_flight_conditions/ })
      await pluginCheckbox.waitFor()
      assert.equal(await pluginCheckbox.isChecked(), true)
      assert.equal(await page.getByRole('checkbox', { name: /web_search/ }).isChecked(), true)
      await pluginCheckbox.uncheck()
      await page.getByRole('button', { name: 'Update Agent', exact: true }).click()
      await page.getByRole('button', { name: 'Edit', exact: true }).waitFor()
      assert.deepEqual(writes[1].tools, [{ serverId: 'search', toolName: 'web_search' }])
      assert.deepEqual(errors, [])
    } finally {
      await context.close()
    }
  })
}

test('plugin tools remain selectable if MCP discovery fails', async () => {
  const { context, page, errors } = await openPage({ mcpStatus: 503 })
  try {
    await page.getByRole('button', { name: '+ New Agent', exact: true }).click()
    await page.getByRole('checkbox', { name: /check_flight_conditions/ }).check()
    await page.getByRole('alert').filter({ hasText: 'Failed to fetch MCP' }).waitFor()
    assert.deepEqual(errors, [])
  } finally {
    await context.close()
  }
})

test('unavailable plugin selections survive save and can be removed after discovery fails', async () => {
  const reference = { serverId: 'plugin:Removed', toolName: 'old_tool', source: 'plugin' }
  const { context, page, writes } = await openPage({
    pluginStatus: 503,
    definitions: [{
      id: 'existing', name: 'existing', displayName: 'Existing', description: '', instructions: '',
      enabled: true, tools: [reference], isBuiltIn: false,
    }],
  })
  try {
    await page.getByRole('button', { name: 'Edit', exact: true }).click()
    await page.getByRole('heading', { name: 'Unavailable selected plugin tools', exact: true }).waitFor()
    await page.getByRole('checkbox', { name: /web_search/ }).waitFor()
    await page.getByRole('alert').filter({ hasText: 'Failed to load plugin tools' }).waitFor()
    await page.getByRole('button', { name: 'Update Agent', exact: true }).click()
    await page.getByRole('button', { name: 'Edit', exact: true }).waitFor()
    assert.deepEqual(writes[0].tools, [reference])
    await page.getByRole('button', { name: 'Edit', exact: true }).click()
    await page.getByRole('button', { name: 'Remove old_tool from plugin:Removed', exact: true }).click()
    await page.getByRole('button', { name: 'Update Agent', exact: true }).click()
    await page.getByRole('button', { name: 'Edit', exact: true }).waitFor()
    assert.deepEqual(writes[1].tools, [])
  } finally {
    await context.close()
  }
})

test('no plugin providers shows installation guidance without hiding MCP tools', async () => {
  const { context, page } = await openPage({ providers: [] })
  try {
    await page.getByRole('button', { name: '+ New Agent', exact: true }).click()
    await page.getByText('No plugin tool providers registered.', { exact: false }).waitFor()
    await page.getByRole('checkbox', { name: /web_search/ }).check()
  } finally {
    await context.close()
  }
})
