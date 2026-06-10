/**
 * Playwright test fixtures with optional Istanbul coverage collection.
 *
 * Import `test` and `expect` from this file instead of `@playwright/test`
 * to enable frontend coverage when running with VITE_COVERAGE=true.
 *
 * How it works:
 *  1. The Vite dev server must be started with VITE_COVERAGE=true so that
 *     vite-plugin-istanbul instruments the source files and populates
 *     window.__coverage__ as the app runs.
 *  2. After each test the fixture extracts window.__coverage__ from the page
 *     and writes it as a JSON file under e2e/.nyc_output/.
 *  3. The Playwright globalTeardown (helpers/global-teardown.ts) merges all
 *     those JSON files and generates an HTML report under e2e/coverage/.
 *
 * Run with coverage:
 *   VITE_COVERAGE=true npx playwright test
 *
 * Non-coverage run (default):
 *   npx playwright test
 */
import { test as base, expect, type Page } from '@playwright/test'
import * as fs from 'fs'
import * as path from 'path'
import { randomUUID } from 'crypto'

export type { Page }

const COVERAGE_ENABLED = process.env.VITE_COVERAGE === 'true'
const COVERAGE_DIR = path.resolve(__dirname, '..', '.nyc_output')

export const test = base.extend<object>({
  page: async ({ page }, use) => {
    await use(page)

    if (!COVERAGE_ENABLED) return

    try {
      // Extract the Istanbul coverage map that vite-plugin-istanbul has
      // accumulated in window.__coverage__ throughout this test.
      const coverage = await page.evaluate(() => (window as unknown as { __coverage__?: unknown }).__coverage__)
      if (coverage) {
        fs.mkdirSync(COVERAGE_DIR, { recursive: true })
        const outFile = path.join(COVERAGE_DIR, `${randomUUID()}.json`)
        fs.writeFileSync(outFile, JSON.stringify(coverage))
      }
    } catch {
      // The page may have been closed/navigated away — not a fatal error.
    }
  },
})

export { expect }
