# Lietuva 2.0

Code for the Lietuva 2.0 (LT2) Seimas engagement platform. This branch (`master`) holds the product code. **The
system design this code follows is [`design/system-design.md`](design/system-design.md)**; this is milestone M0
"Foundation" from its section 11. Rules for changing the code are in `AGENTS.md`. The concept website stays on the
`docs` branch.

The user interface is in Lithuanian only. All names and data in examples and tests are fictional.

## What is here

A modular monolith on .NET 10 (design sections 2–4):

| Path | What |
|---|---|
| `src/Lt2.Web` | ASP.NET Core host: `/api/v1` minimal-API endpoints (one per file), Blazor server rendering, security plumbing |
| `src/Lt2.Web.Client` | Blazor WebAssembly: the interactive sign-in, registration and "hello" pages; HTTP implementations of the API clients |
| `src/Lt2.Api.Contracts` | DTOs, routes, typed client interfaces, FluentValidation validators shared by browser and server |
| `src/Lt2.SharedKernel` | Result type, identifiers, outbox, audit and settings abstractions |
| `src/Modules/Lt2.Identity` | Accounts, registration by invitation, emailed sign-in codes, passkeys, sessions — schema `identity` |
| `src/Modules/Lt2.Notifications` | Transactional outbox, dispatcher, SMTP transport, the sign-in code email — schema `notify` |
| `src/Modules/Lt2.Audit` | Append-only audit log and runtime settings — schemas `audit`, `config` |
| `src/Modules/Lt2.Parliament`, `Lt2.Appeals` | Empty module shells (schema and `DbContext` only) for milestones M1 and M2 |
| `src/Lt2.Worker` | Hangfire server (PostgreSQL storage) and the outbox dispatch loop |
| `src/Lt2.Migrator` | Creates the database roles, applies every module's migrations, grants access; run once per deploy |
| `tests/` | Unit, integration (Testcontainers), architecture (NetArchTest) and end-to-end (Playwright) tests |
| `deploy/` | Dockerfile, `compose.yaml`, `Caddyfile`, secret generators |

## Run it locally

Needs Docker (Docker Desktop on Windows or macOS).

```bash
python deploy/init-secrets.py
```

This writes random local secrets to `deploy/secrets/` (git-ignored; existing files are kept). Then:

```bash
docker compose -f deploy/compose.yaml up --build
```

- Site: <https://lt2.localhost:8443>. Caddy serves it with its own local certificate authority, so the browser warns
  about the certificate the first time; accept it for this local address. (`*.localhost` resolves to your machine in
  Chrome, Edge and Firefox.)
- <http://lt2.localhost:8080> redirects there. If Windows has reserved port 8080 (`netsh int ipv4 show
  excludedportrange protocol=tcp`), set another, for example `LT2_HTTP_PORT=8090`, in your shell or `deploy/.env`.
- Every email lands in Mailpit: <http://localhost:8025>.
- Trial invitation code: `BANDYMAS-2026` (created by the migrator from `deploy/.env`).

Register with any address and the invitation code, open the sign-in email in Mailpit, enter the six-digit code, and the
"Sveiki" page appears. From there you can add a passkey and see or end your sessions.

To give a registered account the administrator role (needed for `/api/v1/admin/…`). A staff role works only in a session
signed in with the account's staff passkey (design 6.2), so the command prints a one-time activation code on the console
(valid 24 hours, never logged or emailed):

```bash
docker compose -f deploy/compose.yaml run --rm migrator --grant-role administrator ruta.mockute@pavyzdys.test
```

The person then adds a passkey on the "Sveiki" page, signs in with it, and enters the activation code there: that
passkey becomes the staff passkey and any other passkey on the account is removed. Use `run --rm` so the container, and
the console output it keeps, is removed afterwards. Later grants and resets go through `POST /api/v1/admin/roles` and
`POST /api/v1/admin/accounts/reset`, which need a passkey confirmation from the last ten minutes
(`POST /api/v1/me/passkey-confirmation`).

Stop with `docker compose -f deploy/compose.yaml down` (add `-v` to delete the database).

Other useful addresses: `/openapi/v1.json` (API description), `/health/live`, `/health/ready`.

## Build and test

Needs the .NET SDK pinned in `global.json` (10.0.402). Warnings are errors.

```bash
dotnet build
```

```bash
dotnet test
```

`dotnet test` runs the unit and architecture tests, and the integration tests, which start PostgreSQL 17 in a container
with Testcontainers (Docker must be running). Tests run on Microsoft.Testing.Platform (opted in by `global.json`), so a
single project is chosen with `dotnet test --project tests/Lt2.UnitTests`.

The end-to-end tests are skipped unless pointed at a running stack. Once, install Playwright's Chromium:

```bash
dotnet build tests/Lt2.E2E
```

```bash
pwsh tests/Lt2.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then, with the stack up, set these environment variables in your shell and run the project:

