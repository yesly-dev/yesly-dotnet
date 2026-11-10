# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

The official .NET SDK, `Yesly` on NuGet. Targets `net8.0`; solution `Yesly.slnx`. Usage is in `README.md`.

## Commands

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~<name>"   # one test or class
node ../yesly-openapi/sdk/generate.mjs dotnet .    # regenerate src/Yesly/Generated/
YESLY_API_KEY=ysk_test_… dotnet run scripts/smoke.cs   # smoke test against the real API
```

Smoke tests run against staging with a `ysk_test_` key and `YESLY_BASE_URL=https://api.dev.yesly.dev/v1`; they create and clean up one object per resource.

## How it is built

The API-shaped part is generated from `yesly-openapi/openapi.yaml` by `yesly-openapi/sdk/generate.mjs` (`node ../yesly-openapi/sdk/generate.mjs dotnet .`): `src/Yesly/Generated/`. Never edit it by hand: change the spec in `yesly-openapi`, lint it, and regenerate with both repos checked out side by side. Everything else is hand-written: `YeslyClient.cs`, `Options.cs`, `Exceptions.cs`, `Page.cs`, `Webhook.cs`, `Version.cs`. The hand-written client sends `Authorization: Bearer <key>` and `Yesly-Version`, retries with backoff honouring `Retry-After`, adds an automatic `Idempotency-Key` to writes, pages with `starting_after`, and verifies webhook signatures. The procedure for an API change across all SDKs is `yesly-openapi/docs/sdks.md`; package names must match `yesly-web/src/lib/sdks.ts`.

## Releasing

Bump `<Version>` in `src/Yesly/Yesly.csproj` and `YeslyVersion.Current`, then push a tag `v<version>`.
