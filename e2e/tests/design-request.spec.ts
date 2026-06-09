/**
 * Design-request end-to-end test suite.
 *
 * Covers 7 scenarios:
 * 1. Unauthenticated redirect — /account/design-requests → /login
 * 2. AI builder form loads with template cards and allows stepping through
 * 3. API-seeded AI request appears in the customer design-request list
 * 4. Design request detail shows InProgress status (seeded via admin API)
 * 5. Customer approves an AI request in AwaitingApproval status
 * 6. Customer requests a revision on a Manual AwaitingApproval request
 * 7. Admin sees the request in the list and can update its status
 *
 * NOTE on payment simulation:
 * MockStripePaymentService.VerifyAndParseEvent always returns null, so the
 * Stripe webhook cannot advance status. All status progression is done via
 * the admin status API (apiAdminUpdateDesignRequestStatus).
 */
import { test, expect } from '@playwright/test'
import {
  injectAuth,
  loginViaApi,
  getTestUserEmail,
  getTestUserPassword,
  getAdminEmail,
  getAdminPassword,
} from '../helpers/auth'
import {
  apiLogin,
  apiRegister,
  apiCreateAiDesignRequest,
  apiCreateManualDesignRequest,
  apiAdminUpdateDesignRequestStatus,
  type LoginResponse,
} from '../helpers/api'

// ─── Local helpers ─────────────────────────────────────────────────────────────

/**
 * Log in as the shared test user, registering if the account doesn't exist yet.
 * Returns the LoginResponse. Throws on failure (caller should call test.skip).
 */
async function ensureTestUser(): Promise<LoginResponse> {
  const email = getTestUserEmail()
  const password = getTestUserPassword()
  try {
    return await apiLogin(email, password)
  } catch {
    return await apiRegister(email, password, 'Test Bruker')
  }
}

/**
 * Log in as the admin user.
 * Throws if admin credentials are unavailable (caller should call test.skip).
 */
async function ensureAdminUser(): Promise<LoginResponse> {
  return await apiLogin(getAdminEmail(), getAdminPassword())
}

/**
 * Register a fresh single-use user for a single test invocation.
 *
 * BANNERSH-67 introduced per-user free-AI-generation tracking: a user's first
 * AI request is free, subsequent ones require credits. The shared
 * `testuser@example.com` account flips `HasUsedFreeAiGeneration=true` after the
 * first run, so reusing it across tests yields 402 `insufficient_credits`.
 * Seeding via a throwaway user per invocation gives every test its own fresh
 * free-generation budget without depending on DB cleanup between runs.
 */
async function registerThrowawayUser(): Promise<LoginResponse> {
  const stamp = `${Date.now()}_${Math.floor(Math.random() * 1_000_000)}`
  const email = `e2e_seed_${stamp}@example.com`
  return await apiRegister(email, 'E2eSeedPass123!', 'E2E Seed User')
}

/**
 * Create an AI design request as a fresh throwaway user, then optionally
 * advance it to the given status via the admin API.
 * Returns the designRequestId and the throwaway user's LoginResponse so the
 * caller can `injectAuth(page, userAuth)` to act as the owner.
 */
async function seedAiRequestInStatus(
  adminToken: string,
  status: string,
  templateId = 1,
): Promise<{ designRequestId: number; userAuth: LoginResponse }> {
  const userAuth = await registerThrowawayUser()
  const { designRequestId } = await apiCreateAiDesignRequest(userAuth.accessToken, {
    templateId,
    personName: 'E2E Test Person',
    textContent: 'Gratulerer!',
    themeDescription: 'Festlig',
  })
  if (status !== 'Pending') {
    await apiAdminUpdateDesignRequestStatus(adminToken, designRequestId, status)
  }
  return { designRequestId, userAuth }
}

/**
 * Create a Manual design request as a fresh throwaway user, then optionally
 * advance it to the given status via the admin API.
 * Returns the designRequestId and the throwaway user's LoginResponse.
 *
 * NOTE: canRequestRevision requires mode=Manual, status=AwaitingApproval,
 * revisionCount<1. Seeding with this helper leaves revisionCount at 0.
 *
 * Manual requests aren't gated by AI credits but we still use a throwaway user
 * for symmetry and per-test isolation.
 */
async function seedManualRequestInStatus(
  adminToken: string,
  status: string,
  templateId = 1,
): Promise<{ designRequestId: number; userAuth: LoginResponse }> {
  const userAuth = await registerThrowawayUser()
  const { designRequestId } = await apiCreateManualDesignRequest(userAuth.accessToken, {
    templateId,
    personName: 'E2E Test Person',
    textContent: 'Gratulerer!',
    themeDescription: 'Festlig',
  })
  if (status !== 'Pending') {
    await apiAdminUpdateDesignRequestStatus(adminToken, designRequestId, status)
  }
  return { designRequestId, userAuth }
}

