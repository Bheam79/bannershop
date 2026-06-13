import { defineConfig, devices } from '@playwright/test'
import * as dotenv from 'dotenv'
import * as path from 'path'

// Load .env file from e2e directory if it exists
dotenv.config({ path: path.resolve(__dirname, '.env') })

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:5173'
const API_URL = process.env.API_URL ?? 'http://localhost:5000'

export default defineConfig({
  testDir: './tests',
  fullyParallel: false, // Run suites sequentially to avoid DB conflicts
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 1 : 1,
  reporter: [
    ['html', { outputFolder: 'playwright-report' }],
    ['list'],
  ],

  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'on-first-retry',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },

  projects: [
    // ── Default CI project ──────────────────────────────────────────────────
    // Runs all functional + accessibility tests.
    // Visual-regression specs are excluded here — run them separately via
    //   npx playwright test --project=visual-regression
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
      testIgnore: ['**/visual-*.spec.ts'],
    },

    // ── Opt-in visual-regression project ────────────────────────────────────
    // Full-page screenshot tests. Not run by default.
    //
    // First-run / updating baselines:
    //   npx playwright test --project=visual-regression --update-snapshots
    //
    // Comparing against committed baselines:
    //   npx playwright test --project=visual-regression
    //
    // Snapshots are stored in e2e/snapshots/ and must be committed.
    {
      name: 'visual-regression',
      use: {
        ...devices['Desktop Chrome'],
        // Fixed 1280×720 viewport ensures reproducible full-page screenshots
        // regardless of host screen resolution.
        viewport: { width: 1280, height: 720 },
      },
      testMatch: ['**/visual-*.spec.ts'],
      snapshotDir: './snapshots',
    },
  ],

  // Pass through as global variables accessible in tests
  globalSetup: './helpers/global-setup.ts',
  // Merges Istanbul coverage data and generates HTML report when
  // VITE_COVERAGE=true.  Runs after all tests complete.
  globalTeardown: './helpers/global-teardown.ts',
})

export { BASE_URL, API_URL }
