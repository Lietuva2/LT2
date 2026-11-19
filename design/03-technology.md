# 3. Technology choices

| Concern | Choice | Notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# 14 | Supported until November 2028, which covers the 2028 Seimas election. |
| Database | **PostgreSQL 17** | `jsonb` for snapshots and settings, `FOR UPDATE SKIP LOCKED` for the outbox. |
| ORM | **EF Core 10** with **Npgsql**, `EFCore.NamingConventions` (snake_case) | One `DbContext` per module, each on its own schema. Raw SQL only for aggregate queries and materialised statistics. `Lt2.Migrator` applies every module's migrations. |
| Web API | ASP.NET Core **Minimal APIs**, one file per endpoint or per close group of endpoints, `/api/v1`, built-in OpenAPI document (`/openapi/v1.json`), RFC 9457 problem details | The API is complete; the UI uses nothing else. |
| UI | **Blazor Web App**: static server rendering for public pages, **Interactive WebAssembly** for signed-in features | See 3.1. |
| Request handling | Plain handler classes injected into endpoints | No mediator library: MediatR and MassTransit are commercial, and the indirection buys little here. |
| Validation | **FluentValidation**, validators in the shared contracts project | Same rules run in the browser and on the server. |
| Mapping | Hand-written | No mapping library (AutoMapper is commercial). |
| Sign-in | **ASP.NET Core Identity** on EF Core: email one-time code, passkeys, no passwords | See 6.2. |
| Background jobs | **Hangfire** with PostgreSQL storage (schema `hangfire`) for scheduled and retried jobs; own **transactional outbox** table for emails and events | The job classes are in `Lt2.Jobs`; the worker runs them. The Hangfire dashboard "Foniniai darbai", `/administravimas/darbai` in the web host, shows failed runs and runs a recurring job by hand, over the worker's Hangfire storage (no Hangfire server in the web host). Administrator in a staff-passkey session only (6.3). The import's manual jobs, run only by "Trigger now", are in 7.2. |
| HTTP to Seimas | `HttpClientFactory` + `Microsoft.Extensions.Http.Resilience` (retry, timeout, circuit breaker) | The service is slow at times and drops connections. |
| Caching | `IMemoryCache` in each process | No Redis until there is a second web instance. |
| Time | `TimeProvider`, UTC in the database (`timestamptz`), `Europe/Vilnius` for schedules and display | |
| Identifiers | UUID v7 for our own rows; Seimas identifiers kept as natural keys; public URLs use slugs (`/p/jonas-petraitis`) and bill numbers (`/seimas/XVP-0412`) | |
| Logging and telemetry | **Serilog**, **OpenTelemetry** traces, metrics and logs over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set | No backend is chosen (9.4). No personal data in logs. |
| Email | SMTP through **MailKit** behind `IEmailTransport`; MP mail on its own stream (`Email:Mp`) | The provider is not chosen; locally and on the trial Mailpit catches every email. |
| SMS | None: accounts are confirmed by email (6.2). | |
| Bot check | A self-hosted proof of work in the ALTCHA protocol, written in the repository (`BotCheckRules`, `lt2-botcheck.js`; no package, no third-party script) | On registration and on asking for a sign-in code by email; nothing for the person to do or click (6.2, 6.4). |
| AI | One interface, `IAiAssistant`, on the **Claude API** (first-party), model the setting `Ai:DraftModel` (default `claude-sonnet-5-5`): the draft of a bill's description in a moderator's hands (8.11.4) and the AI summaries of bills (8.13). On only with `Ai:Provider` = `claude` and a key; otherwise a "not configured" implementation answers and nothing offers a draft. Pre-screening and duplicate-reason hints are rule-based: users' texts can hold personal data and this API has no EU-only processing. | No personal data is sent. Prompts are kept in the repository (`src/Modules/Lt2.Appeals/Ai/*.prompt.md`) so they can be published. |
| Reverse proxy | **Caddy** | Automatic certificates, HTTP/3, simple configuration. |
| Tests | xUnit v3 on Microsoft.Testing.Platform, Shouldly, bUnit, **Testcontainers** (real PostgreSQL), **Playwright** for end-to-end, `NetArchTest` for module boundaries | |

## 3.1 Why Blazor, and which Blazor

The product is a client application over a full API, in Blazor. The options weighed:

| | Blazor Web App (SSR + WASM) | Standalone Blazor WASM | React / SvelteKit + API |
|---|---|---|---|
| Public pages indexable, with share previews | Yes, server-rendered | No: empty shell until WASM loads | Yes, with a Node SSR server |
| First load from a social link on a phone | Fast: HTML first, WASM only where needed | Slow: several MB before anything shows | Fast |
| Uses the API for everything | Yes in the browser; in-process on the server (same handlers) | Yes | Yes |
| One language, shared DTOs and validators | Yes | Yes | No |
| Team familiarity | Yes | Yes | No |
| Extra moving parts | None | None | Node runtime, second build, token or BFF layer |
| Ecosystem and hiring | Smaller | Smaller | Largest |

**Blazor Web App on .NET 10 with per-page render modes.** Most visitors arrive from social networks on a phone and only
read, so MP profiles and bill pages are static server-rendered HTML with proper Open Graph tags and no WebAssembly
download. Everything a signed-in person does (taking a side, writing a reason, settings, moderation, administration) is an
Interactive WebAssembly component that talks only to `/api/v1`. Until such an island has started, everything in it is
disabled (fields, choices and buttons alike): what was typed would be emptied when the island takes over, and a form sent
natively would reload the page. Without JavaScript the island never starts, and the page says so in one note. An
end-to-end test loads every page with islands with the WebAssembly runtime blocked and fails on any control left usable.
The WebAssembly client logs warnings and errors only (its minimum level is Warning, in every environment), so the HTTP
client factory's line per request stays out of the browser's console.

How the full API holds with server rendering: every component depends on a typed client interface from
`Lt2.Api.Contracts` (`IParliamentApi`, `IAppealsApi`, …). In the browser the implementation is an `HttpClient` call; on the
server it calls the same application handler the endpoint calls. Integration tests exercise the HTTP path for every
operation, so the API cannot fall behind the UI.

React would be the choice if a large front-end team or a native mobile app were planned. Neither is, and the cost is a
second stack for a team of one or two.

UI components: no component library. MudBlazor and similar need interactivity for most controls, which defeats static
rendering. The visual language (`--slate`, `--leaf`, `--brand`, the chips, the result card) is a small set of Razor
components and one stylesheet (`wwwroot/css/lt2.css`); the interface prototype is in `design/ui/`. The seat map and
charts are SVG rendered by Razor, with no chart library; `lt2-logo-spin.js` animates the logo.
