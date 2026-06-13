/**
 * Visual-regression screenshot suite — public, customer, and admin pages.
 *
 * ┌─────────────────────────────────────────────────────────────────────────┐
 * │  This spec belongs to the 'visual-regression' Playwright project.       │
 * │  It does NOT run during normal CI.  Run it explicitly:                  │
 * │                                                                         │
 * │  # Create / update baseline snapshots (first run or intentional update) │
 * │  npx playwright test --project=visual-regression --update-snapshots     │
 * │                                                                         │
 * │  # Compare against committed baselines                                  │
 * │  npx playwright test --project=visual-regression                        │
 * │                                                                         │
 * │  Snapshots live in e2e/snapshots/ — commit them alongside the spec.     │
 * └─────────────────────────────────────────────────────────────────────────┘
 *
 * Each test navigates to a page, waits for the key content to be visible,
 * then calls toHaveScreenshot() which:
 *   – on first run with --update-snapshots: writes the PNG baseline
 *   – on subsequent runs: pixel-diffs against the baseline and fails on
 *     significant changes
 *
 * Viewport is fixed at 1280×720 (set in playwright.config.ts → visual-regression
 * project) to ensure reproducible screenshots across machines.
 *
 * Auth strategy: pages that require login use loginViaApi (fast, no UI).
 * If auth fails the test is skipped rather than failing the whole suite —
 * the screenshot is not meaningful when credentials are missing.
 */
import { test, expect } from '../helpers/fixtures'
import { loginViaApi, getAdminEmail, getAdminPassword, getTestUserEmail, getTestUserPassword } from '../helpers/auth'
import { apiLogin, apiRegister } from '../helpers/api'

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Dismiss cookie banners or loading overlays that might obscure content. */
async function waitForPageReady(page: import('@playwright/test').Page): Promise<void> {
  await page.waitForLoadState('networkidle')
  // Hide any spinning loaders to avoid flaky diffs
  await page.evaluate(() => {
    const spinners = document.querySelectorAll<HTMLElement>(
      '[class*="spinner"], [class*="loader"], [class*="loading"], [class*="skeleton"]',
    )
    spinners.forEach((el) => (el.style.visibility = 'hidden'))
  })
}

// ─── Public pages ─────────────────────────────────────────────────────────────

test.describe('Visual — public pages', () => {
  test('home page', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('h1', { timeout: 10_000 })
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('public-home.png', { fullPage: true })
  })

  test('login page', async ({ page }) => {
    await page.goto('/login')
    await page.waitForSelector('form', { timeout: 10_000 })
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('public-login.png', { fullPage: true })
  })

  test('register page', async ({ page }) => {
    await page.goto('/register')
    await page.waitForSelector('form', { timeout: 10_000 })
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('public-register.png', { fullPage: true })
  })

  test('AI banner builder — initial step', async ({ page }) => {
    await page.goto('/banner-builder/ai')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('public-banner-builder-ai.png', { fullPage: true })
  })

  test('manual banner builder — initial step', async ({ page }) => {
    await page.goto('/banner-builder/manual')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('public-banner-builder-manual.png', { fullPage: true })
  })

  test('cart page (empty)', async ({ page }) => {
    await page.goto('/cart')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('public-cart-empty.png', { fullPage: true })
  })
})

// ─── Customer pages (authenticated) ──────────────────────────────────────────

test.describe('Visual — customer pages', () => {
  test.beforeEach(async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()
    try {
      await loginViaApi(page, email, password)
    } catch {
      try {
        await apiRegister(email, password, 'Test Bruker')
        await loginViaApi(page, email, password)
      } catch {
        test.skip(true, 'Test user unavailable — skipping visual test')
      }
    }
  })

  test('account overview', async ({ page }) => {
    await page.goto('/account')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('customer-account.png', { fullPage: true })
  })

  test('account orders list', async ({ page }) => {
    await page.goto('/account/orders')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('customer-orders.png', { fullPage: true })
  })

  test('account design requests', async ({ page }) => {
    await page.goto('/account/design-requests')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('customer-design-requests.png', { fullPage: true })
  })
})

// ─── Admin pages ──────────────────────────────────────────────────────────────

test.describe('Visual — admin pages', () => {
  test.beforeEach(async ({ page }) => {
    const email = getAdminEmail()
    const password = getAdminPassword()
    try {
      await apiLogin(email, password)
    } catch {
      test.skip(true, 'Admin user unavailable — skipping visual test')
      return
    }
    await loginViaApi(page, email, password)
  })

  test('admin dashboard', async ({ page }) => {
    await page.goto('/admin')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-dashboard.png', { fullPage: true })
  })

  test('admin sizes', async ({ page }) => {
    await page.goto('/admin/sizes')
    // Wait for table rows to render
    await page.waitForSelector('table tbody tr', { timeout: 10_000 }).catch(() => null)
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-sizes.png', { fullPage: true })
  })

  test('admin pricing', async ({ page }) => {
    await page.goto('/admin/pricing')
    await page.waitForSelector('table tbody tr', { timeout: 10_000 }).catch(() => null)
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-pricing.png', { fullPage: true })
  })

  test('admin orders', async ({ page }) => {
    await page.goto('/admin/orders')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-orders.png', { fullPage: true })
  })

  test('admin settings', async ({ page }) => {
    await page.goto('/admin/settings')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-settings.png', { fullPage: true })
  })

  test('admin design requests', async ({ page }) => {
    await page.goto('/admin/design-requests')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-design-requests.png', { fullPage: true })
  })

  test('admin materials', async ({ page }) => {
    await page.goto('/admin/materials')
    await page.waitForLoadState('networkidle')
    await waitForPageReady(page)
    await expect(page).toHaveScreenshot('admin-materials.png', { fullPage: true })
  })
})
