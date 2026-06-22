/**
 * Retry-payment flow test suite (BANNERSH-278).
 *
 * Covers RetryPaymentView.vue (/account/orders/:id/pay) which is the dedicated
 * UI for the BANNERSH-185 "Betal nå" (pay now) feature — lets customers retry
 * payment on unpaid orders. Also covers the "Slett" (delete) action in
 * OrdersView that soft-deletes an unpaid order.
 *
 * Uses the mock Stripe flow: when the backend returns a `pi_mock_…` client
 * secret, RetryPaymentView skips real Stripe and navigates directly to the
 * confirmation page (same as PaymentView in checkout.spec.ts).
 */
import { test, expect } from '../helpers/fixtures'
import { loginViaApi, getTestUserEmail, getTestUserPassword } from '../helpers/auth'
import { apiCreateUnpaidDraftOrder, apiLogin, apiFetchSizes } from '../helpers/api'

test.describe('Retry payment flow', () => {
  // ── Test 1: Happy path — mock payment → confirmation ───────────────────────

  test('happy path — navigate to /pay, submit mock payment, land on confirmation', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let auth: Awaited<ReturnType<typeof apiLogin>>
    try {
      auth = await apiLogin(email, password)
    } catch {
      test.skip(true, 'Test user not available — skipping retry-payment happy-path test')
      return
    }

    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active standard sizes found — skipping test')
      return
    }

    const order = await apiCreateUnpaidDraftOrder(auth.accessToken, { bannerSizeId: size.id })

    if (!order.clientSecret.startsWith('pi_mock_')) {
      test.skip(true, 'Real Stripe configured — skipping mock-payment happy-path test')
      return
    }

    // Inject auth and navigate to the retry-payment view
    await loginViaApi(page, email, password)
    await page.goto(`/account/orders/${order.orderId}/pay`)

    // The heading and pay button should be visible after load
    await expect(page.getByRole('heading', { name: /Fullfør betaling/i })).toBeVisible({
      timeout: 10_000,
    })
    await expect(page.getByRole('button', { name: /Betal/i })).toBeVisible()

    // Clicking "Betal" on a pi_mock_* order routes directly to confirmation
    await page.getByRole('button', { name: /Betal/i }).click()

    await page.waitForURL(`**/checkout/confirmation/${order.orderId}`, { timeout: 10_000 })
    await expect(page.locator('text=Tusen takk for din bestilling')).toBeVisible({ timeout: 10_000 })
  })

  // ── Test 2: Already-paid redirect ──────────────────────────────────────────

  test('already-paid order — /pay redirects straight to confirmation', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let auth: Awaited<ReturnType<typeof apiLogin>>
    try {
      auth = await apiLogin(email, password)
    } catch {
      test.skip(true, 'Test user not available — skipping already-paid redirect test')
      return
    }

    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active standard sizes found — skipping test')
      return
    }

    const order = await apiCreateUnpaidDraftOrder(auth.accessToken, { bannerSizeId: size.id })

    // Mark the order Paid via the test-only mock-pay endpoint
    const mockPayRes = await page.request.post(
      `/api/orders/${order.orderId}/mock-pay`,
      {
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${auth.accessToken}`,
        },
        data: { password: 'test1234' },
      },
    )

    if (!mockPayRes.ok()) {
      test.skip(true, 'Mock payment endpoint not available — skipping already-paid redirect test')
      return
    }

    // Inject auth and navigate to /pay — should redirect to confirmation immediately
    await loginViaApi(page, email, password)
    await page.goto(`/account/orders/${order.orderId}/pay`)

    await page.waitForURL(`**/checkout/confirmation/${order.orderId}`, { timeout: 10_000 })
  })

  // ── Test 3: Deleted / not-found order shows error state ───────────────────

  test('non-existent order — shows error state with back link', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    // We need auth so the guard doesn't redirect us to /login first
    try {
      await loginViaApi(page, email, password)
    } catch {
      // If login fails we still want to test, as long as we have *some* auth injected;
      // use a dummy token approach — the backend 404 will surface regardless
      // Actually without a valid token the router auth guard redirects to /login.
      test.skip(true, 'Test user not available — skipping not-found error state test')
      return
    }

    await page.goto('/account/orders/99999/pay')

    // RetryPaymentView: when retryOrderPayment throws a 404, loadError is set to
    // "Ordren finnes ikke (eller er slettet)."
    await expect(page.locator('.alert-error')).toBeVisible({ timeout: 10_000 })
    await expect(
      page.locator('.alert-error').filter({ hasText: /finnes ikke|slettet/i }),
    ).toBeVisible()

    // Stripe card element must NOT appear in the error state
    await expect(page.locator('.stripe-mount')).not.toBeVisible()

    // "Tilbake til ordrelisten" link in the error banner
    await expect(page.getByRole('link', { name: /Tilbake til ordrelisten/i })).toBeVisible()
  })

  // ── Test 4: Unauthenticated access → redirect to /login ───────────────────

  test('unauthenticated — visiting /pay redirects to /login', async ({ page }) => {
    // Navigate without any auth tokens
    await page.goto('/account/orders/1/pay')

    // The Vue router auth guard should redirect to /login
    await page.waitForURL(/\/login/, { timeout: 10_000 })
  })

  // ── Test 5: "Slett" (delete) in OrdersView removes the row ───────────────

  test('Slett button in OrdersView soft-deletes the order and removes the row', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let auth: Awaited<ReturnType<typeof apiLogin>>
    try {
      auth = await apiLogin(email, password)
    } catch {
      test.skip(true, 'Test user not available — skipping Slett test')
      return
    }

    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active standard sizes found — skipping test')
      return
    }

    // Create a deletable (Draft/PendingPayment) order
    const order = await apiCreateUnpaidDraftOrder(auth.accessToken, { bannerSizeId: size.id })

    // Inject auth then navigate to orders list
    await loginViaApi(page, email, password)
    await page.goto('/account/orders')

    // Wait for the orders list to load (the row for our order should be visible)
    const orderRowText = `#O-${order.orderId}`
    await expect(page.locator(`text=${orderRowText}`)).toBeVisible({ timeout: 10_000 })

    // Auto-accept the confirm() dialog that OrdersView shows before deleting
    page.on('dialog', (dialog) => dialog.accept())

    // Click the "Slett" button for this specific order row.
    // The button has title="Slett ordre #O-{id}" — use that for precise targeting.
    const deleteBtn = page.locator(`button[title="Slett ordre ${orderRowText}"]`)
    // Fall back to any visible "Slett" button if the title attribute is slightly different
    const fallbackDeleteBtn = page
      .locator('button.action-btn--delete')
      .filter({ hasText: /Slett/i })
      .first()

    const btnToClick = (await deleteBtn.count()) > 0 ? deleteBtn : fallbackDeleteBtn
    await btnToClick.click()

    // The row must disappear from the list within 5 s (allItems reactive filter)
    await expect(page.locator(`text=${orderRowText}`)).not.toBeVisible({ timeout: 5_000 })
  })
})
