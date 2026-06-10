/**
 * BANNERSH-79 / BANNERSH-115: anonymous free-AI generation + paywall flow.
 *
 * Covers four scenarios:
 *   1. Anonymous bot-protection rejection (Node-side fetch with Googlebot UA → 403)
 *   2. Anonymous happy-path — first free generation lands on anon_pending phase
 *   3. Paywall on second anonymous attempt (IP seeded via BANNERSH-114 helper)
 *   4. Authenticated user with no credits hits paywall on click (frontend pre-check)
 *
 * Test isolation
 * ──────────────
 * Each scenario picks a unique IP in the IANA documentation range 203.0.113.0/24
 * (never routable on the public internet) and injects it as `X-Forwarded-For` via
 * `page.route('**\/api/design-requests/ai', ...)`. The backend's
 * `DesignRequestsController.GetClientIpAddress()` honors X-Forwarded-For, so the
 * test owns its own IP regardless of what the surrounding CI runner / proxy
 * looks like. This makes the suite repeatable across runs without a DB wipe.
 */
import { test, expect, type Page } from '../helpers/fixtures'
import {
  apiRegister,
  apiCreateAiDesignRequest,
  apiSeedIpAiUsage,
  type LoginResponse,
} from '../helpers/api'
import { injectAuth } from '../helpers/auth'

const API_URL = process.env.API_URL ?? 'http://localhost:5000'

/**
 * Pick a fresh random IPv4 inside 203.0.113.0/24 (IANA "TEST-NET-3" / RFC 5737
 * documentation range — guaranteed never to overlap with a real client IP).
 */
function uniqueTestIp(): string {
  return `203.0.113.${Math.floor(Math.random() * 254) + 1}`
}

/**
 * Install a Playwright route handler that injects `X-Forwarded-For: testIp` on
 * every browser-initiated POST to /api/design-requests/ai (the only endpoint
 * that consults the anonymous IP throttle).
 *
 * Other AI-related endpoints (templates, balance, etc.) are left untouched —
 * the throttle key is the POST /ai IP, nothing else.
 */
async function routeWithForwardedFor(page: Page, testIp: string): Promise<void> {
  await page.route('**/api/design-requests/ai', async (route) => {
    const req = route.request()
    if (req.method() !== 'POST') {
      await route.continue()
      return
    }
    const headers = {
      ...req.headers(),
      'x-forwarded-for': testIp,
    }
    await route.continue({ headers })
  })
}

/** Register a fresh throwaway user (mirrors helper in design-request.spec.ts). */
async function registerThrowawayUser(): Promise<LoginResponse> {
  const stamp = `${Date.now()}_${Math.floor(Math.random() * 1_000_000)}`
  return await apiRegister(`e2e_anon_${stamp}@example.com`, 'E2eSeedPass123!', 'E2E Anon User')
}

/**
 * Walk the AI wizard from /banner-builder/ai step 1 → step 3.
 * Leaves the page parked on the "Klar til å generere" step with the
 * Generate button visible (but does NOT click it).
 */
async function advanceToStep3(page: Page): Promise<void> {
  // Step 1: template cards
  await expect(page.locator('.tpl-card').first()).toBeVisible({ timeout: 15_000 })
  await page.getByRole('button', { name: /Neste: Tilpass/i }).click()

  // Step 2: customer details
  const personName = page.locator('#personName')
  await expect(personName).toBeVisible({ timeout: 10_000 })
  await personName.fill('E2E Test Person')
  await page.locator('#textContent').fill('Gratulerer med dagen!')
  await page.locator('#themeDescription').fill('Festlig og fargerik')
  await page.getByRole('button', { name: /Neste: Generer/i }).click()

  // Step 3 header
  await expect(page.locator('text=Klar til å generere')).toBeVisible({ timeout: 10_000 })
}

