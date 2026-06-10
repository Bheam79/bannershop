/**
 * Playwright global teardown — Istanbul coverage report generation.
 *
 * Runs after ALL tests have finished.  When VITE_COVERAGE=true, it merges
 * every per-test JSON file written by the coverage fixture
 * (helpers/fixtures.ts) into a single Istanbul coverage map and generates
 * an HTML + text-summary report under e2e/coverage/.
 *
 * The raw .nyc_output/ directory is removed afterwards to keep the repo clean.
 */
import * as fs from 'fs'
import * as path from 'path'

const NYC_DIR = path.resolve(__dirname, '..', '.nyc_output')
const REPORT_DIR = path.resolve(__dirname, '..', 'coverage')

async function globalTeardown() {
  if (process.env.VITE_COVERAGE !== 'true') return

  if (!fs.existsSync(NYC_DIR)) {
    console.log('\n[coverage] No .nyc_output directory found — no e2e coverage to report.')
    return
  }

  const files = fs.readdirSync(NYC_DIR).filter((f) => f.endsWith('.json'))
  if (files.length === 0) {
    console.log('\n[coverage] No coverage JSON files found in .nyc_output/ — skipping report.')
    return
  }

  console.log(`\n[coverage] Merging ${files.length} coverage file(s)…`)

  // Dynamic imports so the packages are only loaded during a coverage run
  // and don't slow down normal test runs.
  const { createCoverageMap } = await import('istanbul-lib-coverage')
  const { createContext } = await import('istanbul-lib-report')
  const reports = await import('istanbul-reports')

  const map = createCoverageMap({})

  for (const file of files) {
    const raw = fs.readFileSync(path.join(NYC_DIR, file), 'utf-8')
    map.merge(JSON.parse(raw))
  }

  fs.mkdirSync(REPORT_DIR, { recursive: true })

  const context = createContext({
    dir: REPORT_DIR,
    coverageMap: map,
    // Resolve source file paths relative to the frontend src directory so
    // the HTML report shows meaningful file names.
    watermarks: {
      statements: [50, 80],
      functions: [50, 80],
      branches: [50, 80],
      lines: [50, 80],
    },
  })

  // HTML report
  reports.create('html').execute(context)
  // One-line summary printed to the terminal
  reports.create('text-summary').execute(context)

  console.log(`[coverage] E2E coverage report: ${REPORT_DIR}/index.html`)

  // Tidy up the raw per-test JSON files
  fs.rmSync(NYC_DIR, { recursive: true, force: true })
}

export default globalTeardown