| Variable | Local value |
|---|---|
| `LT2_E2E_BASE_URL` | `https://lt2.localhost:8443` |
| `LT2_E2E_MAILPIT_URL` | `http://localhost:8025` (the default) |
| `LT2_E2E_INVITATION` | `BANDYMAS-2026` (the default) |
| `LT2_E2E_BROWSER_CHANNEL` | optional: `msedge` to use an installed Edge instead of Playwright's Chromium |
| `LT2_E2E_HTTP_URL` | optional: `http://lt2.localhost:8080`, to check the redirect to HTTPS |
| `LT2_E2E_COMPOSE_FILE` | optional: the compose file whose Caddy logs are checked (default `deploy/compose.yaml`) |

Besides the browser run, `StackTests` checks the security headers and cookies through Caddy and that Caddy's logs hold
no client address, header or token (it reads `docker compose logs caddy`, so the docker CLI must be on the path).

```bash
dotnet test --project tests/Lt2.E2E
```

## Configuration and secrets

Settings come from `appsettings.json`, environment variables and, in containers, one file per secret under
`/run/secrets` (for example `Database__Password`). Connection strings never contain passwords; the password is a
separate secret. Nothing secret is committed; `deploy/secrets/README.md` lists every secret.

| Setting | Meaning |
|---|---|
| `ConnectionStrings:Lt2` + `Database:Password` | Database connection for the process's own role (`lt2_app`, `lt2_worker` or `lt2_migrator`) |
| `DataProtection:KeyEncryptionKey` | Base64 32-byte key wrapping the data-protection keys stored in PostgreSQL; required outside development |
| `Security:IpHashKey` | Key for hashing client IP addresses in the audit log |
| `Email:Smtp:*` | SMTP host, port, security, user, password, from address (Mailpit locally; the provider is not chosen yet) |
| `ReverseProxy:TrustForwardedHeaders` | Trust `X-Forwarded-*` from Caddy (the web container is reachable only through it) |
| `RateLimits:*` | Per-IP limits for code requests, registration and sign-in attempts |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Turns on the OpenTelemetry exporter (traces, metrics, logs); off when empty |

Runtime settings that an administrator may change without a deploy (design 5.6) live in `config.setting`; every change
is written to `audit.event`.

For a production-shaped run, override `deploy/.env`: `LT2_SITE_ADDRESS` (the domain), `LT2_TLS` (the ACME contact
address), `LT2_HTTP_PORT=80`, `LT2_HTTPS_PORT=443`, `COMPOSE_PROFILES=` (no Mailpit), the `LT2_SMTP_*` values and
`LT2_MIGRATOR_ARGS=` (no seeded invitation), and mount real secrets.

## Conventions

- Pull requests into `master` are merged with merge commits, not squashed.
- Every user-facing string goes through `IStringLocalizer` (`*.resx` next to a marker type); Lithuanian only.
- Modules expose a small public interface and keep everything else `internal`; they never read each other's tables.
  `tests/Lt2.ArchitectureTests` enforces this.
- Domain rules are pure functions with unit tests whose names read as the rule.
- The site requests nothing from other hosts. Fonts (Sofia Sans, Sofia Sans Extra Condensed, Brygada 1918, SIL Open
  Font License) are served from `src/Lt2.Web/wwwroot/fonts`; the licence texts must stay next to the font files, and
  the content security policy allows fonts only from the site itself (`font-src 'self'`).
