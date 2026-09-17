// Standalone targeted browser smoke; not the project test suite. Start Vite on :10000 first.
import { chromium } from '../../e2e/node_modules/playwright/index.mjs'
import assert from 'node:assert/strict'
const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage()
  let polls = 0
  let claims = 0
  let authenticated = false
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (!path.startsWith("/api/")) return route.continue()
    let data = []
    if (path === '/api/templates') data = [{ id: 1, category: 'Birthday', nameNb: 'Bursdag', nameEn: 'Birthday', sortOrder: 1 }]
    if (path.endsWith('/auth/register') || path.endsWith('/auth/login')) {
      authenticated = true
      data = { accessToken: 'smoke', refreshToken: 'smoke', user: { id: 305, name: 'Guest', email: 'guest@example.invalid', role: 'Customer' } }
    }
    if (path.endsWith('/design-requests/305/claim')) { claims++; data = {} }
    if (path.endsWith('/ai-credits/me')) data = { creditsRemaining: 5, hasUsedFreeGeneration: false }

    if (path.endsWith('/design-requests/ai')) data = { designRequestId: 305, requiresAuth: true, creditsRemaining: 0 }
    if (path.endsWith('/design-requests/305')) {
      polls++
      data = { id: 305, userId: authenticated ? 305 : null, status: 'AwaitingApproval', mode: 'Ai', previewUrl: '/guest-preview.svg', generationHistory: [], personName: 'Guest 305', textContent: 'Happy birthday', themeDescription: 'Stars', bannerTemplateId: 1, language: 'nb', aspectRatio: '16:9' }
    }
    await route.fulfill({ json: data })
  })
  await page.route('**/guest-preview.svg', route => route.fulfill({contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="90"><rect width="160" height="90" fill="gold"/></svg>'}))
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
  assert.equal(state.preview, '/guest-preview.svg')
  assert.ok(polls > 0)
  assert.ok(signup.includes('redirect='), 'Login -> register must preserve wizard return URL')
  await page.goto('http://localhost:10000/banner-builder/ai?dr=305')
  await page.locator('#personName').waitFor()
  assert.equal(await page.locator('#personName').inputValue(), 'Guest 305')
  const preview = page.locator('img[src="/guest-preview.svg"]').first()
  await preview.waitFor({ state: 'visible' })
  assert.ok(await preview.evaluate(img => img.complete && img.naturalWidth > 0), 'Guest preview is actually loaded')
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
  await page.locator('#name').fill('Guest')
  await page.locator('#email').fill('guest@example.invalid')
  await page.locator('#password').fill('SmokePassword123!')
  await page.locator('#confirm-password').fill('SmokePassword123!')
  await page.getByRole('button', {name: 'Opprett konto', exact: true}).click()
  await page.waitForURL('**/banner-builder/ai?dr=305')
  await page.locator('#personName').waitFor()
  assert.equal(await page.locator('#personName').inputValue(), 'Edited before sign-up')
  assert.equal(await page.locator('#themeDescription').inputValue(), 'Edited theme before sign-up')
  await preview.waitFor({ state: 'visible' })
  assert.equal(claims, 1)
  assert.equal(await page.evaluate(() => localStorage.getItem('ai_banner_guest_305')), null)
  console.log('PASS guest preview, login/register switching, claim, and return to same edited wizard')
} finally { await browser.close() }
