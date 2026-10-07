# Admin order upscale smoke (BANNERSH-312)

No project test suite, live fal calls, or credentials are used.

- `dotnet run --project scripts/verify-order-upscale/VerifyOrderUpscale.csproj` checks the real service/controllers with a fake fal HTTP transport and disposable EF InMemory database: model/auth/payload, 2x/4x isolation, simultaneous clicks, restart/repeated-download caching, content-change invalidation, invalid downloads, queue failure/ambiguous submission safeguards, canonical upload/AI/manual print paths, item/order scoping, attachment responses, and seeded/masked settings.
- `cd frontend && npm run type-check && npm run build-only && npm run preview -- --host 127.0.0.1 --port 10000 --strictPort`, then `node scripts/verify-order-upscale/browser.mjs` from the repo root. Uses the existing `e2e` Playwright installation and real built Vue screens with mocked API responses. Checks both download buttons, per-item visibility, progress/disabled state, authenticated download attachment and filename, repeat downloads, missing-key recovery and settings visibility. Stop preview afterwards. Preview avoids Vite's dev-server inotify watcher limit in the shared container.

## Production manual check

After deploying, the normal migration adds a blank, sensitive `fal_api_key` row. Save the key in `/admin/settings`, then open an order with a print file. First 2x/4x download uses `fal-ai/seedvr/upscale/image/seamless`; subsequent downloads of that file and scale reuse the local PNG, including after app restarts. Originals and customer previews are not modified. Jobs are separate from the customer generation pipeline.

Derivatives and queue handles persist under `FileStorage:LocalRoot/admin-upscales/seedvr-seamless-v1/`, keyed by original-file SHA-256 and scale. `.queue` handles are not served by default StaticFiles MIME mappings. Do not delete a handle for an in-progress job: it prevents another paid submission. Failed jobs remain recorded; after an ambiguous submission timeout the admin must reconcile the job in fal's dashboard before an operator clears the specific failed `.queue` file to explicitly retry (never clear successful PNGs just to download again).

API schema: https://fal.ai/models/fal-ai/seedvr/upscale/image/seamless/api
