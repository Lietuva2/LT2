# 4. Solution layout

```
LT2.sln
Directory.Build.props          nullable, warnings as errors, analyzers, deterministic builds
Directory.Packages.props       central package versions
global.json                    pinned SDK
src/
  Lt2.Web/                     ASP.NET Core host: endpoints, Blazor server rendering, auth, security middleware
    Endpoints/                 one file per endpoint, grouped by module
    Components/                layouts, static pages (MP profile, bill, appeal, digest archive)
    Security/                  headers, antiforgery filter, rate-limit policies, authorization handlers
  Lt2.Web.Client/              Blazor WebAssembly: interactive components, HTTP implementations of the API clients
  Lt2.Api.Contracts/           DTOs, route constants, client interfaces, FluentValidation validators
  Lt2.SharedKernel/            Result type, clock, identifiers, domain and integration event base types
  Modules/
    Lt2.Identity/              Domain/ Application/ Infrastructure/ (internal), public IIdentityModule
    Lt2.Parliament/            + Import/ (open-data client, parsers, upserts) and Statistics/
    Lt2.Appeals/
    Lt2.Notifications/         + Digests/, Templates/, Transports/
    Lt2.Audit/
  Lt2.Worker/                  Hangfire server: recurring jobs, outbox dispatcher
  Lt2.Migrator/                applies migrations for every module, creates roles and grants; run once per deploy
tests/
  Lt2.UnitTests/               domain rules: statistics, digest eligibility, thresholds
  Lt2.IntegrationTests/        API over a real PostgreSQL (Testcontainers), one fixture per module
  Lt2.Parliament.ImportTests/  parsers against recorded Seimas XML and agenda HTML
  Lt2.ArchitectureTests/       module boundaries, no direct cross-schema access
  Lt2.E2E/                     Playwright: sign-up, take a side, moderation, digest preview; 1280 px and 390 px
deploy/
  compose.yaml  compose.staging.yaml  Caddyfile  backup/  runbooks/
.github/workflows/             ci.yml (build, test, scan), deploy.yml (manual approval for production)
```

Conventions:

- Inside a module everything is `internal` except the module interface, its DTOs and its events.
- A feature is a folder with its endpoint contract, validator, handler and tests: a vertical slice, not layers by type.
- Domain rules that people will argue about (the two-thirds faction rule, participation, digest eligibility, the party
  group rule) are pure functions with unit tests whose names read as the rule.
- Every user-facing string goes through `IStringLocalizer`; the only language at launch is Lithuanian.
- Code lives on `master`, which is empty today, through pull requests with merge commits; `docs` keeps the website and
  `discovery/`. **[decided]**

