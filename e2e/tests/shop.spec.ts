/**
 * Public shop test suite.
 *
 * Tests the home page: hero section, occasion category cards,
 * preview panel, and navigation to banner-builder flows.
 * No authentication required.
 */
import { test, expect } from '../helpers/fixtures'

test.describe('Public shop', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/')
  })

  test('home page loads with hero heading visible', async ({ page }) => {
    await expect(
      page.getByRole('heading', { name: /store øyeblikkene/i }),
    ).toBeVisible()
  })

  test('occasion categories section is visible with multiple cards', async ({ page }) => {
    const section = page.locator('#anledninger')
    await expect(section).toBeVisible()

    const cards = section.locator('button.cat-card')
    const count = await cards.count()
    expect(count).toBeGreaterThanOrEqual(3)
  })

  test('all five occasions are listed', async ({ page }) => {
    const section = page.locator('#anledninger')
    await expect(section.getByText('Bursdag')).toBeVisible()
    await expect(section.getByText('Konfirmasjon')).toBeVisible()
    await expect(section.getByText('Dåp')).toBeVisible()
    await expect(section.getByText('Bryllup')).toBeVisible()
    await expect(section.getByText('Sommerfest')).toBeVisible()
  })

  test('first occasion (Bursdag) is selected by default', async ({ page }) => {
    // The first cat-card has .sel applied by default (selectedCat = CATS[0])
    const firstCard = page.locator('#anledninger button.cat-card').first()
    await expect(firstCard).toHaveClass(/sel/)
  })

  test('clicking a different occasion selects it', async ({ page }) => {
    const konfCard = page
      .locator('#anledninger button.cat-card')
      .filter({ hasText: /Konfirmasjon/i })
    await expect(konfCard).toBeVisible()
    await konfCard.click()
    await expect(konfCard).toHaveClass(/sel/)
  })

  test('preview section shows "Forhåndsvisning" heading', async ({ page }) => {
    const previewSection = page.locator('#bestill')
    await expect(previewSection).toBeVisible()
    await expect(previewSection.getByText('Forhåndsvisning')).toBeVisible()
  })

  test('"Kom i gang med dette" CTA navigates to AI banner builder', async ({ page }) => {
    const cta = page.getByRole('button', { name: /Kom i gang med dette/i })
    await expect(cta).toBeVisible()
    await cta.click()
    await expect(page).toHaveURL(/\/banner-builder\/ai/, { timeout: 10_000 })
  })

  test('"Lag ditt eget banner" section shows upload, AI, and manual options', async ({ page }) => {
    const section = page.locator('#lagselv')
    await expect(section).toBeVisible()
    await expect(section.getByText('Last opp eget design')).toBeVisible()
    await expect(section.getByText('AI-designet banner')).toBeVisible()
    await expect(section.getByText('Vi designer for deg')).toBeVisible()
  })
})