// ─── Tests ─────────────────────────────────────────────────────────────────────

test.describe('Design requests', () => {
  // ── Test 1 ──────────────────────────────────────────────────────────────────

  test('1: unauthenticated user visiting /account/design-requests is redirected to login', async ({ page }) => {
    await page.goto('/account/design-requests')
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 })
  })

  // ── Test 2 ──────────────────────────────────────────────────────────────────

  test('2: AI builder form loads with template cards and allows stepping through', async ({ page }) => {
    try {
      await ensureTestUser()
    } catch {
      test.skip(true, 'Test user unavailable — skipping')
      return
    }

    await loginViaApi(page, getTestUserEmail(), getTestUserPassword())
    await page.goto('/banner-builder/ai')

    // Step 1: template cards must load
    await expect(page.locator('.tpl-card').first()).toBeVisible({ timeout: 15_000 })
    const cardCount = await page.locator('.tpl-card').count()
    expect(cardCount).toBeGreaterThanOrEqual(3)

    // At least one card should contain the Norwegian name for Birthday
    const bursdagCard = page.locator('.tpl-card').filter({ hasText: 'Bursdag' })
    await expect(bursdagCard.first()).toBeVisible()

    // Templates auto-select the first — step 2 button should already be enabled
    const nextStep2Btn = page.getByRole('button', { name: /Neste: Tilpass/i })
    await expect(nextStep2Btn).toBeEnabled()

    // Navigate to step 2
    await nextStep2Btn.click()

    // Step 2: personName input should appear
    const personNameInput = page.locator('#personName')
    await expect(personNameInput).toBeVisible({ timeout: 10_000 })

    // Fill all required fields
    await personNameInput.fill('E2E Test Person')
    await page.locator('#textContent').fill('Gratulerer med dagen!')
    await page.locator('#themeDescription').fill('Festlig og fargerik')

    // Step 3 button should be enabled after filling required fields.
    // BANNERSH-70: button text changed from 'Neste: Se over og betal' to 'Neste: Generer'
    // (the free-first flow generates without an upfront Stripe step).
    const nextStep3Btn = page.getByRole('button', { name: /Neste: Generer/i })
    await expect(nextStep3Btn).toBeEnabled()

    // Navigate to step 3
    await nextStep3Btn.click()

    // Step 3: the free-first "Generer banner gratis" CTA must appear (no Stripe upfront).
    await expect(page.getByRole('button', { name: /Generer banner gratis/i })).toBeVisible({
      timeout: 10_000,
    })
  })

  // ── Test 3 ──────────────────────────────────────────────────────────────────

  test('3: API-seeded AI request appears in the customer design-request list', async ({ page }) => {
    // Use a throwaway user so the free AI generation is always available
    // (the shared testuser would 402 after its first successful run).
    let userAuth: LoginResponse
    try {
      userAuth = await registerThrowawayUser()
    } catch {
      test.skip(true, 'Could not register throwaway user — skipping')
      return
    }

    // Seed a design request via the API (stays in Pending status)
    const { designRequestId } = await apiCreateAiDesignRequest(userAuth.accessToken, {
      templateId: 1,
      personName: 'E2E Test Person',
      textContent: 'Gratulerer!',
      themeDescription: 'Festlig',
    })

    // Authenticate as the seed user and navigate to the customer list
    await page.goto('/')
    await injectAuth(page, userAuth)
    await page.goto('/account/design-requests')

    // Wait for the newly created entry to appear (with enough timeout for API latency)
    await expect(page.locator(`text=#${designRequestId}`).first()).toBeVisible({
      timeout: 15_000,
    })

    // Find the row (desktop table row or mobile list item) and check for AI badge
    const row = page.locator('tr, li.mobile-row').filter({ hasText: `#${designRequestId}` }).first()
    await expect(row.locator('.badge-ai')).toBeVisible()
  })

  // ── Test 4 ──────────────────────────────────────────────────────────────────

  test('4: design request detail page shows InProgress status text', async ({ page }) => {
    let adminAuth: LoginResponse

    try {
      adminAuth = await ensureAdminUser()
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    // BANNERSH-122: use a Manual request to avoid the race between the
    // background AI pipeline and the admin status update. AI requests are
    // created with Status=InProgress and immediately processed by
    // AiGenerationPipeline (which advances them to AwaitingApproval via the
    // instant MockAiImageService), so by the time the page loads the status
    // text would be "Klar til godkjenning" instead of "Designet er under
    // arbeid". Manual requests have no background pipeline, so the admin's
    // InProgress status sticks.
    const { designRequestId: drId, userAuth } = await seedManualRequestInStatus(
      adminAuth.accessToken,
      'InProgress',
    )

    await page.goto('/')
    await injectAuth(page, userAuth)
    await page.goto(`/account/design-requests/${drId}`)

    // Page heading includes the request ID
    await expect(page.locator(`text=Design-bestilling #${drId}`)).toBeVisible({
      timeout: 10_000,
    })

    // InProgress status block title
    await expect(page.locator('text=Designet er under arbeid')).toBeVisible({ timeout: 10_000 })
  })

  // ── Test 5 ──────────────────────────────────────────────────────────────────

  test('5: customer can approve an AI request in AwaitingApproval status', async ({ page }) => {
    let adminAuth: LoginResponse

    try {
      adminAuth = await ensureAdminUser()
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    const { designRequestId: drId, userAuth } = await seedAiRequestInStatus(
      adminAuth.accessToken,
      'AwaitingApproval',
    )

    await page.goto('/')
    await injectAuth(page, userAuth)
    await page.goto(`/account/design-requests/${drId}`)

    // 'Godkjenn design' button is shown when status === AwaitingApproval
    const approveBtn = page.getByRole('button', { name: /Godkjenn design/i })
    await expect(approveBtn).toBeVisible({ timeout: 10_000 })

    await approveBtn.click()

    // approveSuccess message set in AccountDesignRequestDetailView.approve()
    await expect(
      page.locator('text=Designet er godkjent! Banneret sendes til produksjon.'),
    ).toBeVisible({ timeout: 15_000 })
  })

  // ── Test 6 ──────────────────────────────────────────────────────────────────

  test('6: customer can request a revision on a Manual AwaitingApproval request', async ({ page }) => {
    let adminAuth: LoginResponse

    try {
      adminAuth = await ensureAdminUser()
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    // canRequestRevision = mode=Manual && status=AwaitingApproval && revisionCount<1
    const { designRequestId: drId, userAuth } = await seedManualRequestInStatus(
      adminAuth.accessToken,
      'AwaitingApproval',
    )

    await page.goto('/')
    await injectAuth(page, userAuth)
    await page.goto(`/account/design-requests/${drId}`)

    // 'Be om korrigering' button appears for Manual+AwaitingApproval+0 revisions
    const revisionBtn = page.getByRole('button', { name: /Be om korrigering/i })
    await expect(revisionBtn).toBeVisible({ timeout: 10_000 })

    // Open the inline revision form
    await revisionBtn.click()

    // Fill the revision comment textarea (class="field-textarea" in the template)
    const textarea = page.locator('textarea.field-textarea')
    await expect(textarea).toBeVisible({ timeout: 5_000 })
    await textarea.fill('Bytt bakgrunnsfargen til blå')

    // Submit
    const submitBtn = page.getByRole('button', { name: /Send korrigeringsønske/i })
    await expect(submitBtn).toBeEnabled()
    await submitBtn.click()

    // revisionSuccess or RevisionRequested status block title should be visible.
    // Use .first() because both the inline success notice and the status block
    // title may render simultaneously (strict-mode would otherwise fail).
    await expect(
      page.locator('text=/Korrigeringsønske er sendt|Korrigering er under behandling/i').first(),
    ).toBeVisible({ timeout: 15_000 })
  })

  // ── Test 7 ──────────────────────────────────────────────────────────────────

  test('7: admin sees design request in list and can update its status', async ({ page }) => {
    let adminAuth: LoginResponse

    try {
      adminAuth = await ensureAdminUser()
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    // Seed a Pending AI request owned by a fresh throwaway user (so the free
    // generation is always available even after previous test runs).
    const { designRequestId: drId } = await seedAiRequestInStatus(
      adminAuth.accessToken,
      'Pending',
    )

    // Log in as admin and navigate to the admin design-requests list
    await loginViaApi(page, getAdminEmail(), getAdminPassword())
    await page.goto('/admin/design-requests')

    // Wait for the seeded request to appear in the table
    await expect(page.locator(`text=#${drId}`)).toBeVisible({ timeout: 15_000 })

    // Click the table row to open the detail page
    await page.locator('tr').filter({ hasText: `#${drId}` }).first().click()

    // Detail page: verify heading
    await expect(
      page.locator(`text=Design-bestilling #${drId}`),
    ).toBeVisible({ timeout: 10_000 })

    // 'Oppdater status' section is always shown on the admin detail page
    await expect(page.locator('text=Oppdater status')).toBeVisible()

    // Select 'InProgress' (value) in the status <select>
    // The current status is Pending which is not an option, so any selection enables the save button
    await page.locator('select').selectOption('InProgress')

    // 'Lagre status' button should be enabled (newStatus !== request.status)
    const saveBtn = page.getByRole('button', { name: /Lagre status/i })
    await expect(saveBtn).toBeEnabled({ timeout: 5_000 })
    await saveBtn.click()

    // statusSuccess message set in AdminDesignRequestDetailView.saveStatus()
    await expect(page.locator('text=Status oppdatert.')).toBeVisible({ timeout: 15_000 })
  })
})
