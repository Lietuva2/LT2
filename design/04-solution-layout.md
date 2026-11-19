# 4. Solution layout

```
LT2.sln
Directory.Build.props          nullable, warnings as errors, analyzers, NuGet audit, deterministic builds
Directory.Packages.props       central package versions
global.json                    pinned SDK; tests on Microsoft.Testing.Platform
src/
  Lt2.Web/                     ASP.NET Core host: API endpoints, Blazor server rendering, auth, security middleware
    Endpoints/<Module>/        one file per endpoint or per close group of endpoints
    Components/                layouts and the server-rendered pages (MP profile, bill, votes, agenda, summaries, …)
    Services/                  the server's implementations of the API clients, composing the modules' interfaces
    Security/                  headers, antiforgery and Origin filter, trusted proxy, IP hashing, staff role, terms gate
    Jobs/                      the Hangfire dashboard (3, 6.3)
  Lt2.Web.Client/              Blazor WebAssembly: interactive components, HTTP implementations of the API clients
  Lt2.Api.Contracts/           DTOs, route constants (ApiRoutes), client interfaces, FluentValidation validators
  Lt2.SharedKernel/            Result type, identifiers, the modules' shared interfaces (outbox, audit, settings, …),
                               the EF Core set-up (Persistence/), VilniusTime, the outbound allowlist (Http/)
  Lt2.Hosting/                 Serilog and OpenTelemetry, set up the same way by the web host and the worker
  Modules/
    Lt2.Identity/              Domain/ Application/ Infrastructure/ (internal), public interfaces in PublicApi.cs
    Lt2.Parliament/            + Import/ (open-data client, agenda pages, parsers, upserts) and Statistics/
    Lt2.Appeals/               + Ai/ (the AI assistant and its public prompts) and Screening/
    Lt2.Notifications/         + Digests/, MpMail/, Follows/, Rhythm/, Outbox/, Templates/, Transports/
    Lt2.Audit/                 the audit log and the runtime settings (Settings/)
    Lt2.Usage/                 the site's usage statistics (Counting/)
  Lt2.Jobs/                    the Hangfire job classes: run by the worker, loaded by the web host's dashboard (3, 6.3)
  Lt2.Worker/                  Hangfire server: recurring jobs, outbox dispatcher
  Lt2.Migrator/                creates roles, applies migrations for every module, grants; seeds invitations and
                               (only where Demo:Allowed) the invented demonstration data; run once per deploy
tests/
  Lt2.UnitTests/               counted rules, validators, templates; bUnit tests of components (Components/)
  Lt2.IntegrationTests/        the API over HTTP against a real PostgreSQL (Testcontainers)
  Lt2.Parliament.ImportTests/  Seimas client, parsers against recorded Seimas XML and agenda HTML, the import
  Lt2.ArchitectureTests/       module boundaries, the read-only role, static assets, the connection budget (9.5)
  Lt2.E2E/                     Playwright and stack checks against a running compose stack; 1280 px and 390 px
  Shared/                      TestOrder.cs, compiled into every test project
tools/
  Lt2.SeimasStandIn/           local and test only: invented Seimas open data and lrs.lt agenda pages; never shipped
  standin_overlay.py …         the scripts that write the stand-in's invented data
  run_tests.py                 runs a test project in a chosen order
deploy/
  compose.yaml  Dockerfile  Caddyfile  .env  init-secrets.py  secrets/
  compose.shared-proxy.yaml  shared-proxy.Caddyfile    behind a server's existing Caddy (9.2)
.github/workflows/ci.yml       build, NuGet audit, unit, architecture and integration tests; end-to-end on a compose stack
```

Conventions:

- Inside a module everything is `internal` except the module interface, its DTOs and its events.
- A feature's parts are named alike across the solution: its contract and validator in `Lt2.Api.Contracts/<Area>/`, its
  handler in the module's `Application/`, its endpoint in `Lt2.Web/Endpoints/<Module>/`, its tests in the test projects.
- Domain rules that people will argue about (the two-thirds faction rule, participation, digest eligibility, the party
  group rule) are pure functions with unit tests whose names read as the rule.
- Every user-facing string goes through `IStringLocalizer`; the only language is Lithuanian.
- Code lives on `master`, changed through branches with merge commits; `docs` keeps the website.
