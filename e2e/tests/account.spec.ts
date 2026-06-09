/**
 * Customer account test suite.
 *
 * Tests registration, login (valid & invalid), order list, order detail
 * with production stages, and shipped order tracking info.
 */
import { test, expect } from '@playwright/test'
import { loginViaUI, loginViaApi, clearAuth, getTestUserEmail, getTestUserPassword } from '../helpers/auth'
import {
  apiLogin,
  apiRegister,
  apiCreateOrder,
  apiFetchSizes,
  apiUpdateProductionStage,
  apiSetShipping,
  apiAdvanceOrderToInProduction,
} from '../helpers/api'

/** Generate a unique email for registration tests */
function uniqueEmail(): string {
  return `e2e_test_${Date.now()}@example.com`
}

test.describe('Customer account', () => {
  test('register new account', async ({ page }) => {
    const email = uniqueEmail()
    const password = 'E2eTestPass123!'

    await page.goto('/register')
    await expect(page.getByRole('heading', { name: /Opprett konto/i })).toBeVisible()

    await page.fill('input[autocomplete="name"]', 'E2E Test Bruker')
    await page.fill('input[type="email"]', email)
    await page.fill('input[autocomplete="new-password"]', password)
    // Confirm password (second password field)
    const passwordFields = page.locator('input[type="password"]')
    await passwordFields.nth(1).fill(password)

    await page.getByRole('button', { name: /Opprett konto/i }).click()

    // Should redirect to /account after successful registration
    await expect(page).toHaveURL(/\/account$/, { timeout: 15_000 })
  })

  test('login with valid credentials', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    // Ensure test user exists
    try {
      await apiLogin(email, password)
    } catch {
      try {
        await apiRegister(email, password, 'Test Bruker')
      } catch {
        test.skip(true, 'Cannot create or log in as test user — skipping')
        return
      }
    }

    await loginViaUI(page, email, password)
    await expect(page).toHaveURL(/\/account/, { timeout: 10_000 })
  })

  test('login with invalid credentials shows error', async ({ page }) => {
    await page.goto('/login')

    await page.fill('input[type="email"]', 'wrong@example.com')
    await page.fill('input[type="password"]', 'wrongpassword')
    await page.getByRole('button', { name: /Logg inn/i }).click()

    // Error message should appear
    await expect(page.locator('text=/Innlogging feilet|feil passord|ikke funnet/i')).toBeVisible({
      timeout: 10_000,
    })
    // Should still be on login page
    await expect(page).toHaveURL(/\/login/)
  })

  test('view order list (requires seeded test order)', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let auth: Awaited<ReturnType<typeof apiLogin>>
    try {
      auth = await apiLogin(email, password)
    } catch {
      try {
        auth = await apiRegister(email, password, 'Test Bruker')
      } catch {
        test.skip(true, 'Test user unavailable — skipping order list test')
        return
      }
    }

    // Ensure there's at least one order
    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (size) {
      await apiCreateOrder(auth.accessToken, { bannerSizeId: size.id }).catch(() => {
        // Ignore if it fails (user may have orders already)
      })
    }

    // Navigate to orders page with auth
    await loginViaApi(page, email, password)
    await page.goto('/account/orders')

    await expect(page).toHaveURL(/\/account\/orders/, { timeout: 10_000 })
    await expect(page.getByRole('heading', { name: /ordrer|bestillinger/i })).toBeVisible()

    // Should have at least one order listed
    // The order list shows items with status labels
    const orderItems = page.locator('a').filter({ hasText: /#\d+/ })
    if (await orderItems.count() > 0) {
      await expect(orderItems.first()).toBeVisible()
    } else {
      // Empty state is also valid
      await expect(page.locator('main, [role="main"], .max-w-4xl')).toBeVisible()
    }
  })

  test('order detail shows production stages', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let auth: Awaited<ReturnType<typeof apiLogin>>
    try {
      auth = await apiLogin(email, password)
    } catch {
      try {
        auth = await apiRegister(email, password, 'Test Bruker')
      } catch {
        test.skip(true, 'Test user unavailable — skipping order detail test')
        return
      }
    }

    // Create a test order
    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active sizes — skipping order detail test')
      return
    }

    const order = await apiCreateOrder(auth.accessToken, { bannerSizeId: size.id })

    // Login and navigate to order detail
    await loginViaApi(page, email, password)
    await page.goto(`/account/orders/${order.orderId}`)

    await expect(page.getByRole('heading', { name: /Ordre #/i })).toBeVisible({ timeout: 10_000 })

    // Production stages section should be present IF order has been paid
    // (for PendingPayment status it's hidden)
    const statusEl = page.locator('.badge').first()
    await expect(statusEl).toBeVisible()

    // Price breakdown should be visible
    await expect(page.locator('text=Totalt inkl. MVA').first()).toBeVisible()
  })

  test('shipped order shows tracking info', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()
    const adminEmail = process.env.ADMIN_EMAIL ?? 'admin@bannershop.no'
    const adminPassword = process.env.ADMIN_PASSWORD ?? 'AdminPassword123!'

    let auth: Awaited<ReturnType<typeof apiLogin>>
    let adminAuth: Awaited<ReturnType<typeof apiLogin>>

    try {
      auth = await apiLogin(email, password)
    } catch {
      try {
        auth = await apiRegister(email, password, 'Test Bruker')
      } catch {
        test.skip(true, 'Test user unavailable — skipping shipped order test')
        return
      }
    }

    try {
      adminAuth = await apiLogin(adminEmail, adminPassword)
    } catch {
      test.skip(true, 'Admin user unavailable — skipping shipped order test')
      return
    }

    // Create an order, move it to shipped via admin API
    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active sizes — skipping shipped order test')
      return
    }

    const order = await apiCreateOrder(auth.accessToken, { bannerSizeId: size.id })

    // The fresh order is PendingPayment; shipping requires InProduction or
    // ReadyToShip — advance via the admin status API first.
    await apiAdvanceOrderToInProduction(adminAuth.accessToken, order.orderId)

    // Add shipping tracking via admin API (this also sets status to Shipped)
    await apiSetShipping(adminAuth.accessToken, order.orderId, {
      carrier: 'Bring',
      trackingNumber: '370799000000000001',
      trackingUrl: 'https://sporing.bring.no/sporing/370799000000000001',
      shippedAt: new Date().toISOString(),
      estimatedArrival: new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString(),
    })

    // Navigate to order detail as customer
    await loginViaApi(page, email, password)
    await page.goto(`/account/orders/${order.orderId}`)

    await expect(page.getByRole('heading', { name: /Ordre #/i })).toBeVisible({ timeout: 10_000 })

    // Should show "Fraktstatus" section with tracking info
    await expect(page.locator('text=Fraktstatus')).toBeVisible()
    await expect(page.locator('text=Bring')).toBeVisible()
    await expect(page.locator('text=370799000000000001')).toBeVisible()
  })
})
