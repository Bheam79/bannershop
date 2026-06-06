/**
 * Authentication helpers for Playwright tests.
 *
 * The app stores auth in localStorage (access_token, refresh_token, user).
 * We inject these directly to bypass the login UI when we just need an
 * authenticated session.
 */
import { type Page } from '@playwright/test'
import { apiLogin, type LoginResponse } from './api'

/**
 * Inject auth tokens into localStorage so the page considers the user logged in.
 * Call after page.goto() to set auth before navigation guards run.
 */
export async function injectAuth(page: Page, auth: LoginResponse): Promise<void> {
  await page.evaluate((data) => {
    localStorage.setItem('access_token', data.accessToken)
    localStorage.setItem('refresh_token', data.refreshToken)
    localStorage.setItem('user', JSON.stringify(data.user))
  }, auth)
}

/**
 * Log in via UI (fills the login form and submits).
 */
export async function loginViaUI(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('E-post').fill(email)
  await page.getByLabel('Passord').fill(password)
  await page.getByRole('button', { name: /Logg inn/i }).click()
  // Wait for redirect away from login page
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 10_000 })
}

/**
 * Log in programmatically (API call) then inject tokens into the page session.
 * Much faster than UI login; use this in beforeEach hooks.
 */
export async function loginViaApi(page: Page, email: string, password: string): Promise<LoginResponse> {
  const auth = await apiLogin(email, password)
  // Navigate first so we have a page context with the right origin
  await page.goto('/')
  await injectAuth(page, auth)
  return auth
}

/**
 * Clear auth tokens from localStorage (simulates logout).
 */
export async function clearAuth(page: Page): Promise<void> {
  await page.evaluate(() => {
    localStorage.removeItem('access_token')
    localStorage.removeItem('refresh_token')
    localStorage.removeItem('user')
  })
}

export function getTestUserEmail(): string {
  return process.env.TEST_USER_EMAIL ?? 'testuser@example.com'
}

export function getTestUserPassword(): string {
  return process.env.TEST_USER_PASSWORD ?? 'TestPassword123!'
}

export function getAdminEmail(): string {
  return process.env.ADMIN_EMAIL ?? 'admin@bannershop.no'
}

export function getAdminPassword(): string {
  return process.env.ADMIN_PASSWORD ?? 'AdminPassword123!'
}
