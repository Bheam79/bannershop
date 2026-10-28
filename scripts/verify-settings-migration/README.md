# SystemSettings startup migration smoke (BANNERSH-316)

Standalone MariaDB regression check; does not run the project test suite, start
the web app, or contact any external provider. Each scenario creates a uniquely
named database, applies the real EF migrations with production's retry strategy,
and drops that database in `finally`.

Use **only a disposable MariaDB server**. The supplied account needs CREATE/DROP
DATABASE privileges. Example from the project checkout:

```bash
cb docker ls
cb docker run mariadb:11 --purpose test --name bannersh316verify \
  -e MARIADB_ROOT_PASSWORD=bannersh316_local_only
cb docker exec bannersh316verify healthcheck.sh --connect --innodb_initialized
# Read the sibling's IP; do not use the host's production database.
cb docker exec bannersh316verify hostname -I
SETTINGS_SMOKE_MYSQL='Server=<IP>;User=root;Password=bannersh316_local_only;' \
  dotnet run --project scripts/verify-settings-migration
cb docker stop bannersh316verify
cb docker rm bannersh316verify
```

The original reproduction migrates to `AddFalOrderUpscaleKey`, saves CLI
credentials using auto-generated IDs (the first occupies ID 20), then calls
`db.Database.Migrate()` exactly as startup does. Before the fix, running with
`-- --expect-collision` confirms MariaDB error 1062 for primary key 20. After the
fix, run without that flag: the same upgrade must succeed, preserve all settings,
record migration history, restart idempotently, and roll back/reapply without
deleting the credential at ID 20. Also covers fresh installs, an existing SMS key
at another ID (customized or intentionally blank), model/snapshot consistency,
and databases that already applied the original migration.

Production recovery: update/build/install the corrected source through the normal
deployment process (`make up` as the deployment user). Startup retries the pending
migration automatically. No manual row deletion, renumbering, migration-history
editing, or credential reset is needed. This check does not deploy to production.
