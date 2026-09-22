// Standalone targeted browser smoke; not the project test suite. Start Vite on :10000 first.
import { chromium } from '../../e2e/node_modules/playwright/index.mjs'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
const fixture = JSON.parse(readFileSync(process.env.GUEST_SMOKE_FIXTURE, 'utf8'))
const login = process.env.GUEST_SMOKE_AUTH === 'login'
const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage()
  let polls = 0
  let claims = 0
  let authenticated = false
  let activations = 0
  let approvals = 0
  let detail = structuredClone(fixture)
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (!path.startsWith("/api/")) return route.continue()
    let data = []
    if (path === '/api/templates') data = [{ id: 1, category: 'Birthday', nameNb: 'Bursdag', nameEn: 'Birthday', sortOrder: 1 }]
    if (path.endsWith('/auth/register') || path.endsWith('/auth/login')) {
      authenticated = true
      data = { accessToken: 'smoke', refreshToken: 'smoke', user: { id: 305, name: 'Guest', email: 'guest@example.invalid', role: 'Customer' } }
    }
    if (path.includes('/generations/')) {
      activations++
      if (!authenticated) return route.fulfill({status: 401, json: {}})
      const id = Number(path.split('/').at(-2))
      detail.currentGenerationId = id
      detail.previewUrl = detail.generationHistory.find(g => g.id === id).previewUrl
      detail.generationHistory.forEach(g => { g.isActive = g.id === id })
      data = detail
    }
    if (path.endsWith('/design-requests/305/approve')) {
      approvals++
      assert.ok(authenticated, 'Approval requires an account')
      assert.equal(detail.currentGenerationId, 3052)
      data = { ...detail, status: 'Approved', finalBannerDesignId: 42 }
    }
    if (path === '/api/banner-builder/42') data = { computedWidthCm: 267, selectedHeightCm: 150 }
    if (path === '/api/sizes/price') data = { sizeId: 1, priceNok: 1000 }
    if (path === '/api/sizes') data = [{ id: 1, minWidthCm: 1, maxWidthCm: 999, minHeightCm: 1, maxHeightCm: 999, materialId: 1, material: {id: 1, weightGsm: 680, name: 'Smoke'} }]
    if (path.endsWith('/design-requests/305/claim')) { claims++; detail.userId = 305; data = detail }
    if (path.endsWith('/ai-credits/me')) data = { creditsRemaining: 5, hasUsedFreeGeneration: false }

    if (path.endsWith('/design-requests/ai')) data = { designRequestId: 305, requiresAuth: true, creditsRemaining: 0 }
    if (path.endsWith('/design-requests/305')) {
      polls++
      data = detail
    }
    await route.fulfill({ json: data })
  })
  await page.route('**/files/guest-preview*', route => route.fulfill({contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="90"><rect width="160" height="90" fill="gold"/></svg>'}))
  await page.goto('http://localhost:10000/login')
  const state = await page.evaluate(async () => {
    const { useBannerGeneration } = await import('/src/composables/banner-builder/useBannerGeneration.ts')
    const generation = useBannerGeneration({
      getTemplateId: () => 1, getLanguage: () => 'nb', getPersonName: () => 'Guest 305', getPersonAge: () => null,
      getTextContent: () => 'Happy birthday', getThemeDescription: () => 'Stars', getAspectRatioForBackend: () => '16:9',
      getUploadedPhotoBannerDesignId: () => null, getSelectedDimensions: () => ({width: 266, height: 150}),
      onPaywall: () => {}, onGenerationComplete: () => {}, loadTilpassPricing: async () => {}, isManual: () => false,
    })
    await generation.generateBanner()
    await new Promise(resolve => setTimeout(resolve, 500))
    generation.cleanup()
    return {phase: generation.genPhase.value, preview: generation.currentDesignRequest.value?.previewUrl}
  })
  console.log('Guest generation:', state, 'polls:', polls)
  // Also reproduce the login -> register redirect loss independently.
  await page.goto('http://localhost:10000/login?redirect=' + encodeURIComponent('/banner-builder/ai?dr=305'))
  const signup = await page.getByRole('link', {name: 'Registrer deg', exact: true}).getAttribute('href')
  console.log('Sign-up link:', signup)
  assert.equal(state.phase, 'ready')
  assert.equal(state.preview, fixture.previewUrl)
  assert.ok(polls > 0)
  assert.ok(signup.includes('redirect='), 'Login -> register must preserve wizard return URL')
  await page.goto('http://localhost:10000/banner-builder/ai?dr=305')
  await page.locator('#personName').waitFor()
  assert.equal(await page.locator('#personName').inputValue(), 'Guest 305')
  const preview = page.locator(`img[src="${fixture.previewUrl}"]`).first()
  await preview.waitFor({ state: 'visible' })
  assert.ok(await preview.evaluate(img => img.complete && img.naturalWidth > 0), 'Guest preview is actually loaded')
  await page.getByRole('button', {name: /Alternativ 2/}).click()
  await page.waitForTimeout(500)
  assert.ok(page.url().includes('/banner-builder/ai?dr=305'), 'Switching guest alternative must not navigate to login')
  const selectedPreview = page.locator('img[src="/files/guest-preview-2.svg"]').first()
  await selectedPreview.waitFor({ state: 'visible' })
  assert.ok(await selectedPreview.evaluate(img => img.complete && img.naturalWidth > 0))
  await page.getByRole('button', {name: /Alternativ 1/}).click()
  await preview.waitFor({ state: 'visible' })
  await page.getByRole('button', {name: /Alternativ 2/}).click()
  assert.equal(activations, 0, 'Guest selection never calls the authenticated activation endpoint')
  await page.reload()
  await selectedPreview.waitFor({ state: 'visible' })
  await page.locator('#personName').fill('Edited before sign-up')
  await page.locator('#themeDescription').fill('Edited theme before sign-up')
  await page.getByRole('button', {name: 'Gå videre', exact: true}).click()
  await page.waitForURL('**/register?**')
  assert.ok(decodeURIComponent(page.url()).includes('/banner-builder/ai?dr=305'))
  // Switching auth forms must keep the same destination in both directions.
  await page.getByRole('link', {name: 'Logg inn', exact: true}).last().click()
  await page.waitForURL('**/login?**')
  await page.getByRole('link', {name: 'Registrer deg', exact: true}).click()
  await page.waitForURL('**/register?**')
  if (login) {
    await page.getByRole('link', {name: 'Logg inn', exact: true}).last().click()
    await page.waitForURL('**/login?**')
  } else {
    await page.locator('#name').fill('Guest')
    await page.locator('#confirm-password').fill('SmokePassword123!')
  }
  await page.locator('#email').fill('guest@example.invalid')
  await page.locator('#password').fill('SmokePassword123!')
  await page.getByRole('button', {name: login ? 'Logg inn' : 'Opprett konto', exact: true}).click()
  await page.waitForURL('**/banner-builder/ai?dr=305')
  await page.locator('#personName').waitFor()
  assert.equal(await page.locator('#personName').inputValue(), 'Edited before sign-up')
  assert.equal(await page.locator('#themeDescription').inputValue(), 'Edited theme before sign-up')
  await selectedPreview.waitFor({ state: 'visible' })
  assert.equal(activations, 1)
  assert.equal(claims, 1)
  assert.equal(await page.evaluate(() => localStorage.getItem('ai_banner_guest_305')), null)
  assert.equal(approvals, 0, 'Auth returns to the wizard without automatically continuing')
  await page.getByRole('button', {name: 'Gå videre', exact: true}).click()
  await page.getByRole('button', {name: /Legg i handlekurv/}).waitFor({ state: 'visible' })
  assert.equal(approvals, 1)
  console.log(`PASS guest alternative switching/reload, ${login ? 'login' : 'signup'} return to edited form + selected image, and Continue to customization`)
} finally { await browser.close() }
