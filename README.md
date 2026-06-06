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

## Seeded Data

On first migration the following data is seeded:

**Materials:**
- 400g Frontlit Banner (160cm wide) — available immediately
- 680g Heavy Duty Banner (180cm wide) — available from 2026-08-31

**Standard Banner Sizes:**
- 300×150 cm, 350×150 cm, 400×150 cm, 450×150 cm, 500×150 cm
- Custom width × 150 cm (price calculated)
- 300×180 cm — fixed price 699 NOK (180cm material, pre-order)
