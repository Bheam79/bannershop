# Guest banner smoke (BANNERSH-305)

These standalone checks do not run the project test suite or call real image providers.

- `dotnet run --project scripts/verify-guest-banner`: real controller/service with a disposable in-memory SQLite database and ephemeral protection keys. Checks guest pending/ready reads, protected-cookie isolation/expiry, authenticated idempotent claiming, portrait ownership, and no duplicate designs/orders. Only the generation enqueue response is stubbed.
- Start `npm run dev -- --port 10000 --strictPort` in `frontend`, then run `node scripts/verify-guest-banner/browser.mjs`. Requires the existing `e2e` Playwright installation. Browser uses the real Vue composable/views with mocked API responses; verifies visible guest preview and registration returning to the same edited form through login/register navigation. Stop Vite afterwards.

Original browser reproduction: `phase: anon_pending`, no preview, **0 polls**, and login's registration link dropped the redirect (`/register`).
