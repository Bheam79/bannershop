Targeted offline verification for BANNERSH-311 (does not run the project suites).

```bash
dotnet run --project scripts/verify-order-phone/VerifyOrderPhone.csproj
# Separate terminal, task's temporary port:
cd frontend && CHOKIDAR_USEPOLLING=true npm run dev -- --host 127.0.0.1 --port 10000 --strictPort
# Repo root, with e2e dependencies/Chromium installed:
node scripts/verify-order-phone/browser.mjs
```

The service check uses an in-memory SQLite database and mocked Stripe/Bring services. It verifies invalid numbers, contact snapshots for Standard/Express/Pickup, both detail APIs, profile changes, and legacy orders. The browser check mocks every API request and checks order/confirmation display, checkout validation, payment submission, saved contact reuse, and invalidation of a draft when the number changes.
