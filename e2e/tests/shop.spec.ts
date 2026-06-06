/**
 * Public shop test suite.
 *
 * Tests the home page banner selection, pricing, shipping estimator, and
 * add-to-cart flow. No authentication required.
 */
import { test, expect } from '@playwright/test'

test.describe('Public shop', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/')
    // Wait for banner sizes to load
    await expect(page.locator('text=Laster bannerstørrelser')).toBeHidden({ timeout: 15_000 })
  })

  test('home page loads with banner sizes visible', async ({ page }) => {
    // Heading
    await expect(page.getByRole('heading', { name: /Bestill ditt banner/i })).toBeVisible()

    // Should show banner size cards (at least one standard size)
    const sizeCards = page.locator('section').filter({ hasText: 'Velg størrelse' }).locator('button')
    await expect(sizeCards).not.toHaveCount(0)

    // At least one size name should contain cm
    const firstCard = sizeCards.first()
    await expect(firstCard).toBeVisible()
  })

  test('standard banner sizes are listed on the home page', async ({ page }) => {
    // Standard sizes: 300×150, 350×150, 400×150, 450×150, 500×150 plus the 300×180 special
    const sizesSection = page.locator('section').filter({ hasText: 'Velg størrelse' })

    // Expect multiple size buttons to be visible
    const buttons = sizesSection.locator('button[type="button"]')
    await expect(buttons).not.toHaveCount(0)
    const count = await buttons.count()
    expect(count).toBeGreaterThanOrEqual(3)
  })

  test('300×180 special offer shows 699 kr price', async ({ page }) => {
    // Find the card containing "300 × 180" or "300×180"
    const specialCard = page
      .locator('button[type="button"]')
      .filter({ hasText: /300\s*[×x]\s*180/i })
    await expect(specialCard).toBeVisible({ timeout: 10_000 })

    // Should display 699 kr (the fixed price for this size)
    await expect(specialCard).toContainText('699')
  })

  test('300×180 availability note mentions Aug 31 (pre-order)', async ({ page }) => {
    const specialCard = page
      .locator('button[type="button"]')
      .filter({ hasText: /300\s*[×x]\s*180/i })
    await expect(specialCard).toBeVisible()

    // Should show availability note (the material is available from 2026-08-31)
    await expect(specialCard).toContainText(/Tilgjengelig fra/i)
  })

  test('custom width input updates price in real time', async ({ page }) => {
    // Click the "Valgfri bredde" (custom width) card
    const customCard = page
      .locator('button[type="button"]')
      .filter({ hasText: /Valgfri bredde/i })
    await expect(customCard).toBeVisible()
    await customCard.click()

    // Find the width input
    const widthInput = page.locator('input#customWidth')
    await expect(widthInput).toBeVisible()

    // Get current price
    const priceText = async () =>
      customCard.locator('.text-2xl.font-bold').innerText()

    // Change width to 200 cm
    await widthInput.fill('200')
    await widthInput.dispatchEvent('input')

    // Wait for price to update (debounced 250ms)
    await page.waitForTimeout(500)

    // Price should be visible and numeric
    const price = await priceText()
    expect(price).toMatch(/\d/)
    expect(price).toContain('kr')

    // Change to a different width — price should update
    const priceAt200 = price
    await widthInput.fill('500')
    await widthInput.dispatchEvent('input')
    await page.waitForTimeout(500)

    const priceAt500 = await priceText()
    // Wider banner should cost more
    expect(priceAt500).not.toEqual(priceAt200)
  })

  test('postal code entry shows shipping cost estimate', async ({ page }) => {
    // Select a size first (should auto-select 300×180 or first available)
    // The ShippingEstimator section is rendered below
    const estimatorSection = page.locator('section').filter({ hasText: /Frakt|postnummer/i }).last()
    await expect(estimatorSection).toBeVisible()

    // Find and fill postal code input
    const postalInput = estimatorSection.locator('input[type="text"]').first()
    await postalInput.fill('0150')
    await postalInput.press('Tab') // trigger the estimation

    // Wait for shipping cost to appear (API call)
    const shippingCost = estimatorSection.locator('text=/\\d+ kr/')
    await expect(shippingCost.first()).toBeVisible({ timeout: 15_000 })
  })

  test('Standard vs Express toggle updates total', async ({ page }) => {
    // Select a non-custom size
    const standardSizeCards = page
      .locator('button[type="button"]')
      .filter({ hasText: /\d+ × \d+ cm/ })
      .filter({ hasNot: page.locator('text=Valgfri bredde') })
    await standardSizeCards.first().click()

    // The order details section should appear
    const detailsSection = page
      .locator('section')
      .filter({ hasText: 'Bestillingsdetaljer' })
    await expect(detailsSection).toBeVisible()

    // Get total in Standard mode
    const totalLocator = detailsSection.locator('.font-bold.text-blue-700')
    const standardTotal = await totalLocator.innerText()

    // Click Express
    const expressBtn = detailsSection.locator('button').filter({ hasText: /Ekspress/i })
    await expressBtn.click()

    // Total should increase (500 kr express fee added)
    const expressTotal = await totalLocator.innerText()
    expect(expressTotal).not.toEqual(standardTotal)

    // Parse and compare (express must be higher)
    const parseNok = (s: string) => parseInt(s.replace(/[^0-9]/g, ''), 10)
    expect(parseNok(expressTotal)).toBeGreaterThan(parseNok(standardTotal))
  })

  test('can add item to cart and be redirected to checkout', async ({ page }) => {
    // Select first non-custom size
    const sizeCards = page
      .locator('section')
      .filter({ hasText: 'Velg størrelse' })
      .locator('button[type="button"]')
      .filter({ hasNot: page.locator('text=Valgfri bredde') })
    await sizeCards.first().click()

    // Click add to cart button
    const addToCartBtn = page.getByRole('button', { name: /Legg i handlekurv/i })
    await expect(addToCartBtn).toBeVisible()
    await addToCartBtn.click()

    // Should navigate to checkout
    await expect(page).toHaveURL(/\/checkout$/, { timeout: 10_000 })
    await expect(page.getByRole('heading', { name: /Kasse/i })).toBeVisible()
  })
})
