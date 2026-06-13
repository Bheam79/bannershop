/**
 * Visual-regression screenshot suite — checkout flow and order detail pages.
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
 * └─────────────────────────────────────────────────────────────────────────┘
 *
 * Strategy:
 *  1. The checkout flow requires an authenticated user and a banner in the cart.
 *     We use the API helpers to seed the user/order rather than driving the
 *     full purchase wizard, keeping screenshots stable regardless of upstream
 *     pricing data.
 *  2. Admin order detail captures the fully-styled order management panel.
 *  3. Screenshots use toHaveScreenshot() with `fullPage: true`.
 *
 * Dynamic / unstable content (timestamps, order IDs) is masked where practical
 * using the `mask` option so that cosmetic changes don't cause false diffs.
 */
import { test, expect } from '../helpers/fixtures'
import {
  loginViaApi,
  getAdminEmail,
  getAdminPassword,
  getTestUserEmail,
  getTestUserPassword,
} from '../helpers/auth'
import {
  apiLogin,
  apiRegister,
  apiCreateOrder,
  apiFetchSizes,
  apiAdvanceOrderToInProduction,
} from '../helpers/api'

// ─── Helper ───────────────────────────────────────────────────────────────────

async function waitForPageReady(page: import('@playwright/test').Page): Promise<void> {
  await page.waitForLoadState('networkidle')
  await page.evaluate(() => {
    const spinners = document.querySelectorAll<HTMLElement>(
      '[class*="spinner"], [class*="loader"], [class*="loading"], [class*="skeleton"]',
    )
    spinners.forEach((el) => (el.style.visibility = 'hidden'))
  })
}

// ─── Checkout flow ─────────────────────────────────────────────────────────────

test.describe('Visual — checkout flow', () => {
  test('checkout shipping + payment form', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let userToken: string
    try {
      const auth = await apiLogin(email, password)
      userToken = auth.accessToken
    } catch {
      try {
        const auth = await apiRegister(email, password, 'Test Bruker')
        userToken = auth.accessToken
      } catch {
        test.skip(true, 'Test user unavailable — skipping visual test')
        return
      }
    }

    // Fetch a valid size so we can reach the checkout page
    const sizes = await apiFetchSizes().catch(() => [] as Awaited<ReturnType<typeof apiFetchSizes>>)
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active banner sizes — skipping visual test')
      return
    }

    // Create a draft order via API (skips the banner-builder steps)
    let orderId: number
    try {
      const order = await apiCreateOrder(userToken, { bannerSizeId: size.id })
      orderId = order.orderId
    } catch {
      test.skip(true, 'Could not create test order — skipping visual test')
      return
    }

    // Inject auth and navigate to the pay page
    await loginViaApi(page, email, password)
    await page.goto(`/account/orders/${orderId}/pay`)
    await waitForPageReady(page)

    // Mask the dynamic order ID so the screenshot is stable across runs
    const masks = [page.locator('text=/Ordre #\\d+/')]

    await expect(page).toHaveScreenshot('checkout-payment-view.png', {
      fullPage: true,
      mask: masks,
    })
  })

  test('account order detail — pending payment', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let userToken: string
    try {
      const auth = await apiLogin(email, password)
      userToken = auth.accessToken
    } catch {
      try {
        const auth = await apiRegister(email, password, 'Test Bruker')
        userToken = auth.accessToken
      } catch {
        test.skip(true, 'Test user unavailable — skipping visual test')
        return
      }
    }

    const sizes = await apiFetchSizes().catch(() => [] as Awaited<ReturnType<typeof apiFetchSizes>>)
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active banner sizes — skipping visual test')
      return
    }

    let orderId: number
    try {
      const order = await apiCreateOrder(userToken, { bannerSizeId: size.id })
      orderId = order.orderId
    } catch {
      test.skip(true, 'Could not create test order — skipping visual test')
      return
    }

    await loginViaApi(page, email, password)
    await page.goto(`/account/orders/${orderId}`)
    await waitForPageReady(page)

    await expect(page).toHaveScreenshot('checkout-order-detail-pending.png', {
      fullPage: true,
      mask: [
        page.locator('text=/Ordre #\\d+/'),
        page.locator('text=/\\d{2}\\.\\d{2}\\.\\d{4}/'), // date stamps
      ],
    })
  })
})

// ─── Admin order detail ───────────────────────────────────────────────────────

test.describe('Visual — admin order detail', () => {
  test('admin order detail — in production', async ({ page }) => {
    const adminEmail = getAdminEmail()
    const adminPassword = getAdminPassword()
    const userEmail = getTestUserEmail()
    const userPassword = getTestUserPassword()

    let adminToken: string
    let userToken: string

    try {
      const auth = await apiLogin(adminEmail, adminPassword)
      adminToken = auth.accessToken
    } catch {
      test.skip(true, 'Admin user unavailable — skipping visual test')
      return
    }

    try {
      const auth = await apiLogin(userEmail, userPassword)
      userToken = auth.accessToken
    } catch {
      try {
        const auth = await apiRegister(userEmail, userPassword, 'Test Bruker')
        userToken = auth.accessToken
      } catch {
        test.skip(true, 'Test user unavailable — skipping visual test')
        return
      }
    }

    const sizes = await apiFetchSizes().catch(() => [] as Awaited<ReturnType<typeof apiFetchSizes>>)
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active banner sizes — skipping visual test')
      return
    }

    let orderId: number
    try {
      const order = await apiCreateOrder(userToken, { bannerSizeId: size.id })
      orderId = order.orderId
    } catch {
      test.skip(true, 'Could not create test order — skipping visual test')
      return
    }

    // Advance to InProduction so all admin controls are visible
    await apiAdvanceOrderToInProduction(adminToken, orderId)

    // Inject admin auth and navigate
    await loginViaApi(page, adminEmail, adminPassword)
    await page.goto(`/admin/orders/${orderId}`)
    await page.waitForSelector('h1', { timeout: 10_000 })
    await waitForPageReady(page)

    await expect(page).toHaveScreenshot('admin-order-detail-in-production.png', {
      fullPage: true,
      mask: [
        page.locator('text=/Ordre #\\d+/'),
        page.locator('text=/\\d{2}\\.\\d{2}\\.\\d{4}/'), // date stamps
      ],
    })
  })
})
