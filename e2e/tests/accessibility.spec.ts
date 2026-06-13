/**
 * Accessibility test suite — WCAG 2.1 AA gate (runs in normal CI).
 *
 * Uses @axe-core/playwright to check key pages for WCAG A + AA violations.
 * A single test failure means at least one violation was detected — the test
 * output prints a human-readable list of each violation so it's quick to
 * identify the offending element.
 *
 * Scope: public pages + authenticated customer/admin surfaces.
 * Any new route that ships with visible UI should get a test here.
 *
 * Excluded rules / known false-positives:
 *   (add entries here as needed rather than disabling globally)
 */
import { test, expect } from '../helpers/fixtures'
import { AxeBuilder } from '@axe-core/playwright'
import { loginViaApi, getAdminEmail, getAdminPassword, getTestUserEmail, getTestUserPassword } from '../helpers/auth'
import { apiLogin, apiRegister } from '../helpers/api'

// ─── Helper ───────────────────────────────────────────────────────────────────

/**
 * Format axe violations into a readable string for assertion error messages.
 */
function formatViolations(violations: import('axe-core').Result[]): string {
  if (violations.length === 0) return ''
  return violations
    .map(
      (v) =>
        `[${v.impact?.toUpperCase() ?? 'UNKNOWN'}] ${v.id}: ${v.description}\n` +
        v.nodes
          .slice(0, 3) // truncate — full list can be very long
          .map((n) => `  - ${n.html.slice(0, 120)}`)
          .join('\n'),
    )
    .join('\n\n')
}

/**
 * Run axe WCAG AA check on the current page and assert zero violations.
 * Disables `color-contrast` on pages where the backend is not running
 * (404 pages rendered by the SPA may not have real content to contrast-check).
 */
async function assertNoViolations(
  page: import('@playwright/test').Page,
  {
    exclude,
    disableRules,
  }: { exclude?: string[]; disableRules?: string[] } = {},
): Promise<void> {
  let builder = new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa'])
  if (exclude) {
    for (const sel of exclude) builder = builder.exclude(sel)
  }
  if (disableRules) {
    builder = builder.disableRules(disableRules)
  }
  const results = await builder.analyze()
  const violations = results.violations
  expect(
    violations,
    `WCAG AA violations found:\n${formatViolations(violations)}`,
  ).toHaveLength(0)
}

// ─── Public pages ─────────────────────────────────────────────────────────────

test.describe('Accessibility — public pages', () => {
  test('home page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('login page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/login')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('register page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/register')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('AI banner builder has no WCAG AA violations', async ({ page }) => {
    await page.goto('/banner-builder/ai')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('manual banner builder has no WCAG AA violations', async ({ page }) => {
    await page.goto('/banner-builder/manual')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })
})

// ─── Authenticated customer pages ─────────────────────────────────────────────

test.describe('Accessibility — customer pages (authenticated)', () => {
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
        test.skip(true, 'Test user unavailable — skipping')
      }
    }
  })

  test('account overview has no WCAG AA violations', async ({ page }) => {
    await page.goto('/account')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('account orders list has no WCAG AA violations', async ({ page }) => {
    await page.goto('/account/orders')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('cart page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/cart')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })
})

// ─── Admin pages ──────────────────────────────────────────────────────────────

test.describe('Accessibility — admin pages', () => {
  test.beforeEach(async ({ page }) => {
    const email = getAdminEmail()
    const password = getAdminPassword()
    try {
      await apiLogin(email, password)
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }
    await loginViaApi(page, email, password)
  })

  test('admin dashboard has no WCAG AA violations', async ({ page }) => {
    await page.goto('/admin')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('admin sizes page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/admin/sizes')
    await page.waitForLoadState('networkidle')
    // Wait for table to be populated
    await page.waitForSelector('table tbody tr', { timeout: 10_000 }).catch(() => null)
    await assertNoViolations(page)
  })

  test('admin pricing page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/admin/pricing')
    await page.waitForLoadState('networkidle')
    await page.waitForSelector('table tbody tr', { timeout: 10_000 }).catch(() => null)
    await assertNoViolations(page)
  })

  test('admin orders page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/admin/orders')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('admin settings page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/admin/settings')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })

  test('admin design requests page has no WCAG AA violations', async ({ page }) => {
    await page.goto('/admin/design-requests')
    await page.waitForLoadState('networkidle')
    await assertNoViolations(page)
  })
})