test.describe('AI banner — anonymous free + paywall', () => {

  // ── Scenario 1 ──────────────────────────────────────────────────────────────

  test('1: anonymous bot UA → 403 from POST /api/design-requests/ai', async ({ page }) => {
    // a) The /banner-builder/ai page must load for unauthenticated visitors
    //    (AllowAnonymous on POST /api/design-requests/ai, no auth guard on the route).
    await page.goto('/banner-builder/ai')
    await expect(page.locator('.tpl-card').first()).toBeVisible({ timeout: 15_000 })

    // b) From Node (NOT page.evaluate) post with a known bot UA. We expect 403.
    //    The X-Request-Integrity header is included so the rejection is unambiguously
    //    from the UA check rather than the missing-integrity branch.
    const res = await fetch(`${API_URL}/api/design-requests/ai`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'User-Agent': 'Googlebot/2.1 (+http://www.google.com/bot.html)',
        'X-Request-Integrity': 'e2e-test-integrity-token',
      },
      body: JSON.stringify({
        templateId: 1,
        personName: 'Bot',
        textContent: 'Hello',
        themeDescription: 'Test',
        language: 'nb',
        aspectRatio: '16:9',
        personAge: null,
      }),
    })
    expect(res.status).toBe(403)
  })

  // ── Scenario 2 ──────────────────────────────────────────────────────────────

  test('2: anonymous happy path — first free generation → anon_pending', async ({ page }) => {
    const testIp = uniqueTestIp()
    await routeWithForwardedFor(page, testIp)

    await page.goto('/banner-builder/ai')
    await advanceToStep3(page)

    // The free-first CTA must be the rendered button label (canGenerateForFree is null
    // for anonymous so the wizard defaults to "Generer banner gratis").
    const freeBtn = page.getByRole('button', { name: /Generer banner gratis/i })
    await expect(freeBtn).toBeVisible()

    await freeBtn.click()

    // anon_pending phase shows the success heading + "Opprett konto" CTA.
    // The wizard cannot poll without auth, so it parks here.
    await expect(page.locator('text=Banneret genereres!')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('link', { name: /Opprett konto/i })).toBeVisible()
  })

  // ── Scenario 3 ──────────────────────────────────────────────────────────────

  test('3: paywall on second anonymous attempt (IP already used)', async ({ page }) => {
    const testIp = uniqueTestIp()

    // Pre-mark the IP as having used its free generation. Without this we'd have
    // to fire one real generation first, which depends on the AI pipeline running.
    await apiSeedIpAiUsage(testIp)

    await routeWithForwardedFor(page, testIp)
    await page.goto('/banner-builder/ai')
    await advanceToStep3(page)

    // Anonymous user with no client-side hint that they're out — they still see the
    // "Generer banner gratis" label. The 402 paywall fires server-side on click.
    await page.getByRole('button', { name: /Generer banner/i }).click()

    // Paywall modal: heading "Generer flere AI-banner" + the credit-pack option
    // ("Kjøp pakke med N AI banner forslag (<price> kr)").
    await expect(page.locator('text=Generer flere AI-banner')).toBeVisible({ timeout: 15_000 })
    const buyOption = page.locator('text=/Kjøp pakke med .+ AI banner forslag/i')
    await expect(buyOption).toBeVisible()

    // The option's NOK price must render — sanity-check the seeded PricingParameter
    // (default 29 kr per BANNERSH-65) made it into the modal.
    await expect(page.locator('.modal-box').getByText(/\d+\s*kr/).first()).toBeVisible()
  })

  // ── Scenario 4 ──────────────────────────────────────────────────────────────

  test('4: authenticated user with no credits → paywall on click', async ({ page }) => {
    // Step 1: register a throwaway user (fresh per-user free-generation budget).
    let userAuth: LoginResponse
    try {
      userAuth = await registerThrowawayUser()
    } catch {
      test.skip(true, 'Could not register throwaway user — skipping')
      return
    }

    // Step 2: spend the per-user free generation via API. This flips
    // `HasUsedFreeAiGeneration=true` on the user and leaves creditsRemaining at 0.
    await apiCreateAiDesignRequest(userAuth.accessToken, {
      templateId: 1,
      personName: 'E2E Test Person',
      textContent: 'Gratulerer!',
      themeDescription: 'Festlig',
    })

    // Step 3: enter the wizard authenticated as the throwaway user.
    await page.goto('/')
    await injectAuth(page, userAuth)
    await page.goto('/banner-builder/ai')

    await advanceToStep3(page)

    // The "Kjøp kreditter for å generere" label is the deterministic signal that
    // the wizard has loaded the credit balance and computed `isOutOfGenerations`.
    // Wait for it explicitly so we don't race the /ai-credits/me fetch.
    const buyBtn = page.getByRole('button', { name: /Kjøp kreditter for å generere/i })
    await expect(buyBtn).toBeVisible({ timeout: 15_000 })

    // Click → paywall modal opens without any /design-requests/ai POST
    // (generateBanner() short-circuits when isOutOfGenerations is true).
    await buyBtn.click()
    await expect(page.locator('text=Generer flere AI-banner')).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('text=/Kjøp pakke med .+ AI banner forslag/i')).toBeVisible()
  })
})
