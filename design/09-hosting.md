# 9. Hosting and operations

## 9.1 Layout

One EU VPS running Docker Compose (`deploy/compose.yaml`, project `lt2`). At least 2 vCPU and 8 GB RAM; 4 vCPU and
16 GB is comfortable. The data-centre location is inside the EU.

| Container | Role |
|---|---|
| `caddy` | The published HTTP and HTTPS ports (HTTP/3 too), certificates (ACME, or Caddy's own CA locally), compression, the 1 MB request limit, no `Server` header; forwards to `web`. Its logs drop client addresses and headers and replace invitation codes and the tokens of MPs' and members' mail links. |
| `web` | API and Blazor host, with the Hangfire dashboard. One instance. |
| `worker` | Hangfire server: imports, agenda watcher, summaries, follow emails, outbox. One instance. |
| `migrator` | Creates roles, applies migrations and grants, then exits; `web` and `worker` start after it succeeds. |
| `postgres` | PostgreSQL 17, reachable only on the internal network. |
| `mailpit` | Profiles `local` and `mailpit`: catches every email. |
| `seimas-standin` | Profile `local` only: the stand-in for the Seimas open data, with invented answers; never built by `deploy/Dockerfile`. |

The .NET containers run with a read-only file system, all capabilities dropped and `no-new-privileges`. Secrets are files
(`deploy/secrets/`, or `LT2_SECRETS_DIR`), made by `deploy/init-secrets.py`, mounted under `/run/secrets` and never
committed. Per-client request limits are the application's (6.4), not Caddy's.

**Not built.** Two `web` instances behind Caddy so a deploy does not drop requests; a `backup` container (pgBackRest:
continuous WAL archiving and scheduled full backups to EU object storage, encrypted); an `otel-collector` container;
host hardening (SSH by key only on a non-default port for a named deploy user, firewall closed except 80 and 443,
automatic security updates, CrowdSec or fail2ban, Docker with user namespaces); a CDN with DDoS protection in front of
Caddy. A single VPS is a single point of failure, acceptable only with rehearsed recovery: at most a few minutes of data
lost (WAL archiving) and back on a fresh VPS within a few hours from backups and the compose files.

## 9.2 Environments

`local`: Docker Compose with real Seimas open data and Mailpit (`deploy/.env`, profile `mailpit`), at
`https://lt2.localhost:8443`. The stand-in is opt-in for demonstrations and end-to-end tests
(`deploy/standin.env`, profile `local`, a separate Compose project). The trial below is a staging environment.
There is no production.

**The trial on the owner's VPS.** It deploys `master` as an isolated Compose project (`lt2`) on the owner's Hostinger
VPS, which also runs the owner's other sites:

- **Data and emails.** The parliament data is the real Seimas open data (owner, 2026-11-25); the members' activity on
  it is invented (`--demo-activity`, 8.26.5) and every page says so; emails are caught by Mailpit and never delivered.
  Registration needs an invitation code made for this server, not the one in `deploy/.env`. The request limits are the
  code's defaults.
- **Behind the server's proxy.** Ports 80 and 443 belong to the Caddy that already serves the other sites. LT2 does
  not start its own Caddy there: that Caddy gets one site block for LT2's hostname and reaches `web` over its Docker
  network (`deploy/compose.shared-proxy.yaml`, `deploy/shared-proxy.Caddyfile`). The block keeps what LT2's own
  Caddyfile does (compression, the 1 MB request limit, no `Server` header, the forwarded scheme), writes no access log
  for the site, and adds `X-Robots-Tag: noindex, nofollow` and a `/robots.txt` that disallows everything except the
  link-preview fetchers (Facebook and Messenger, X, LinkedIn, Slack, Telegram, WhatsApp, Discord; owner, 2026-11-26), since a
  trial must be neither indexed nor crawled (the robots file also keeps out crawlers that collect pages for purposes
  other than search, which `noindex` does not) while a shared link still shows its preview (8.22.5): those fetchers read
  only the page shared and index nothing.
  Forwarding headers count only from that Caddy (`LT2_PROXY_HOST`). Nothing else on the server is changed: no other
  site's block, container, volume, port or firewall rule.
- **Reading the caught emails.** Whoever reads Mailpit reads every sign-in code and can sign in to any account (a staff
  account only as an ordinary member, 6.2), so it is never open. Its web interface has its own hostname, served by the
  server's Caddy over HTTPS only and marked `noindex`, and Mailpit itself asks for a name and password (its own password
  file, `mailpit_ui_auth`, generated on the server). The password is in a root-only file on the server and in one copy
  that the owner keeps in their own private storage, to read the emails from another device. It is nowhere else: in no
  repository and on no developer's machine. Whoever changes the password replaces the owner's copy too. Its own
  hostname, not a path of the site: the viewer shows the HTML of emails and must not share the site's origin. No port
  is opened for it; the SMTP port stays on the internal network. Locally Mailpit stays without a password.
- **Delivery.** Images are built from the deployed commit, tagged with it (`LT2_IMAGE_TAG`), and copied to the server
  over SSH; nothing is pushed to a registry. They are built either on the developer's machine or by the `deploy-trial`
  GitHub Actions workflow, which only the owner approves (owner, 2026-11-28); its key can run nothing on the server but
  the deploy command (`deploy/trial-deploy.sh`), which also rolls back to the version before. The migrator runs once
  per deploy.
