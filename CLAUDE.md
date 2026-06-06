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
Current DB IP: `10.89.7.3` (changes each time the container restarts — re-read with the `DB_IP=$(cb docker exec db hostname -I | tr -d ' ')` snippet above; appsettings.json is hard-coded to a previous IP and may need updating).

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

## Banner builder (BANNERSH-15)
- File uploads land under `FileStorage:BasePath` (default `/workspace/uploads`) and are served via StaticFiles at `FileStorage:PublicUrlPrefix` (default `/uploads`).
- `IImageProcessingService` uses `SixLabors.ImageSharp` for raster ops and `PDFtoImage` for PDF→PNG (first page only, 200 DPI).
- `PDFtoImage` v4 has int-page overloads marked obsolete — use the `Index`-based overload.
- Dimension math lives in pure helper `BannerDimensions` (covered by unit tests in `BannerDimensionsTests`).
