/**
 * Admin panel test suite.
 *
 * Tests admin login, access control, size management, pricing parameters,
 * order management, production stage updates, and shipping tracking.
 */
import { test, expect } from '@playwright/test'
import { loginViaApi, loginViaUI, clearAuth, getTestUserEmail, getTestUserPassword, getAdminEmail, getAdminPassword } from '../helpers/auth'
import {
  apiLogin,
  apiRegister,
  apiCreateOrder,
  apiFetchSizes,
} from '../helpers/api'

/** Navigate to admin panel with admin auth injected */
async function asAdmin(page: import('@playwright/test').Page) {
  const email = getAdminEmail()
  const password = getAdminPassword()
  await loginViaApi(page, email, password)
}

test.describe('Admin panel', () => {
  test.beforeEach(async ({ page }) => {
    // Clear any previous auth
    await page.goto('/')
    await clearAuth(page)
  })

  test('admin login via UI', async ({ page }) => {
    const email = getAdminEmail()
    const password = getAdminPassword()

    try {
      await apiLogin(email, password)
    } catch {
      test.skip(true, 'Admin user not available — skipping')
      return
    }

    await loginViaUI(page, email, password)

    // Admin should be redirected to /account (or home) after login
    await expect(page).toHaveURL(/\/(account|$)/, { timeout: 10_000 })

    // Admin should be able to navigate to /admin
    await page.goto('/admin')
    await expect(page).toHaveURL(/\/admin$/)
  })

  test('non-admin cannot access /admin (redirected)', async ({ page }) => {
    const email = getTestUserEmail()
    const password = getTestUserPassword()

    let auth
    try {
      auth = await apiLogin(email, password)
    } catch {
      try {
        auth = await apiRegister(email, password, 'Test Bruker')
      } catch {
        test.skip(true, 'Test user unavailable — skipping')
        return
      }
    }

    // Inject non-admin user
    await page.goto('/')
    await page.evaluate((data) => {
      localStorage.setItem('access_token', data.accessToken)
      localStorage.setItem('refresh_token', data.refreshToken)
      localStorage.setItem('user', JSON.stringify(data.user))
    }, auth)

    await page.goto('/admin')

    // Should be redirected away from /admin (to home or login)
    await expect(page).not.toHaveURL(/\/admin$/, { timeout: 10_000 })
  })

  test('unauthenticated user visiting /admin is redirected to login', async ({ page }) => {
    // No auth — go to admin
    await page.goto('/admin')
    // Should redirect to login
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 })
  })

  test('admin can view sizes list', async ({ page }) => {
    try {
      await apiLogin(getAdminEmail(), getAdminPassword())
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    await asAdmin(page)
    await page.goto('/admin/sizes')
    await expect(page).toHaveURL(/\/admin\/sizes/, { timeout: 10_000 })

    // Table with banner sizes
    await expect(page.getByRole('heading', { name: /Bannerstørrelser/i })).toBeVisible()

    // Table rows
    const rows = page.locator('table tbody tr')
    await expect(rows.first()).toBeVisible({ timeout: 10_000 })
  })

  test('admin can add new banner size', async ({ page }) => {
    try {
      await apiLogin(getAdminEmail(), getAdminPassword())
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    await asAdmin(page)
    await page.goto('/admin/sizes')
    await expect(page.getByRole('heading', { name: /Bannerstørrelser/i })).toBeVisible()

    // Click "+ Ny størrelse" button
    await page.getByRole('button', { name: /Ny størrelse/i }).click()

    // Modal should appear
    await expect(page.locator('text=Ny størrelse').last()).toBeVisible()

    // Fill in the form
    const nameInput = page.locator('dialog, [role="dialog"], .fixed').locator('input[type="text"]').first()
    // Fallback: look for any text input inside the modal
    const modal = page.locator('.fixed').filter({ hasText: 'Ny størrelse' })
    await expect(modal).toBeVisible()

    const nameField = modal.locator('input[type="text"]').first()
    await nameField.fill('E2E Test 200×150 cm')

    const heightField = modal.locator('input[type="number"]').last()
    await heightField.fill('150')

    // Make sure width is set
    const widthField = modal.locator('input[type="number"]').first()
    if (await widthField.isEnabled()) {
      await widthField.fill('200')
    }

    // Submit
    await modal.getByRole('button', { name: /Lagre/i }).click()

    // Modal should close and new row should appear in table
    await expect(modal).toBeHidden({ timeout: 10_000 })
    await expect(page.locator('table')).toContainText('E2E Test 200×150 cm', { timeout: 10_000 })
  })

  test('admin can edit existing size', async ({ page }) => {
    try {
      await apiLogin(getAdminEmail(), getAdminPassword())
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    await asAdmin(page)
    await page.goto('/admin/sizes')
    await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 10_000 })

    // Click "Rediger" on the first row
    const firstEditBtn = page.locator('table tbody tr').first().getByText('Rediger')
    await firstEditBtn.click()

    // Modal should appear with "Rediger størrelse" title
    const modal = page.locator('.fixed').filter({ hasText: 'Rediger størrelse' })
    await expect(modal).toBeVisible()

    // Modify the name slightly
    const nameField = modal.locator('input[type="text"]').first()
    const originalName = await nameField.inputValue()
    const newName = originalName.trim() + ' (editert)'
    await nameField.fill(newName)

    // Save
    await modal.getByRole('button', { name: /Lagre/i }).click()
    await expect(modal).toBeHidden({ timeout: 10_000 })

    // Revert: edit again to restore original name
    await page.locator('table tbody tr').first().getByText('Rediger').click()
    const modal2 = page.locator('.fixed').filter({ hasText: 'Rediger størrelse' })
    await expect(modal2).toBeVisible()
    await modal2.locator('input[type="text"]').first().fill(originalName)
    await modal2.getByRole('button', { name: /Lagre/i }).click()
    await expect(modal2).toBeHidden({ timeout: 10_000 })
  })

  test('admin can toggle size active/inactive', async ({ page }) => {
    try {
      await apiLogin(getAdminEmail(), getAdminPassword())
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    await asAdmin(page)
    await page.goto('/admin/sizes')
    await expect(page.locator('table tbody tr').first()).toBeVisible({ timeout: 10_000 })

    // Find first active row
    const activeRow = page.locator('table tbody tr').filter({ hasText: 'Aktiv' }).first()
    await expect(activeRow).toBeVisible()

    // Click "Rediger" on that row
    await activeRow.getByText('Rediger').click()

    const modal = page.locator('.fixed').filter({ hasText: 'Rediger størrelse' })
    await expect(modal).toBeVisible()

    // Uncheck "Aktiv" checkbox
    const activeCheckbox = modal.locator('#isActive')
    await expect(activeCheckbox).toBeChecked()
    await activeCheckbox.uncheck()
    await expect(activeCheckbox).not.toBeChecked()

    // Save
    await modal.getByRole('button', { name: /Lagre/i }).click()
    await expect(modal).toBeHidden({ timeout: 10_000 })

    // Row should now show "Inaktiv"
    await expect(page.locator('table tbody tr').first().locator('text=Inaktiv')).toBeVisible({ timeout: 5_000 })

    // Re-activate (restore)
    await page.locator('table tbody tr').first().getByText('Rediger').click()
    const modal2 = page.locator('.fixed').filter({ hasText: 'Rediger størrelse' })
    await modal2.locator('#isActive').check()
    await modal2.getByRole('button', { name: /Lagre/i }).click()
    await expect(modal2).toBeHidden({ timeout: 10_000 })
  })

  test('admin can update pricing parameters and save', async ({ page }) => {
    try {
      await apiLogin(getAdminEmail(), getAdminPassword())
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    await asAdmin(page)
    await page.goto('/admin/pricing')
    await expect(page.getByRole('heading', { name: /Prissetting/i })).toBeVisible()

    // Wait for params to load
    const rows = page.locator('table tbody tr')
    await expect(rows.first()).toBeVisible({ timeout: 10_000 })

    // Click "Rediger" on first parameter
    const firstRow = rows.first()
    await firstRow.getByText('Rediger').click()

    // An input should appear
    const editInput = firstRow.locator('input[type="number"]')
    await expect(editInput).toBeVisible()

    const originalValue = await editInput.inputValue()

    // Set a value (same as original to avoid changing production data)
    await editInput.fill(originalValue)

    // Save by clicking "Lagre"
    await firstRow.getByText('Lagre').click()

    // Input should disappear (edit mode off)
    await expect(editInput).toBeHidden({ timeout: 10_000 })
  })

  test('admin can view orders list', async ({ page }) => {
    try {
      await apiLogin(getAdminEmail(), getAdminPassword())
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    await asAdmin(page)
    await page.goto('/admin/orders')
    await expect(page.getByRole('heading', { name: /Ordrer/i })).toBeVisible({ timeout: 10_000 })
  })

  test('admin can update production stage on order item', async ({ page }) => {
    const adminEmail = getAdminEmail()
    const adminPassword = getAdminPassword()
    const userEmail = getTestUserEmail()
    const userPassword = getTestUserPassword()

    let adminAuth: Awaited<ReturnType<typeof apiLogin>>
    let userAuth: Awaited<ReturnType<typeof apiLogin>>

    try {
      adminAuth = await apiLogin(adminEmail, adminPassword)
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    try {
      userAuth = await apiLogin(userEmail, userPassword)
    } catch {
      try {
        userAuth = await apiRegister(userEmail, userPassword, 'Test Bruker')
      } catch {
        test.skip(true, 'Test user unavailable — skipping')
        return
      }
    }

    // Create a test order
    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active sizes — skipping')
      return
    }

    const order = await apiCreateOrder(userAuth.accessToken, { bannerSizeId: size.id })

    // Navigate to admin order detail
    await asAdmin(page)
    await page.goto(`/admin/orders/${order.orderId}`)

    await expect(page.getByRole('heading', { name: /Ordre #/i })).toBeVisible({ timeout: 10_000 })

    // Production stage section
    await expect(page.locator('text=Produksjonsstatus per vare')).toBeVisible()

    // Select "Printing" stage from dropdown
    const prodSection = page.locator('.bg-gray-50.rounded-lg.p-4')
    if (await prodSection.count() > 0) {
      const stageSelect = prodSection.first().locator('select')
      await stageSelect.selectOption('Printing')

      // Add a note
      const notesInput = prodSection.first().locator('input[type="text"]')
      await notesInput.fill('E2E automatisert test')

      // Save
      await prodSection.first().getByRole('button', { name: /Oppdater produksjon/i }).click()

      // Success feedback
      await expect(prodSection.first().locator('text=/Oppdatert|✓/i')).toBeVisible({ timeout: 10_000 })
    }
  })

  test('admin can add shipping tracking info', async ({ page }) => {
    const adminEmail = getAdminEmail()
    const adminPassword = getAdminPassword()
    const userEmail = getTestUserEmail()
    const userPassword = getTestUserPassword()

    let adminAuth: Awaited<ReturnType<typeof apiLogin>>
    let userAuth: Awaited<ReturnType<typeof apiLogin>>

    try {
      adminAuth = await apiLogin(adminEmail, adminPassword)
    } catch {
      test.skip(true, 'Admin user unavailable — skipping')
      return
    }

    try {
      userAuth = await apiLogin(userEmail, userPassword)
    } catch {
      try {
        userAuth = await apiRegister(userEmail, userPassword, 'Test Bruker')
      } catch {
        test.skip(true, 'Test user unavailable — skipping')
        return
      }
    }

    const sizes = await apiFetchSizes()
    const size = sizes.find((s) => s.isActive && !s.isCustomWidth)
    if (!size) {
      test.skip(true, 'No active sizes — skipping')
      return
    }

    const order = await apiCreateOrder(userAuth.accessToken, { bannerSizeId: size.id })

    // Navigate to admin order detail
    await asAdmin(page)
    await page.goto(`/admin/orders/${order.orderId}`)
    await expect(page.getByRole('heading', { name: /Ordre #/i })).toBeVisible({ timeout: 10_000 })

    // Find shipping form
    const shipSection = page.locator('.bg-white').filter({ hasText: 'Fraktinformasjon' })
    await expect(shipSection).toBeVisible()

    // Fill carrier
    const carrierInput = shipSection.locator('input[placeholder*="Bring"]')
    await carrierInput.clear()
    await carrierInput.fill('Bring')

    // Fill tracking number
    const trackingInput = shipSection.locator('input[placeholder*="370799"]')
    await trackingInput.fill('370799000000000002')

    // Save
    await shipSection.getByRole('button', { name: /Lagre fraktinfo/i }).click()

    // Success message
    await expect(shipSection.locator('text=/Fraktinfo lagret|✓/i')).toBeVisible({ timeout: 10_000 })
  })
})
