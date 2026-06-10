# BannerShop — Dev Notes for Claude

## Stack
- **Backend**: C#.NET 10, ASP.NET Core Web API, EF Core 9 + Pomelo MySQL (MariaDB)
- **Frontend**: Vue 3 + Vite + Tailwind CSS v4 (uses `@tailwindcss/vite` plugin — NOT `tailwind.config.js`)
- **Database**: MariaDB 11 (container managed via `cb docker`)
- **Solution file**: `BannerShop.slnx` (new `.slnx` format, not `.sln`)

## Dev container DB setup
The MariaDB container is a sibling, accessible by IP (not hostname from dev container):
```bash
cb docker run mariadb:11 --name db -v /workspace/mariadb-data:/var/lib/mysql \
  -e MYSQL_ROOT_PASSWORD=root_dev -e MYSQL_DATABASE=bannershop \
  -e MYSQL_USER=bannershop -e MYSQL_PASSWORD=bannershop_dev
DB_IP=$(cb docker exec db hostname -I | tr -d ' ')
# Update appsettings*.json to use $DB_IP instead of localhost/hostname
```
Current DB IP: `10.89.7.5` (changes each time the container restarts — re-read with the `DB_IP=$(cb docker exec db hostname -I | tr -d ' ')` snippet above; appsettings.json is hard-coded to a previous IP and may need updating).

## EF Core migrations
The `DesignTimeDbContextFactory` reads `BANNERSHOP_DB` env var:
```bash
BANNERSHOP_DB="Server=10.89.7.2;Port=3306;Database=bannershop;User=bannershop;Password=bannershop_dev;" \
  dotnet-ef migrations add <Name> \
  --project BannerShop.Infrastructure --startup-project BannerShop.Api --output-dir Data/Migrations

BANNERSHOP_DB="..." dotnet-ef database update \
  --project BannerShop.Infrastructure --startup-project BannerShop.Api
```
**Do NOT use `ServerVersion.AutoDetect()`** — it tries to connect at design time. Use `new MariaDbServerVersion(new Version(11,0,0))`.

## Swashbuckle version conflict
`Microsoft.AspNetCore.OpenApi` v10 brings in `Microsoft.OpenApi` v2.0 which conflicts with Swashbuckle 6.x. We removed `Microsoft.AspNetCore.OpenApi` from the API project and use only `Swashbuckle.AspNetCore` 6.9.0.

## Frontend
- Tailwind v4: no `tailwind.config.js` — configured via `@theme {}` in `main.css`
- API calls: all via `/api` proxy in Vite dev server (proxied to `localhost:5000`)
- Auth store: `localStorage` backed JWT + refresh token

## Production (`make up` / `make down`) — BANNERSH-30
Top-level `Makefile` stands up the full stack on any Linux host with `docker`, `dotnet`, `npm`, `systemctl`, `openssl` in PATH. MariaDB runs as a Docker container; backend (which now also serves the SPA from `BannerShop.Api/wwwroot/`) runs as a `systemctl --user` service.

- Ports: backend `:17080`, MariaDB `127.0.0.1:17006` (both in the 17000–17100 range).
- Layout under `$HOME/.local/share/bannershop/{app,data,secrets}` and unit at `$HOME/.config/systemd/user/bannershop.service` — no sudo required (except optional `loginctl enable-linger $USER` for boot start).
- Secrets (DB password, JWT key, admin password) are generated on first run via `openssl rand`, kept mode 0600 under `secrets/`, and reused on subsequent `make up`.
- The Vite build is copied into `BannerShop.Api/wwwroot/` at publish time so a single ASP.NET process serves `/api/*` and the SPA (with `MapFallbackToFile("index.html")` for client-side routes — only registered when the file exists, so tests/dev are unaffected).
- `Program.cs` auto-runs `db.Database.Migrate()` only in `Development` or `Production` environments — `Testing` (used by `TestWebApplicationFactory` with the InMemory provider) is skipped because InMemory doesn't support migrations.

