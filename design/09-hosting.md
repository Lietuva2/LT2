# 9. Hosting and operations

## 9.1 Layout

One EU VPS **[decided: the owner's Hostinger VPS]** running Docker Compose. At least 2 vCPU and 8 GB RAM; 4 vCPU and
16 GB is comfortable. Choose a data-centre location inside the EU (the concept promises EU storage).

| Container | Role |
|---|---|
| `caddy` | Ports 80 and 443, certificates, compression, per-IP limits, static assets. |
| `web` | API and Blazor host. Two instances behind Caddy so a deploy does not drop requests. |
| `worker` | Hangfire server: imports, agenda watcher, digests, outbox. One instance. |
| `postgres` | Database, reachable only on the internal network. |
| `backup` | pgBackRest: continuous WAL archiving and scheduled full backups to EU object storage, encrypted. |
| `otel-collector` | Receives telemetry and forwards it. |

Host: SSH by key only on a non-default port for a named deploy user, firewall closed except 80 and 443, automatic
security updates, CrowdSec or fail2ban, Docker with user namespaces.

A single VPS is a single point of failure. That is acceptable for this product if recovery is rehearsed: the target is to
lose at most a few minutes of data (WAL archiving) and to be back on a fresh VPS within a few hours from backups and the
compose files. A CDN with DDoS protection in front of Caddy is recommended before the first public campaign (section 12).

## 9.2 Environments

`local` (Docker Compose with a seeded database and recorded Seimas responses), `staging` (same compose on a separate small
VPS or an isolated project, synthetic data only, emails captured and never delivered), `production`. Staging never
receives production data.

**The trial on the owner's VPS [decided by the owner on 2026-11-14: deploy `master` there, leaving everything else on
the server as it is].** It is a staging environment, an isolated Compose project (`lt2`) on the Hostinger VPS, which
also runs the owner's other sites:

- **Data and emails.** The parliament data is the stand-in's, invented, and every page says so; the demonstration
  record and organisations are seeded; emails are caught by Mailpit and never delivered. Registration needs an invitation code made for this
  server, not the one in `deploy/.env`. The request limits are the code's defaults.
- **Behind the server's proxy.** Ports 80 and 443 belong to the Caddy that already serves the other sites. LT2 does
  not start its own Caddy there: that Caddy gets one site block for LT2's hostname and reaches `web` over its Docker
  network (`deploy/compose.shared-proxy.yaml`, `deploy/shared-proxy.Caddyfile`). The block keeps what LT2's own
  Caddyfile does (compression, the 1 MB request limit, no `Server` header, the forwarded scheme), writes no access log
  for the site, and adds `X-Robots-Tag: noindex, nofollow`, since a trial with invented data must not be indexed.
  Nothing else on the server is changed: no other site's block, container, volume, port or firewall rule.
- **Reading the caught emails [decided by the owner on 2026-11-15].** Whoever reads Mailpit reads every sign-in code
  and can sign in to any account (a staff account only as an ordinary member, 6.2), so it is never open. Its web
  interface has its own hostname, served by the server's Caddy over HTTPS only and marked `noindex`, and Mailpit
  itself asks for a name and password (its own password file, generated on the server). The password is in a
  root-only file on the server and in one copy that the owner keeps in their own private storage, to read the emails
  from another device. It is nowhere else: in no repository and on no developer's machine. Whoever changes the
  password replaces the owner's copy too. Its own hostname, not a path of the site: the viewer shows the HTML of
  emails and must not share the site's origin. No port is opened for it; the SMTP port stays on the internal network.
  Locally Mailpit stays without a password.
- **Delivery.** Images are built from the deployed commit on the developer's machine and copied to the server over
  SSH; nothing is pushed to a registry or to GitHub. The migrator runs once per deploy, as in 9.3.
- **Not there yet, owed before production:** the host hardening of 9.1 (a named deploy user, a non-default SSH port,
  user namespaces), two `web` instances, backups, telemetry and alerts (9.4), real email, the real Seimas data.

## 9.3 Delivery

GitHub Actions: build, unit and integration tests, architecture tests, scans, then an image per commit in the container
registry. Deploy is a manual, approved step: pull by digest, run `Lt2.Migrator`, start the new `web` instances, check
health, stop the old ones. Schema changes follow expand-then-contract so the old and new versions can run side by side.
The migrator holds a PostgreSQL advisory lock from its first step to its last, so a second copy started at the same
time (a hand run during a deploy) waits for the first instead of changing roles and grants alongside it.

## 9.4 Watching it

- Health endpoints for liveness and readiness; an external uptime check.
- Telemetry over OTLP to one backend with EU storage (a hosted free tier or a self-hosted single-node stack; decide with
  the budget). Dashboards: request errors, import freshness, outbox age, digest runs, email bounces, registrations per day.
- Alerts: import older than expected, agenda watcher failing, outbox stuck, bounce rate up, backup missed, certificate
  near expiry, an unusual jump in registrations.
- Runbooks in `deploy/runbooks/`: restore from backup, rotate secrets, stop all outgoing email, respond to a data
  incident (including the 72-hour notification duty).
- A restore drill before launch and every quarter.

## 9.5 Database connections

PostgreSQL allows a fixed number of connections; when the pools of all parts together ask for more, it refuses new ones
and every part fails at once. So the pools have a budget, set explicitly and checked by a test
(`tests/Lt2.ArchitectureTests/ConnectionBudgetTests.cs`):

| Who | Pool | Where it is set |
|---|---|---|
| PostgreSQL `max_connections` | 150, of which 3 are kept for superusers | `deploy/compose.yaml` (`command`) |
| `web`, per instance | 25; up to four instances at once (two, plus two new ones during a deploy) = 100 | `src/Lt2.Web/appsettings.json` `Database:MaxPoolSize` |
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

