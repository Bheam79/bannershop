// Standalone browser smoke for the real admin Vue screen, with offline API fixtures.
import { chromium } from '../../e2e/node_modules/playwright/index.mjs'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'

const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage({ acceptDownloads: true })
  const runtimeErrors = []
  page.on('pageerror', e => runtimeErrors.push(e.message))
  await page.addInitScript(() => {
    localStorage.setItem('access_token', 'fixture')
    localStorage.setItem('user', JSON.stringify({ id: 1, name: 'Admin', email: 'admin@example.invalid', role: 'Admin' }))
  })
  const item = id => ({ id, bannerSizeName: `Banner ${id}`, customWidthCm: 300, heightCm: 150,
    quantity: 1, unitPriceNok: 1000, lineTotalNok: 1000, currentProductionStage: 'Queued',
    productionStatusHistory: [], designDownloadUrl: id === 3 ? null : '/files/original.png', designSource: 'Ai' })
  const order = { id: 312, status: 'Paid', orderType: 'AiBanner', orderState: 'InProduction',
    deliveryType: 'Standard', shippingCostNok: 0, expressFeeNok: 0, totalNok: 3000,
    createdAt: '2026-10-07T08:00:00Z', updatedAt: '2026-10-07T08:00:00Z', items: [1, 2, 3].map(item),
    shippingAddress: null, shipmentTracking: null }
  const polls = new Map()
  let fail = false
  await page.route('**/api/**', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.pathname.includes('/upscale')) {
      assert.equal(request.headers().authorization, 'Bearer fixture', 'Authenticated API transport')
      const key = `${url.pathname}/${url.searchParams.get('scale')}`
      if (fail) return route.fulfill({ status: 503, json: { error: 'Legg inn fal.ai API Key under admin-innstillinger først.' } })
      if (url.pathname.endsWith('/download')) {
        return route.fulfill({ contentType: 'image/png', body: Buffer.from('offline-fixture-png') })
      }
      assert.equal(request.method(), 'POST')
      polls.set(key, (polls.get(key) ?? 0) + 1)
      return route.fulfill({ json: { ready: polls.get(key) > 1 } })
    }
    if (url.pathname === '/api/admin/orders/312') return route.fulfill({ json: order })
    if (url.pathname === '/api/admin/settings') return route.fulfill({ json: [{ id: 7, key: 'fal_api_key',
      label: 'fal.ai API Key (admin order upscaling)', isSensitive: true, value: '••••••••' }] })
    return route.fulfill({ json: {} })
  })
  await page.goto('http://localhost:10000/admin/orders/312')
  await page.getByRole('button', { name: '⬇ Last ned 2x oppskalert', exact: true }).first().waitFor()
  assert.equal(await page.getByRole('button', { name: /Last ned [24]x oppskalert/ }).count(), 4,
    'Two buttons per item with a print file; none for missing file')
  for (const scale of [2, 4]) {
    const button = page.getByRole('button', { name: `⬇ Last ned ${scale}x oppskalert`, exact: true }).first()
    const downloadPromise = page.waitForEvent('download')
    await button.click()
    await page.getByRole('button', { name: `Oppskalerer ${scale}x …`, exact: true }).waitFor()
    assert.equal(await page.getByRole('button', { name: `⬇ Last ned ${scale === 2 ? 4 : 2}x oppskalert`, exact: true }).first().isDisabled(), true)
    const download = await downloadPromise
    assert.equal(download.suggestedFilename(), `ordre-312-banner-1-${scale}x.png`)
    assert.equal((await readFile(await download.path())).toString(), 'offline-fixture-png')
    console.log(`PASS: ${scale}x loading state, correct item/scale, authenticated PNG attachment`)
  }
  // Ready cached result completes immediately with the same endpoint; no new client job ID.
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: '⬇ Last ned 2x oppskalert', exact: true }).first().click()
  await downloadPromise
  assert.equal(polls.get('/api/admin/orders/312/items/1/upscale/2'), 3)
  console.log('PASS: repeat download reuses same item/scale cache request')
  fail = true
  await page.getByRole('button', { name: '⬇ Last ned 4x oppskalert', exact: true }).nth(1).click()
  await page.getByRole('alert').filter({ hasText: 'Legg inn fal.ai API Key' }).waitFor()
  assert.equal(await page.getByRole('button', { name: '⬇ Last ned 4x oppskalert', exact: true }).nth(1).isEnabled(), true)
  console.log('PASS: missing-key error is visible and buttons recover')
  await page.goto('http://localhost:10000/admin/settings')
  await page.getByText('fal.ai API Key (admin order upscaling)', { exact: true }).waitFor()
  assert.equal(runtimeErrors.length, 0, runtimeErrors.join('\n'))
  console.log('PASS: fal key appears in existing masked admin settings editor; no Vue runtime errors')
} finally { await browser.close() }
