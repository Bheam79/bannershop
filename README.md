# BannerShop.no

Online shop for ordering printed banners from our print shop.

## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend | C# .NET 10 / ASP.NET Core Web API |
| Frontend | Vue 3 + Vite + Tailwind CSS v4 |
| Database | MariaDB 11 (via EF Core / Pomelo) |
| Auth | JWT Bearer tokens |
| Payments | Stripe |
| Shipping | Bring/Posten API |

## Project Structure

```
BannerShop.slnx            # .NET solution
BannerShop.Core/           # Domain entities & enums
BannerShop.Infrastructure/ # EF Core DbContext, migrations, repositories
BannerShop.Api/            # ASP.NET Core Web API
frontend/                  # Vue 3 + Vite + Tailwind CSS
e2e/                       # Playwright E2E tests
docker-compose.yml         # MariaDB for local dev (outside dev container)
```

## Running Locally

### Prerequisites
- .NET 10 SDK
- Node 22+
- Docker (for MariaDB) — or use the dev container

### 1. Start the database

**Outside dev container (standard local dev):**
```bash
docker compose up -d
```

**Inside dev container:**
```bash
cb docker run mariadb:11 --name db \
  -v /workspace/mariadb-data:/var/lib/mysql \
  -e MYSQL_ROOT_PASSWORD=root_dev \
  -e MYSQL_DATABASE=bannershop \
  -e MYSQL_USER=bannershop \
  -e MYSQL_PASSWORD=bannershop_dev
# Get DB IP:
DB_IP=$(cb docker exec db hostname -I)
# Update appsettings.json with the DB IP
```

### 2. Configure appsettings

Copy and edit `BannerShop.Api/appsettings.json`:
- Set `ConnectionStrings:DefaultConnection` to point to your MariaDB
- Set `Jwt:Secret` (min 32 characters)
- Set `Stripe:SecretKey` and `Stripe:WebhookSecret`
- Set `Bring:ApiUid` and `Bring:ApiKey` (or leave as placeholder to use mock)
- Set `Admin:SeedEmail` and `Admin:SeedPassword`

### 3. Run migrations

```bash
BANNERSHOP_DB="Server=<db-host>;Port=3306;Database=bannershop;User=bannershop;Password=bannershop_dev;" \
  dotnet-ef database update \
  --project BannerShop.Infrastructure \
  --startup-project BannerShop.Api
```

### 4. Start the backend

```bash
cd BannerShop.Api
dotnet run
# API available at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

### 5. Start the frontend

```bash
cd frontend
cp .env.example .env
# Edit .env with your Stripe publishable key
npm install
npm run dev
# Frontend at http://localhost:5173
```

## Database Migrations

```bash
# Add a new migration
dotnet-ef migrations add <MigrationName> \
  --project BannerShop.Infrastructure \
  --startup-project BannerShop.Api \
  --output-dir Data/Migrations

# Apply migrations
dotnet-ef database update \
  --project BannerShop.Infrastructure \
  --startup-project BannerShop.Api
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__DefaultConnection` | MariaDB connection string |
| `Jwt__Secret` | JWT signing secret (min 32 chars) |
| `Stripe__SecretKey` | Stripe secret key (`sk_test_...`) |
| `Stripe__WebhookSecret` | Stripe webhook signing secret |
| `Stripe__PublishableKey` | Stripe publishable key (for frontend) |
| `Bring__ApiUid` | Bring API user ID |
| `Bring__ApiKey` | Bring API key |
| `Bring__SenderPostalCode` | Shop's postal code for shipping calc |
| `Bring__SenderCountryCode` | Shop's country code (default `NO`) |
| `Bring__ProductCodes` | Comma-separated Bring product codes (default `SERVICEPAKKE`) |
| `Bring__ClientUrl` | Identifier sent in `X-Bring-Client-URL` header |
| `Admin__SeedEmail` | Initial admin user email |
| `Admin__SeedPassword` | Initial admin user password |
| `VITE_STRIPE_PUBLISHABLE_KEY` | Stripe publishable key (frontend `.env`) |

## Shipping (Bring/Posten)

Checkout uses the Bring Shipping Guide 2.0 API to quote shipping cost.

- Configure credentials via `Bring__ApiUid` and `Bring__ApiKey` (env or appsettings).
- If credentials are missing / left as `REPLACE_WITH_...`, a deterministic mock
  implementation is wired up so the rest of the checkout flow stays usable in dev.
- The sender postal code (`Bring__SenderPostalCode`) and country (`Bring__SenderCountryCode`)
  are also configurable. The product code(s) requested default to `SERVICEPAKKE`
  (Bring Bedriftspakke) and can be overridden via `Bring__ProductCodes` (comma-separated).

The endpoint is:

```
POST /api/shipping/calculate
{
  "postalCode": "5003",
  "city": "Bergen",
  "bannerSizeId": 1,
  "customWidthCm": null,
  "qty": 1
}
```

Response includes `standard` and `express` options. Express uses the same shipping
cost; the 500 NOK express production fee is added on top (from the `express_fee`
pricing parameter).

Parcel dimensions for the carrier rating are derived from the banner size and
material gsm; the relevant knobs live in the `PricingParameter` table:

| Key                              | Default | Meaning                                  |
|----------------------------------|---------|------------------------------------------|
| `shipping_tube_diameter_cm`      | 15      | Estimated rolled tube diameter           |
| `shipping_packaging_weight_g`    | 500     | Tube + endcaps + label weight            |
| `shipping_max_length_cm`         | 240     | Carrier max parcel length                |

Results are cached in memory for 1 hour per (postal code, parcel dimensions) to
avoid hammering the Bring API.

## Orders, Payments & Production

The full order flow is implemented:

```
Draft → PendingPayment → Paid → InProduction → ReadyToShip → Shipped → Delivered
                                                                      ↘ Cancelled