## Build commands
```bash
# Backend
dotnet build /workspace/repo/BannerShop.slnx

# Frontend
cd /workspace/repo/frontend && npm run type-check
cd /workspace/repo/frontend && npm run build-only

# Run backend (after starting DB)
cd /workspace/repo/BannerShop.Api && dotnet run
```

## Ports
- Backend API: `http://localhost:5000` (or port 80 via container)
- Frontend dev: `http://localhost:5173` → mapped to `31780` on host? (Vite on :5173 exposed on :80 → host :31780)

## Seeded data
- 2 materials (160cm/400g and 180cm/680g), 7 banner sizes, 5 pricing parameters
- Seed is in `BannerShopDbContext.SeedData()` — applied via migration

## Parcel preview in checkout (BANNERSH-180)
`POST /api/shipping/parcel-preview` returns the parcel `L × W × H + weight`
that will be sent to Bring for a given `(bannerSizeId, customWidthCm, qty,
packingMode)` — no postal code, no carrier call. `CheckoutView` calls it on
mount for both Folded and Rolled so the customer sees the actual dims under
each Pakkemetode radio before entering a postal code.

The packaging-weight default in `ParcelCalculator` was lowered from 500 g →
**200 g** (matches Michael's measured packaging). The seeded
`shipping_packaging_weight_g` pricing parameter still ships at 500 g in old
migrations, so the displayed weight on existing installs reflects whatever
the admin has stored — update via `/admin/pricing` to surface the new 200 g
default end-to-end.

## Bring shipping (BANNERSH-143)
`BringOptions` ships with the production Mybring credentials hardcoded as defaults
(uid `post@beatgrid.no`, customer number `20027039252`, rates endpoint
`https://api.bring.com/shippingguide/v2`). Override via `Bring:*` in appsettings.
The `ParcelCalculator` supports two customer-selectable packing modes via
`PackingMode { Rolled, Folded }`:
- **Rolled**: L = `min(width,height) + 2 cm`; W = H = `(9 + 0.5 × longSideMeters) × √qty`
- **Folded**: 50 × 60 cm flat; H = `(10 + 1 × longSideMeters) × qty`

`ShippingEstimator.vue` exposes the radio toggle; `CreateOrderDraftRequest.PackingMode`
threads the choice to `OrderService` so the server-side quote matches what the
customer saw. **`TestWebApplicationFactory` re-registers `MockShippingService`** —
because Bring is now considered configured by default, tests would otherwise hit
the live API. Persisting the choice on the `Order` entity for fulfilment is filed
as a follow-up.

## Multi-panel pricing (BANNERSH-88)
`Material.MaxBannerWidthCm` is the max banner width producible as a single panel. When a
banner exceeds it, `PricingService` multiplies the per-panel formula price by the panel
count (×1 / ×2 / ×3 / …). The panel formula is `⌈(width − overlap) / (max − overlap)⌉`
where `overlap` is the `banner_panel_overlap_cm` pricing parameter (default 5 cm).
Fixed-price sizes skip both the formula AND the multiplier — admin sets the final price
manually. The multiplier requires `Material` to be `.Include`d; if the navigation is
null, `PricingService` falls back to single-panel pricing (so it never crashes on stale
callers).

## Banner builder (BANNERSH-15)
- File uploads land under `FileStorage:BasePath` (default `/workspace/uploads`) and are served via StaticFiles at `FileStorage:PublicUrlPrefix` (default `/uploads`).
- `IImageProcessingService` uses `SixLabors.ImageSharp` for raster ops and `PDFtoImage` for PDF→PNG (first page only, 200 DPI).
- `PDFtoImage` v4 has int-page overloads marked obsolete — use the `Index`-based overload.
- Dimension math lives in pure helper `BannerDimensions` (covered by unit tests in `BannerDimensionsTests`).
- Only `SixLabors.ImageSharp` (3.1.5) is referenced — `SixLabors.ImageSharp.Drawing` is NOT, so `Fill`/`DrawText` extensions on `IImageProcessingContext` are unavailable. Use `Image<Rgba32>` constructor with a fill colour, or `Mutate(ctx => ctx.Crop(...))` for crops.

## Manual design requests (BANNERSH-19 / BANNERSH-104)
- `POST /api/design-requests/manual` charges the customer **design fee (495 kr) + physical-banner production cost** in a single Stripe PaymentIntent. Pre-BANNERSH-104 only the 495 kr design fee was collected, which silently left the printed banner unpaid.
- `DesignRequest.PriceNok` stays the design fee (495). New columns `BannerPriceNok`, `BannerSizeId`, `CustomBannerWidthCm` hold the production cost breakdown (migration `AddBannerPriceToManualDesignRequest`).
- Banner cost is resolved server-side from the chosen `AspectRatio` (`18:9` → 300×150 standard size; `16:9` → 266×150 via custom-width size). Mirrored on the frontend in `ManualBannerBuilderView.pickBannerSize` — keep the two in sync. If no `BannerSize` matches (e.g. catalog not seeded in tests), the service logs and degrades to design-fee-only pricing rather than crashing.
- `CreateDesignRequestResponseDto` now also returns `designPriceNok` + `bannerPriceNok` so the wizard's summary panel renders the line items without recomputing.

## AI design requests (BANNERSH-19)
- `DesignRequest` + `DesignRequestRevision` entities + `AddDesignRequests` migration ship with this task (BANNERSH-26 is the consolidated foundation task — was still TODO when this was done, so the entities were added here).
- Stripe webhook (`payment_intent.succeeded`) calls BOTH `OrderService.MarkPaidAsync` and `DesignRequestService.MarkPaidAndEnqueueAsync` — the latter looks up by PaymentIntentId, ignores misses, and enqueues a job. Design-request PaymentIntents use `orderId = -designRequestId` metadata so order-lookups by id won't accidentally hit them.
- AI pipeline runs in `DesignRequestJobProcessor` (BackgroundService) reading from `IDesignRequestJobQueue` (in-process `Channel<int>`). No external queue dependency for v1 volumes.
- Image provider abstraction: `IAiImageService` is `OpenAiImageService` when `OpenAi:ApiKey` is set, else `MockAiImageService` (solid-colour PNG, so the rest of the pipeline is exercisable without API credit).
- **BANNERSH-127: OpenAI key + model + quality precedence is DB > appsettings.** `OpenAiImageService` resolves all three at call time: `system_settings.openai_api_key` / `openai_image_model` / `openai_image_quality` win over `OpenAi:ApiKey` / `OpenAi:ImageModel` / `OpenAi:ImageQuality` from config. A blank/whitespace DB row falls through to appsettings — so the admin can override per-field without nuking the others. The settings panel (`/admin/settings`) edits all three rows; quality accepts `low|medium|high|auto`. If an old admin-panel value is in the DB, it masks the appsettings file — the startup log (`Startup.OpenAi`) enumerates each configuration provider's contribution AND probes the three DB rows so the resolved source is visible in journalctl on every boot.
- Per BANNERSH-18: model is `gpt-image-2` (configurable via `OpenAi:ImageModel`), no Replicate upscaling on the customer pipeline — `NoopUpscalingService` returns input unchanged. `IPhotoCompositor` is a stub (`PhotoCompositorNotImplemented`) since portraits go through `/v1/images/edits`.
- **BANNERSH-57: Real-ESRGAN 4x upscaler** (`Services/DesignRequests/Replicate/RealEsrganUpscalingService.cs`) calls Replicate `nightmareai/real-esrgan`. Registered only when `Replicate:ApiToken` is set; injected into `AdminDesignRequestService` as a constructor-optional dependency. Triggered via `POST /api/admin/design-requests/{id}/upscale?scale=4` — writes a new file alongside the original and repoints `FinalCroppedStoragePath`. The customer-facing `IUpscalingService` DI registration stays `NoopUpscalingService` (this is order-backend only).
- **BANNERSH-61: prompt refinement + Baptism template.** Added `BannerTemplateCategory.Baptism` (Id=8, "Dåp"/"Baptism", seed migration `AddBaptismTemplate`) so dåp joins the existing person-centred categories (Birthday, Confirmation, Wedding). `BannerTemplateCategoryExtensions.IsPersonCentred()` reports which categories benefit from a portrait upload + `/v1/images/edits` merge. Added `IPromptRefinementService` between `BannerPromptService.BuildPrompt` and `IAiImageService.GenerateAsync` in `AiGenerationPipeline`: `OpenAiPromptRefinementService` (chat-completions, default model `gpt-4o-mini`, configurable via `OpenAi:ChatModel`) is registered when the OpenAI key is set; otherwise `NoopPromptRefinementService` returns the deterministic prompt unchanged. Refiner failures **never** block the pipeline — any HTTP / parse / timeout error falls back to the base prompt.
- **BANNERSH-83: AI banner wizard UX (free-first surfacing).** `AiBannerBuilderView` now reads `/api/ai-credits/me` on mount (both `creditsRemaining` and `hasUsedFreeGeneration`) and uses it to (a) label the Generate button accurately ("Generer banner gratis" / "Generer banner (1 kreditt)" / "Kjøp kreditter for å generere") and (b) open the paywall modal *before* posting when the call is known to 402 — the previous "I clicked the FREE button and got a popup" flow is gone. After a banner is ready, "Generer ny versjon" is demoted to a tertiary action; the primary secondary CTA is "Tilbake og endre detaljer" which clears `currentDesignRequest` / `designRequestId` / the `ai_banner_draft_id` localStorage key and jumps back to step 2 with inputs preserved. A new "Tidligere genererte banner" gallery (also embedded inside the paywall) renders the user's prior AI designs as cards — clicking one loads the detail and lands on the ready phase. The list endpoint `GET /api/design-requests` was extended with `previewUrl` (resolved server-side via `BannerFileStorage.PublicUrlFor`), `personName`, and `themeDescription` to feed the gallery without an extra round-trip; the AccountDesignRequestsView ignores the new fields.
- **BANNERSH-67: free-first AI request flow.** `POST /api/design-requests/ai` is now `[AllowAnonymous]` + `[ServiceFilter(typeof(BotProtectionFilter))]` and no longer creates a Stripe PaymentIntent. `DesignRequest.UserId` became nullable and `IpAddress` (varchar 45) was added (migration `AddDesignRequestAnonymousFields`). Anonymous callers get 1 free generation per IP per rolling 30 days (`IAiCreditService.IsAnonymousEligibleAsync`); auth callers get 1 free per user (`User.HasUsedFreeAiGeneration`) then must consume credits. 402 paywall response carries `{ reason, creditsRemaining, paywallOptions: { creditPackPriceNok, creditPackCount, bannerOrderActivationFeeNok, manualDesignerUrl, uploadOwnUrl } }` sourced from `PricingParameter` keys seeded by BANNERSH-65. `MarkPaidAndEnqueueAsync` for `mode=Ai` is now a dead-code guard (log + no-op) — only Manual requests still go through Stripe. The pipeline buckets anonymous file output under user "0" via `request.UserId ?? 0`. Frontend (`apiClient`, `createAiRequest`) still expects the old `{ clientSecret, totalNok }` shape — owner of follow-up BANNERSH-69 should rewire it to `{ requiresAuth, creditsRemaining }`.
- **BANNERSH-162: quality/size picker placement + image-ratio-driven sizing.** In `AiBannerBuilderView`, the "Velg kvalitet og størrelse" block is rendered *below* the generated preview (not above it) and is hidden until `genPhase === 'ready' && currentDesignRequest.previewUrl`. The displayed widths come from `aiImageAspectRatio` (preferred: `aiImageNaturalRatio` captured by the preview `<img>`'s `@load` handler; fallback: parsing `currentDesignRequest.aspectRatio` as `WxH` or `A:B`). High → 150 cm tall × `round(150 × ratio)` wide; Good → 180 cm tall × `round(180 × ratio)` wide; Custom width ↔ height are linked via the same ratio (lock cleared on `nextTick` so Vue's same-value skip doesn't strand the guard). `step2Valid` only requires custom W/H once `genPhase === 'ready'`, since the picker is invisible during the pre-generation form. `aiImageNaturalRatio` is reset in `generateBanner`, `regenerate`, `selectPastDesign`, and `returnToWizardIdle` so the next image's `@load` is authoritative.
- **BANNERSH-139: credit packs tracked as Orders.** AI credit-pack purchases now write a real `Order` row with `OrderType = CreditPack` (enum value 3) so transaction reports pick them up. The buy endpoint (`AiCreditsController.BuyCreditPack`) creates the Order + a synthetic `OrderItem` *before* asking Stripe for a PaymentIntent, then stamps the PI id on the order. `IStripePaymentService.CreateCreditPackPaymentIntentAsync` now takes an optional `orderId` so the PI metadata includes it. The webhook handler still routes `type=ai_credit_pack` to the credits-grant path, AND additionally calls `OrderService.MarkPaidAsync(pi, orderId)` — `MarkPaidAsync` skips production-row seeding AND the order-confirmation email for `OrderType=CreditPack` orders (those flows don't apply). Admin orders list (`OrderService.ListAllAsync` + `AdminOrderFilter.IncludeCreditPacks` + `GET /api/admin/orders?includeCreditPacks=true`) hides them by default; the type filter overrides the hide. Frontend admin OrdersView has an "Inkluder AI-kjøp" checkbox + a gold `CreditPack` chip; account OrdersView + AccountView label them "AI-pakke" with a matching chip.

## Stripe webhook (BANNERSH-166)
Yes — the app uses Stripe webhooks. The endpoint is:

```
POST /api/webhooks/stripe
```

In production (`make up`) the backend listens on port **17080**, so the URL to register in the Stripe dashboard is:

```
https://<your-domain>/api/webhooks/stripe
```

(replace `<your-domain>` with the public hostname/IP pointed at the server, e.g. via nginx/Caddy that proxies `:443` → `:17080`).

Events handled: `payment_intent.succeeded` (banner orders, AI credit packs, manual design requests) and `payment_intent.payment_failed`.

The **webhook signing secret** (`whsec_…`) from the Stripe dashboard must be saved to `system_settings.stripe_webhook_secret` via `/admin/settings` — no appsettings fallback.

For local dev with the Stripe CLI:
```bash
stripe listen --forward-to localhost:5000/api/webhooks/stripe
# Copy the printed "whsec_…" into /admin/settings → Stripe Webhook Secret
```

## API keys: DB-only (BANNERSH-161)
All secret API keys live in `system_settings` and are set via `/admin/settings`. **There is no appsettings fallback.** Affected rows:
- `openai_api_key` → consumed by `OpenAiImageService` AND `OpenAiPromptRefinementService` (both inject `ISystemSettingsService`; refinement is silently skipped → base prompt if the key is blank)
- `stripe_secret_key`, `stripe_webhook_secret`, `stripe_publishable_key` → consumed by `StripePaymentService` and `ConfigController` (frontend pulls publishable via `GET /api/config/stripe`)

Non-secret tuning (`OpenAi:ImageModel`, `OpenAi:ImageQuality`, `OpenAi:BaseUrl`, `Stripe:Currency`, etc.) **stays in appsettings** — these are the only `OpenAiOptions` / `StripeOptions` fields now.

Heads-up: when adding a new SystemSettings migration with only seed data, EF won't generate the `.Designer.cs` automatically if you wrote the `.cs` by hand. Always use `dotnet ef migrations add <Name>` (see CLAUDE.md migrations section) — running `dotnet ef migrations list` will reveal a missing migration if the Designer.cs is absent.
