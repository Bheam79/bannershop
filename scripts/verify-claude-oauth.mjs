// Isolated browser smoke; no live accounts, DB, backend, or project test suite.
// Start Vite on a free run-service port, then:
// node scripts/verify-claude-oauth.mjs http://127.0.0.1:10000
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
const require = createRequire(new URL('../e2e/package.json', import.meta.url))
const { chromium } = require('playwright')
const baseUrl = process.argv[2]
if (!baseUrl) throw new Error('Pass the local Vite URL')
const browser = await chromium.launch({ headless: true })
try {
  const context = await browser.newContext()
  await context.addInitScript(() => {
    localStorage.setItem('access_token', 'smoke-only')
    localStorage.setItem('user', JSON.stringify({ id: 1, role: 'Admin' }))
    // Automatic popups are deliberately unavailable; real anchors must work.
    window.open = () => null
  })
  let configured = false
  let startCount = 0
  let failStart = false
  let failComplete = false
  const submitted = []
  const status = () => ({ isConfigured: configured, canRefresh: configured,
    source: configured ? 'database' : 'none', expiresAt: null })
  await context.route('**/*', async route => {
    const url = new URL(route.request().url())
    if (url.origin === 'https://claude.com') {
      // Simulate consent without authenticating or contacting the provider.
      return route.fulfill({ contentType: 'text/html', body:
        '<button onclick="this.outerHTML=\'<p>confirmation#smoke-state</p>\'">Godkjenn</button>' })
    }
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/admin/settings') return route.fulfill({ json: [
      { id: 8, key: 'claude_code_oauth_token', label: 'Claude Code OAuth token', isSensitive: true, value: '********' },
      { id: 18, key: 'claude_code_oauth_refresh_token', label: 'Managed refresh', isSensitive: true, value: '********' },
    ] })
    if (url.pathname.endsWith('/claude-oauth/status')) return route.fulfill({ json: status() })
    if (url.pathname.endsWith('/claude-oauth/start')) {
      startCount++
      if (failStart) return route.fulfill({ status: 502, json: { error: 'Start failed (smoke)' } })
      return route.fulfill({ json: {
        authorizationUrl: `https://claude.com/cai/oauth/authorize?code=true&state=smoke-state&attempt=${startCount}`,
        expiresAt: new Date(Date.now() + 600000).toISOString(),
      } })
    }
    if (url.pathname.endsWith('/claude-oauth/complete')) {
      submitted.push(route.request().postDataJSON())
      if (failComplete) return route.fulfill({ status: 400, json: { error: 'Invalid confirmation (smoke)' } })
      configured = true
      return route.fulfill({ json: status() })
    }
    return route.fulfill({ json: {} })
  })
  const page = await context.newPage()
  await page.goto(new URL('/admin/settings', baseUrl).href)
  const panel = page.getByTestId('claude-oauth')
  const start = panel.getByRole('button', { name: 'Koble til Claude', exact: true })
  await start.waitFor()
  assert.equal(await page.getByRole('button', { name: 'Rediger', exact: true }).count(), 0,
    'Claude must not show a second manual-token editor')
  await start.click()
  const link = panel.getByTestId('claude-oauth-link')
  await link.waitFor()
  const code = panel.locator('#claude-oauth-code')
  assert.equal(await code.isVisible(), false, 'No code entry before opening consent link')
  assert.equal(context.pages().length, 1, 'Starting must give a link, not open a popup')
  assert.match(await link.getAttribute('href'), /oauth\/authorize\?code=true/)
  const popupPromise = context.waitForEvent('page')
  await link.click()
  const consent = await popupPromise
  await consent.getByRole('button', { name: 'Godkjenn' }).click()
  await consent.getByText('confirmation#smoke-state', { exact: true }).waitFor()
  await code.waitFor()
  const finish = panel.getByRole('button', { name: 'Fullfør tilkobling' })
  assert.equal(await finish.isDisabled(), true)
  await code.fill('   ')
  assert.equal(await finish.isDisabled(), true)
  failComplete = true
  await code.fill('wrong-code')
  await finish.click()
  await panel.getByRole('alert').filter({ hasText: 'Invalid confirmation' }).waitFor()
  failComplete = false
  await code.fill('  confirmation#smoke-state  ')
  await finish.click()
  await panel.getByRole('status').filter({ hasText: 'fornyes automatisk' }).waitFor()
  assert.deepEqual(submitted.at(-1), { code: 'confirmation#smoke-state' })
  assert.equal(await code.isVisible(), false)
  assert.equal(await link.isVisible(), false)
  await consent.close()

  await panel.getByRole('button', { name: 'Koble til på nytt', exact: true }).click()
  await link.waitFor()
  assert.equal(await code.isVisible(), false)
  const oldLink = await link.getAttribute('href')
  await panel.getByRole('button', { name: 'Start innlogging på nytt' }).click()
  await page.waitForFunction(old => document.querySelector('[data-testid="claude-oauth-link"]')?.getAttribute('href') !== old, oldLink)
  assert.equal(await code.isVisible(), false)
  await panel.getByRole('button', { name: 'Avbryt' }).click()
  assert.equal(await link.isVisible(), false)
  failStart = true
  await panel.getByRole('button', { name: 'Koble til på nytt', exact: true }).click()
  await panel.getByRole('alert').filter({ hasText: 'Start failed' }).waitFor()
  assert.equal(await code.isVisible(), false)
  assert.equal(await link.isVisible(), false)
  console.log('PASS: link-first, no token editor/device code, consent → paste → connected, validation, restart, cancel and API errors (mocked provider).')
} finally {
  await browser.close()
}
