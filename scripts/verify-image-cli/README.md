# Targeted image CLI verification

Run `dotnet run --project scripts/verify-image-cli/VerifyImageCli.csproj` on Linux.
This standalone console check does not run the project test suite. It uses an
in-memory database, temporary files and fixture CLI executables; no paid image
requests or real credentials are used. It checks provider overlap, single-result
fallbacks, invalid/solid images, process timeout, credential rotation and masking,
two persisted previews/print files, selection consistency, and failed-pair refunds.
Requires Python 3 for the fixture executables. Temporary files are removed on exit.

Optional `-- native-login` verifies that installed Codex and Grok CLIs emit device
login URLs/codes the admin UI can display, then cancels both login processes.
It does not complete account consent or exercise paid image generation.

Optional `-- prompts-only` captures the actual Codex/Grok CLI prompt transport
with fixture executables, including portrait/no-portrait requests, Grok's system
override and separate provider instructions. It also checks the admin prompt
catalogue's defaults/overrides, model configuration and secret exclusion. It does
not prove real-model portrait fidelity; that requires a real generation and visual
comparison of the supplied portrait and output.