```

### Customer endpoints (require Bearer token)

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/orders/draft` | Create a draft order + Stripe PaymentIntent. Body: `{ deliveryType, shippingAddress, items: [{ bannerSizeId, customWidthCm?, quantity, notes }] }`. Returns `{ orderId, clientSecret, totalNok, breakdown }`. |
| `GET`  | `/api/orders?page=1&pageSize=20` | List the caller's orders, newest first. |
| `GET`  | `/api/orders/{id}` | Full order detail (items, production history, shipment). |
| `POST` | `/api/orders/{id}/cancel` | Cancel a `Draft` or `PendingPayment` order. |

### Admin endpoints (require Admin role)

| Method | Path | Purpose |
|--------|------|---------|
| `GET`  | `/api/admin/orders?status=&fromUtc=&toUtc=&search=&page=&pageSize=` | Paginated list with filters. `search` matches order id, customer email & name. |
| `GET`  | `/api/admin/orders/{id}` | Full detail. |
| `PUT`  | `/api/admin/orders/{id}/status` | `{ status }` — set order status. |
| `PUT`  | `/api/admin/orders/{id}/items/{itemId}/production` | `{ stage, notes? }` — append a production-stage row. Auto-promotes order to `InProduction` and (when every item is `ReadyToShip`) to `ReadyToShip`. |
| `POST` | `/api/admin/orders/{id}/shipping` | `{ carrier, trackingNumber, trackingUrl?, shippedAt?, estimatedArrival? }` — create/update shipment tracking and set status to `Shipped`. |

### Stripe webhook

`POST /api/webhooks/stripe` — verifies the `Stripe-Signature` header against
`Stripe__WebhookSecret` and reacts to:

- `payment_intent.succeeded` → marks order `Paid`, seeds `Queued`
  `ProductionStatus` rows for each item.
- `payment_intent.payment_failed` → logs the failure; order stays at
  `PendingPayment` so the customer can retry.

The handler is idempotent — repeated webhook delivery is a no-op.

### Pricing snapshot

When `POST /api/orders/draft` is called, the server:

1. Validates and loads each `BannerSize` (with material).
2. Calls `IPricingService` per item to compute `UnitPriceNok` and snapshots
   that, the area, and the line total onto the `OrderItem` rows so historic
   orders retain their pricing forever.
3. For each item, calls `ParcelCalculator` + `IShippingService` to compute
   shipping cost (sum over items). Express adds the `express_fee` pricing
   parameter as a production surcharge.
4. Estimated delivery = today + `standard_lead_time_days` (default 14) or
   `express_lead_time_days` (default 3), plus the largest carrier transit time.
5. Creates an `Address` row, the `Order` (status `PendingPayment`), then the
   Stripe PaymentIntent (amount in øre = NOK × 100, metadata `{ orderId, userId }`).

If Stripe credentials are missing/placeholder, the API auto-wires
`MockStripePaymentService` so the rest of the flow can be exercised in dev
(client secrets are obviously fake — the frontend Stripe.js confirm step will
fail in dev until real keys are configured).

## Seeded Data

On first migration the following data is seeded:

**Materials:**
- 400g Frontlit Banner (160cm wide) — available immediately
- 680g Heavy Duty Banner (180cm wide) — available from 2026-08-31

**Standard Banner Sizes:**
- 300×150 cm, 350×150 cm, 400×150 cm, 450×150 cm, 500×150 cm
- Custom width × 150 cm (price calculated)
- 300×180 cm — fixed price 699 NOK (180cm material, pre-order)

## E2E Tests (Playwright)

The `e2e/` folder contains a Playwright TypeScript test project covering all critical user journeys.

### Prerequisites

- Backend API running at `http://localhost:5000` (or set `API_URL`)
- Frontend dev server running at `http://localhost:5173` (or set `BASE_URL`)
- Chromium browser (ships with the system Playwright install)

### Setup

```bash
cd e2e
cp .env.example .env
# Edit .env — set TEST_USER_EMAIL, TEST_USER_PASSWORD, ADMIN_EMAIL, ADMIN_PASSWORD
npm install
```

### Running tests

```bash
cd e2e

# Run all tests (headless)
npx playwright test

# Run a specific suite
npx playwright test tests/shop.spec.ts
npx playwright test tests/checkout.spec.ts
npx playwright test tests/account.spec.ts
npx playwright test tests/admin.spec.ts

# Run headed (see the browser)
npx playwright test --headed

# Open interactive UI
npx playwright test --ui

# View HTML report
npx playwright show-report
```

### Test suites

| File | Covers |
|------|--------|
| `tests/shop.spec.ts` | Home page, banner sizes, pricing, shipping estimator, add to cart |
| `tests/checkout.spec.ts` | Checkout form validation, delivery toggle, payment, confirmation |
| `tests/account.spec.ts` | Register, login, order list, order detail, production stages, tracking |
| `tests/admin.spec.ts` | Admin access control, sizes CRUD, pricing params, orders, production, shipping |

### Notes

- Tests that require an admin user or test user will be skipped (not fail) if
  the credentials are not configured.
- The mock Stripe flow (`pi_mock_` client secret) is used in dev; real Stripe
  is tested in CI with Stripe test cards when real keys are configured.
- Screenshots are captured on failure in `test-results/`.
- Run `npx playwright install chromium` if the system browser is not found.