- **Not built there:** the host hardening, two `web` instances, backups, telemetry and alerts (9.4), real email.

**Not built.** A separate staging and production pair: staging on its own small VPS or isolated project, synthetic
data only, emails captured and never delivered, never receiving production data.

## 9.3 Delivery

GitHub Actions (`.github/workflows/ci.yml`) on every push to `master` and every pull request: restore, build (warnings
are errors), a NuGet audit of direct and transitive packages, unit, architecture and integration tests; then a second
job, only when the workflow is started by hand (the end-to-end suite is not a merge gate), starts the compose stack and
runs the end-to-end tests against it.

The migrator holds a PostgreSQL advisory lock from its first step to its last, so a second copy started at the same
time (a hand run during a deploy) waits for the first instead of changing roles and grants alongside it.

**Not built.** An image per commit in a container registry and a manual, approved deploy step: pull by digest, run
`Lt2.Migrator`, start the new `web` instances, check health, stop the old ones, with schema changes following
expand-then-contract so the old and new versions can run side by side.

## 9.4 Watching it

- Health endpoints: `/health/live` (liveness) and `/health/ready` (readiness).
- Telemetry: Serilog and OpenTelemetry traces, metrics and logs go over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set;
  health requests are not traced. Failed imports and summary runs are logged as errors.

**Not built.** An external uptime check; one telemetry backend with EU storage (a hosted free tier or a self-hosted
single-node stack) with dashboards (request errors, import freshness, outbox age, digest runs, email bounces,
registrations per day); alerts (import older than expected, agenda watcher failing, outbox stuck, bounce rate up, backup
missed, certificate near expiry, an unusual jump in registrations); runbooks in `deploy/runbooks/` (restore from backup,
rotate secrets, stop all outgoing email, respond to a data incident, including the 72-hour notification duty); a
restore drill before launch and every quarter.

## 9.5 Database connections

PostgreSQL allows a fixed number of connections; when the pools of all parts together ask for more, it refuses new ones
and every part fails at once. So the pools have a budget, set explicitly and checked by a test
(`tests/Lt2.ArchitectureTests/ConnectionBudgetTests.cs`):

| Who | Pool | Where it is set |
|---|---|---|
| PostgreSQL `max_connections` | 150, of which 3 are kept for superusers | `deploy/compose.yaml` (`command`) |
| `web`, per instance | 25; the budget allows four instances at once (two, plus two new ones during a deploy) = 100 | `src/Lt2.Web/appsettings.json` `Database:MaxPoolSize` |
| `worker` (modules, outbox, jobs) | 10 | `src/Lt2.Worker/appsettings.json` `Database:MaxPoolSize` |
| Hangfire inside the worker | 10 (its own pool) | `src/Lt2.Worker/appsettings.json` `Database:HangfireMaxPoolSize` |
| `migrator` | 5 | `src/Lt2.Migrator/appsettings.json` `Database:MaxPoolSize` |
| Left for psql, backups, the read-only role | 22 (the test requires at least 20) | |

A host that does not set a size gets 10, never Npgsql's default of 100. Each pool names its connections
(`lt2-web`, `lt2-worker`, `lt2-hangfire`, `lt2-migrator`) so `pg_stat_activity` shows who holds what. A burst larger
than a pool waits for a free connection (up to Npgsql's 15-second timeout) instead of opening more.

Static files (the WebAssembly runtime, styles, scripts, images) are answered straight after routing, before
authentication, so a signed-in browser's start (dozens of parallel downloads) never looks up the session in the
database. Pages and the API still check the session on every request, so an ended session stops working at once. A
server error is sent with `Cache-Control: no-store`, so no browser or proxy keeps it; Caddy caches nothing.

## 9.6 Email

The provider is **plain SMTP**, for now `smtp.hostinger.com` (owner, 2026-11-19), configured like any host (`LT2_SMTP_*`,
`LT2_NOTICES_SMTP_*`, `LT2_MP_SMTP_*`; README). There are **three mailboxes**, one per stream (8.19.3): `codes` (the common
account: sign-in codes and account security), `notices` (members' and the team's emails) and `mp` (MP mail), each with its own
sender address and password (secrets `smtp_password`, `notices_smtp_password`, `mp_smtp_password`). A stream with no account of
its own uses the common one, as on the local stack, where Mailpit takes all three.

The provider caps each mailbox, at about 1 000 (Business Starter) or 3 000 (Premium) emails in a rolling 24 hours, and asks
that bulk mail not go through it. So each stream has a **daily ceiling** that the owner sets a little under the cap
(`Email:CeilingCodesPerDay`, `Email:CeilingNoticesPerDay`, `Email:CeilingMpPerDay`, settings, 0 = none): at the ceiling its
messages wait in the outbox and the other streams go on. If the provider refuses for its own limit before the ceiling is
reached, the stream pauses for an hour and nobody's address is suppressed (8.19.6). Plain SMTP gives no reports of later
bounces or complaints (8.19.6, not built). The size of this service is a risk for a campaign (12, "Risks"): it suits the trial and
a first small launch, and sign-in codes move to a transactional provider before a public campaign.
