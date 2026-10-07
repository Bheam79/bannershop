# Admin new-order alerts — offline smoke

Run from the repository root:

```bash
dotnet run --project scripts/verify-admin-order-notifications/VerifyAdminOrderNotifications.csproj
```

Uses the real `OrderService`, admin notifier, settings service and settings masking
controller with EF InMemory, recording email and HTTP fixtures. No external email,
SMS, Stripe or database calls; no project test suite. Covers paid-order triggering,
current admin profiles, Norwegian phone normalization, query encoding, repeated
webhooks, manual/AI/mock-payment paths, credit-pack exclusion and isolated failures.

Delivery is best-effort at the first confirmed-payment transition, like customer
confirmation emails. It does not retry failed deliveries. SMS uses the supplied
HTTP gateway, a 10-second timeout and the masked DB `admin_order_sms_key` setting;
clearing the key disables SMS. Email uses existing `Email:*` SMTP configuration.
The supplied gateway key is seeded by migration and can be changed in
`/admin/settings`. No fixed recipient is used; admins update contacts in their
account profile. Invalid/missing phones skip only SMS.
