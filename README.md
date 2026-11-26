# Lietuva 2.0

Code for the Lietuva 2.0 (LT2) Seimas engagement platform. This branch (`master`) holds the product code. **The
system design this code follows is [`design/`](design/README.md).** Rules for changing the code are in `AGENTS.md`. The concept website stays on the
`docs` branch.

The user interface is in Lithuanian only. All names and data in examples and tests are fictional.

## What is here

A modular monolith on .NET 10 (design sections 2–4):

| Path | What |
|---|---|
| `src/Lt2.Web` | ASP.NET Core host: `/api/v1` minimal-API endpoints (one per file), Blazor server rendering, security plumbing |
| `src/Lt2.Web.Client` | Blazor WebAssembly: the islands of the signed-in features, sign-in, registration and the account screens; HTTP implementations of the API clients |
| `src/Lt2.Api.Contracts` | DTOs, routes, typed client interfaces, FluentValidation validators shared by browser and server |
| `src/Lt2.SharedKernel` | Result type, identifiers, outbox, audit and settings abstractions, the outbound allowlist |
| `src/Lt2.Hosting` | What the web host and the worker set up the same way: Serilog and OpenTelemetry (`AddLt2Observability`) |
| `src/Modules/Lt2.Identity` | Accounts, registration by invitation, emailed sign-in codes, passkeys, sessions — schema `identity` |
| `src/Modules/Lt2.Notifications` | Transactional outbox, dispatcher, SMTP transport, the sign-in code email — schema `notify` |
| `src/Modules/Lt2.Audit` | Append-only audit log and runtime settings — schemas `audit`, `config` |
| `src/Modules/Lt2.Parliament` | Seimas open-data client, raw-response store, parsers, import, statistics — schema `parliament` |
| `src/Modules/Lt2.Appeals` | Bills' records, positions, arguments, reports and moderation, descriptions, organisations and their statements, MPs' responses, comments — schema `appeals` |
| `src/Modules/Lt2.Usage` | The site's own usage statistics — schema `usage` |
| `src/Lt2.Jobs` | The Hangfire job classes the worker runs |
| `src/Lt2.Worker` | Hangfire server (PostgreSQL storage): the outbox dispatch loop and the parliament import (every 15 minutes on sitting days, hourly otherwise; by hand, whatever the schedule, the manual jobs `parliament-members`, `parliament-sittings`, `parliament-votes` and `parliament-everything`); its job classes are in `src/Lt2.Jobs`, and administrators see and trigger them at `/administravimas/darbai` |
| `src/Lt2.Migrator` | Creates the database roles, applies every module's migrations, grants access; run once per deploy |
| `tests/` | Unit, integration (Testcontainers), architecture (NetArchTest) and end-to-end (Playwright) tests |
| `deploy/` | Dockerfile (with `.dockerignore` at the root), `compose.yaml`, `Caddyfile`, secret generators; `compose.shared-proxy.yaml` and `shared-proxy.Caddyfile` for a server whose Caddy already holds ports 80 and 443 |
| `tools/Lt2.SeimasStandIn` | Local and test only: a stand-in for the Seimas open-data service that answers with **invented** data |

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
- Parliament data comes from `https://apps.lrs.lt/sip/`. The first import walks the term's sittings and votes at
  about one request per second. The separate upcoming-agenda watcher needs its own configured source.
- Use a clean database for the live stack. If the existing local database holds the stand-in's data, stop the
  worker and reset the invented parliament data before starting it against the live feed. The reset refuses a
  database that has imported real data or has an import running:

```bash
docker compose -f deploy/compose.yaml stop worker
docker compose -f deploy/compose.yaml run --rm --no-deps migrator --reset-parliament
docker compose -f deploy/compose.yaml up --build --remove-orphans
```

For the demonstration or end-to-end tests, start the stand-in stack separately. This command uses the same ports
as the live stack, so run one at a time unless you choose different ports:

```bash
docker compose --env-file deploy/.env --env-file deploy/standin.env -p lt2-standin -f deploy/compose.yaml up --build
```

The demonstration record on XVP-0412 (6 800 invented accounts asking to vote for, 4 280 against, the prototype's
arguments) is written by the migrator on the local stack and the trial only: it refuses unless `LT2_DEMO_DATA=true` (set
in `deploy/standin.env`) and the parliament data is the stand-in's. Running it again changes nothing.

```bash
docker compose --env-file deploy/.env --env-file deploy/standin.env -p lt2-standin -f deploy/compose.yaml run --rm --no-deps migrator --demo-appeal
```

The same command gives the stand-in overlay's invented closed bills (XVP-0707 and thirty more, XVP-0701 to XVP-0731,
design 8.26.5) invented positions from those invented accounts and closes their records, so `/statistika` has something
to compare; a bill whose vote the worker has not imported yet is reported "not imported yet" and filled by a later run.
The bills' votes come from `tools/Lt2.SeimasStandIn/Recent`, the stand-in's local overlay, written by
`python tools/standin_overlay.py` (fixed seed; `--check` says whether the files are current). The overlay also gives the
local stack and the trial a parliament of the real Seimas's shape: 141 invented MPs in seven invented groups of the real
sizes, the last four and the next four sitting days with about as many bills as real ones, and their votes.

To see "Kas Seime balsuoja taip, kaip prašote jūs" for your own account, give that one account random sides on those
closed bills. It refuses unless `LT2_DEMO_DATA=true` and the parliament data is the stand-in's, refuses an address with no
completed registration, an account without the consent to store positions and an MP's own account, touches no other
account, and never logs the address (matched as sign-in matches it). `-` reads the address from standard input, so it
stays out of the command line; running it again changes nothing:

```bash
printf '%s\n' 'vardas@pavyzdys.test' | docker compose -f deploy/compose.yaml run --rm -T --no-deps migrator --demo-sides-for -
```

To see how every email looks, the migrator can queue one of every outbox kind (and of each variant a kind has: every account
notice, every moderation outcome, the three summary copies, the gathered email in each rhythm) with invented data and
addresses `NAME@pavyzdys.test`, so Mailpit lists them by name and the worker renders them through the real renderers. It
writes outbox rows only (no account, no delivery row), refuses unless `LT2_DEMO_DATA=true` **and** `LT2_MAIL_CAUGHT=true`
(every email of the stack lands in Mailpit; both are set in `deploy/.env`, and the migrator now receives the second), and
sends everything again each time it runs. A unit test fails until each kind of the stream table has a demonstration;
`dotnet test --project tests/Lt2.UnitTests --filter-class "*DemoEmailTests"` also writes them to `output/email-samples/demo/`
without a stack. How to look at them: `agents/testing.md`.

```bash
docker compose -f deploy/compose.yaml run --rm --no-deps migrator --demo-emails
```

Planned dates come from the lrs.lt upcoming-agenda pages, not the open data. Locally the worker reads the stand-in's
INVENTED page (`LT2_AGENDA_BASE_ADDRESS` in `deploy/.env`); production sets the real address and adds its host to the
outbound allowlist; empty means off. The parser's assumptions are in `tests/Lt2.Parliament.ImportTests/Agenda/ASSUMPTIONS.md`;
`LT2_LRS_LIVE=1` with `LT2_LRS_AGENDA_URL` runs the live check. Summaries to MPs are published at `/suvestines` and are
dry runs unless the runtime setting `Digest:Sending` says otherwise and an introduction letter has been sent (design
8.19). Locally every email lands in Mailpit, so `deploy/.env` sets `LT2_MAIL_CAUGHT=true`: MP mail is locked wherever the
data is invented (`LT2_DEMO_DATA`, or any synthetic parliament data) unless the mail is declared caught.
`LT2_MODERATION_CONTACT` is the address shown to visitors who want to report (a local
placeholder in `deploy/.env`). Feedback (design 8.10): `LT2_FEEDBACK_ISSUES_URL` (`Feedback:IssuesUrl`, the
repository's GitHub issues, linked from the feedback panel; nothing shown when empty) and `LT2_FEEDBACK_TEAM_ADDRESS`
(`Feedback:TeamAddress`, where the worker sends at most one notice a day of new feedback; none when empty). `LT2_SECURITY_CONTACT` (`Site:SecurityContact`, a `mailto:` or https address for security reports, design 6.7) makes the site serve `/.well-known/security.txt` (RFC 9116: Contact, Expires 180 days ahead, Preferred-Languages, Canonical); empty (the default, also locally), the file answers 404.

Register with any address and the invitation code, open the sign-in email in Mailpit, enter the six-digit code, and the
account page (`/paskyra`) appears. From there you can add a passkey and see or end your sessions.

`deploy/.env` raises the per-address request limits (`LT2_RATE_*`) for local testing. The invented-data stack also
raises the hourly and daily sign-in code limits through the migrator's `--local-code-limits` flag in `deploy/standin.env`.
Keep these overrides local. (The fixed local sign-in code described in design 6.2 is not built.)

To give a registered account the administrator role (needed for `/api/v1/admin/…`). A staff role works only in a session
signed in with the account's staff passkey (design 6.2), so the command prints a one-time activation code on the console
(valid 24 hours, never logged or emailed):

```bash
docker compose -f deploy/compose.yaml run --rm migrator --grant-role administrator ruta.mockute@pavyzdys.test
```

The person then adds a passkey on the account page ("Saugumas ir duomenys", `/paskyra/saugumas`), signs in with it, and enters the activation code on "Dalyvavimas" (`/paskyra/dalyvavimas`): that
passkey becomes the staff passkey and any other passkey on the account is removed. Use `run --rm` so the container, and
the console output it keeps, is removed afterwards. Later grants and resets go through `POST /api/v1/admin/roles` and
`POST /api/v1/admin/accounts/reset`, which need a passkey confirmation from the last ten minutes
(`POST /api/v1/me/passkey-confirmation`). Saving a runtime setting (`PUT /api/v1/admin/settings/{key}`) needs the same
confirmation; the settings page asks for the staff passkey itself when the last one is older (design 6.9). An account
with an activated staff role can be deleted only from a session signed in with its staff passkey.

Stop with `docker compose -f deploy/compose.yaml down` (add `-v` to delete the database).

Other useful addresses: `/openapi/v1.json` (API description), `/health/live`, `/health/ready`.

## Build and test

Needs exactly the .NET SDK pinned in `global.json` (10.0.402; a newer one is not used, because the lock files depend on
the SDK). Warnings are errors.

```bash
dotnet build
```

```bash
dotnet test
```

`dotnet test` runs the unit and architecture tests, and the integration tests, which start PostgreSQL 17 in a container
with Testcontainers (Docker must be running). Tests run on Microsoft.Testing.Platform (opted in by `global.json`), so a
single project is chosen with `dotnet test --project tests/Lt2.UnitTests`.

`tests/Lt2.Parliament.ImportTests` also holds the **live contract suite** for the Seimas open data, skipped unless
`LT2_SEIMAS_LIVE=1` is set. It saves every response it fetches
into `tests/Lt2.Parliament.ImportTests/Seimas/Recorded/` and reports on every line of the register of assumptions
(`Seimas/ASSUMPTIONS.md`) in `Recorded/contract-report.md`, failing on each assumption that was wrong:

```bash
LT2_SEIMAS_LIVE=1 dotnet test --project tests/Lt2.Parliament.ImportTests
```

In PowerShell, set the variable first (`$env:LT2_SEIMAS_LIVE='1'`) and run the same `dotnet test` line without the
prefix.

The end-to-end tests are skipped unless pointed at a running stack. Once, install Playwright's Chromium:

```bash
dotnet build tests/Lt2.E2E
```

```bash
pwsh tests/Lt2.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then, with the stand-in stack up, set these environment variables in your shell and run the project:

| Variable | Local value |
|---|---|
| `LT2_E2E_BASE_URL` | `https://lt2.localhost:8443` |
| `LT2_E2E_MAILPIT_URL` | `http://localhost:8025` (the default) |
| `LT2_E2E_INVITATION` | optional: an existing invitation code; without it the run makes its own through the migrator and deletes it at the end |
| `LT2_E2E_BROWSER_CHANNEL` | optional: `msedge` to use an installed Edge instead of Playwright's Chromium |
| `LT2_E2E_HTTP_URL` | optional: `http://lt2.localhost:8080`, to check the redirect to HTTPS |
| `LT2_E2E_COMPOSE_FILE` | optional: the compose file the tests run `docker compose` against (default `deploy/compose.yaml`) |
| `COMPOSE_PROJECT_NAME` | `lt2-standin`, matching the stand-in stack's project name |
| `COMPOSE_ENV_FILES` | the absolute paths to `deploy/.env` and `deploy/standin.env`, separated by a comma |

`python tools/run_tests.py e2e` sets the local stack's addresses (any variable already set wins), builds, runs and prints
every failure's message; `--order reverse` or `--order random` runs the classes and tests in another order,
`--each-class` every class alone, `--repeat 2` twice in a row.

Every run registers new accounts with an invitation of its own (`E2ERun`: made by the migrator, deleted at the end), so
it never uses up `BANDYMAS-2026`.

`PasskeyTests` add, use and activate passkeys in the browser with Chromium's virtual WebAuthn authenticator (also with
`navigator.credentials` wrapped the way password managers do it); the staff test runs `docker compose run` for the
migrator. Besides the browser run, `StackTests` checks the security headers and cookies through Caddy and that Caddy's logs hold
no client address, header or token (it reads `docker compose logs caddy`, so the docker CLI must be on the path).

```bash
dotnet test --project tests/Lt2.E2E
```

The classes run one after another (`tests/Lt2.E2E/xunit.runner.json`): they share one stack and its demonstration
bills, and some hide, restore or count them. A whole run takes about 20 minutes; do not run it while the
integration suite runs on the same machine. The local `.env` runs the follow job every minute (`LT2_FOLLOWS_CRON`), so
the follow email `OrganisationInvitationFlowTests` waits for arrives within about a minute; production leaves the
variable unset (every 15 minutes).

Each test cleans up after itself, passed or failed (`E2ETest`): the accounts it registered are deleted through the
product, and what the product keeps on purpose (moderators' actions, closed organisations, unlinked feedback, a
description it wrote on a demonstration bill, a summary it made) is removed by SQL in the same step or in the test's
`finally`. The run needs no clean-up before or after and can be repeated at once; `tools/run_tests.py` prints
`tests/Lt2.E2E/leftovers.sql` before and after a run, and the two should read the same. When a test fails, the last
screens of its browsers are in `tests/Lt2.E2E/bin/Debug/net10.0/output/failures/`.

## Configuration and secrets

Settings come from `appsettings.json`, environment variables and, in containers, one file per secret under
`/run/secrets` (for example `Database__Password`). Connection strings never contain passwords; the password is a
separate secret. Nothing secret is committed; `deploy/secrets/README.md` lists every secret.

| Setting | Meaning |
|---|---|
| `ConnectionStrings:Lt2` + `Database:Password` | Database connection for the process's own role (`lt2_app`, `lt2_worker` or `lt2_migrator`) |
| `Database:MaxPoolSize`, `Database:HangfireMaxPoolSize`, `Database:ApplicationName` | The connection budget (design 9.5): web 25 per instance, worker 10, Hangfire 10, migrator 5, against PostgreSQL's `max_connections=150` in `deploy/compose.yaml`; 10 when unset. Change them only together with the budget test |
| `DataProtection:KeyEncryptionKey` | Base64 32-byte key wrapping the data-protection keys stored in PostgreSQL; required outside development |
| `Security:IpHashKey` | Key for hashing client IP addresses in the audit log |
| `Security:DeviceHashKey` | Web: key of the known browsers' hashes (design 8.20.2), the secret `device_hash_key`; without it no browser is known and the host logs a warning at start |
| `Security:BotCheckKey` | Web: key of the proof of work's signatures on registration and code requests (design 6.4), the secret `bot_check_key`; without it the host signs with a key of its own that holds on that instance only, and logs a warning at start |
| `Email:Smtp:*` | SMTP host, port, security, user, password, from address (Mailpit locally; the provider is not chosen yet) |
| `ReverseProxy:Address` | The proxy whose `X-Forwarded-For`/`-Proto` are believed (design 6.8): a host name resolved through Docker's DNS (again when the proxy's address changes) or an IP address; `caddy` in `compose.yaml`, `LT2_PROXY_HOST` with `compose.shared-proxy.yaml`; empty (the default) believes no forwarding header |
| `RateLimits:*` | Per-IP limits for code requests, registration and sign-in attempts |
| `Seimas:*` | Worker: open-data base address (`https://apps.lrs.lt/sip/`), term, retries, time-outs, request spacing |
| `Outbound:AllowedHosts` | Hosts any outbound HTTP request may go to (`apps.lrs.lt` in the worker; the AI provider's host joins when it is configured); others are refused, a redirect to them included |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Turns on the OpenTelemetry exporter (traces, metrics, logs); off when empty |

Runtime settings that an administrator may change without a deploy (design 5.6) live in `config.setting`; every change
is written to `audit.event`, and saving one needs a fresh staff passkey confirmation (design 6.9).

The read-only database role `lt2_readonly` (for analysis) reads exactly the tables on `DatabaseRoles.ReadableTables` in
`src/Lt2.Migrator/DatabaseRoles.cs` and nothing else; every other table is on `DatabaseRoles.WithheldTables`. Each run
of the migrator brings an existing database to that state. A new table is invisible to the role until it is listed,
and `tests/Lt2.ArchitectureTests/ReadonlyRoleTests.cs` fails until it is on one of the two lists (design 6.9).

For a production-shaped run, override `deploy/.env`: `LT2_SITE_ADDRESS` (the domain), `LT2_TLS` (the ACME contact
address), `LT2_HTTP_PORT=80`, `LT2_HTTPS_PORT=443`, `COMPOSE_PROFILES=` (no Mailpit), the `LT2_SMTP_*` values and
`LT2_MIGRATOR_ARGS=` (no seeded invitation), `LT2_DEMO_DATA=` (no demonstration data), `LT2_MODERATION_CONTACT` (the real address), `LT2_SECURITY_CONTACT` (the address for security reports: a `mailto:` or https address; the site serves `/.well-known/security.txt` only when it is set), `LT2_AGENDA_BASE_ADDRESS`
(the lrs.lt address, or empty), `LT2_SEIMAS_BASE_ADDRESS=` (the worker then imports from `https://apps.lrs.lt/sip/`), `LT2_SEIMAS_INTERVAL_MS=` (one
request a second),
`LT2_OUTBOUND_EXTRA_HOST` (the agenda page's host when `LT2_AGENDA_BASE_ADDRESS` is set, otherwise empty: then
`apps.lrs.lt` is the only host the worker may call; a redirect to another host is refused too), and mount real secrets.

The email provider is plain SMTP (design 9, 8.19.3): for now a Hostinger mailbox per stream (`smtp.hostinger.com`, port 587 with
`StartTls`, or 465 with `SslOnConnect`), configurable like any other host. Email goes on three streams, each with its own sender
address, optionally its own SMTP account and its own daily ceiling (runtime settings `Email:CeilingCodesPerDay`,
`Email:CeilingNoticesPerDay`, `Email:CeilingMpPerDay`, 0 = none, on the settings screen). `LT2_SMTP_*` and `LT2_MAIL_FROM` are the
common account and the sender of the **codes** stream (sign-in codes and every account-security email); the **notices** stream
(everything else to members and the team) adds:

| Variable | Meaning |
|---|---|
| `LT2_MAIL_FROM_NOTICES` | `Email:Notices:FromAddress`: the sender of members' and the team's emails, an address of the sending domain. |
| `LT2_NOTICES_SMTP_HOST`, `LT2_NOTICES_SMTP_PORT`, `LT2_NOTICES_SMTP_SECURITY`, `LT2_NOTICES_SMTP_USERNAME` | `Email:Notices:Smtp`: the notices' own SMTP account (port 587 and `StartTls` by default); with the host empty notices use the common account (`LT2_SMTP_*`). Its password is the secret `notices_smtp_password`. |

MP mail (design 8.19) adds:

| Variable | Meaning |
|---|---|
| `LT2_MAIL_CAUGHT` | `Email:Caught` for web and worker: `true` only where every email lands in a catcher (Mailpit). Default `false`. Where the data is invented (`LT2_DEMO_DATA`, or any synthetic parliament data) no MP mail is sent unless it is `true`. |
| `LT2_MAIL_FROM_MP` | `Email:Mp:FromAddress`: the sender of MP mail, an address of the sending domain. |
| `LT2_MP_SMTP_HOST`, `LT2_MP_SMTP_PORT`, `LT2_MP_SMTP_SECURITY`, `LT2_MP_SMTP_USERNAME` | `Email:Mp:Smtp`: MP mail's own SMTP account (port 587 and `StartTls` by default); with the host empty MP mail uses the common account (`LT2_SMTP_*`). Its password is the secret `mp_smtp_password`. |
| `LT2_RATE_MP_MAIL` | Requests per client address to an MP's link pages and token routes in 15 minutes (default 60); only the local `.env` raises it. |
| `LT2_RATE_BOT_CHALLENGES` | Proof-of-work challenges per client address in 15 minutes (design 6.4; default 60); only the local `.env` raises it. |

and three secrets, `mail_hash_key` (web and worker: the suppression list's keyed hashes, the MPs' link tokens and the members' stop links of design 8.24.1; changing
it breaks every link already emailed and forgets the suppressed addresses), `mp_smtp_password` (worker; empty while
MP mail uses the common account) and `notices_smtp_password` (worker; empty while notices use the common account). **On an
existing deployment, run `init-secrets.py` again (with the same directory, as root on Linux) before using the new compose
file:** it creates the missing files and keeps the others; without them compose refuses to start.

### Behind a server's existing Caddy

A server whose ports 80 and 443 already belong to another Compose project's Caddy (the trial on the owner's VPS, design
9.2) runs LT2 without its own Caddy. `deploy/compose.shared-proxy.yaml` keeps LT2's `caddy` from starting, publishes
nothing on 80 or 443, and joins `web` to that Caddy's Docker network (`LT2_PROXY_NETWORK`) under the alias `lt2-web`.
Forwarded client addresses are believed only from that Caddy, named by `LT2_PROXY_HOST` (design 6.8), not from the other
containers on the shared network. `deploy/shared-proxy.Caddyfile` is the site block to append to that Caddy's Caddyfile, with `lt2.example.org` replaced
by the site's hostname: compression, the 1 MB request limit, no `Server` header, no access log, `X-Robots-Tag:
noindex, nofollow` and a `/robots.txt` that disallows everything except the link-preview fetchers, so shared links
still show their preview (a trial only; drop both for production). A request that Caddy itself fails (a 502 while `web` restarts) still reaches that Caddy's error log
with the client's address and full address, an invitation code or an MP's mail link token in it included; only that
Caddy's global log options could drop them, and they belong to the other project. The application's own logs replace the
link token. Real hostnames, addresses and paths live only on the server, never in this repository.

On a server set up before MP mail (design 8.19), run `sudo python3 deploy/init-secrets.py $LT2_HOME/secrets` once more
before the first deploy of the new compose file: it adds `mail_hash_key` and `mp_smtp_password` (and, since the notices stream, `notices_smtp_password`) and keeps the rest. The same
again on a server set up before known browsers (design 8.20.2): it adds `device_hash_key` (web); and before the bot check (design 6.4): it adds `bot_check_key` (web).

What survives a redeploy lives outside the application tree, here `$LT2_HOME` (for example a directory under `/srv`,
owned by root):

| Path | What |
|---|---|
| `$LT2_HOME/app/` | The deployed commit's tree (`git archive`), replaced on every deploy |
| `$LT2_HOME/lt2.env` | The server's values, passed with `--env-file` instead of `deploy/.env` (mode 600) |
| `$LT2_HOME/secrets/` | `sudo python3 deploy/init-secrets.py $LT2_HOME/secrets`: run as root, it gives each file to the container user that reads it and nobody else |
| `$LT2_HOME/deployed-commit` | The commit that runs |
| `$LT2_HOME/mailpit-password` | The Mailpit viewer's password (mode 600; its hash is `secrets/mailpit_ui_auth`; the owner keeps the one other copy), see "The caught emails" below |

`lt2.env` for a trial with invented data (the request limits and sign-in code limits stay the code's defaults; never
`LT2_RATE_*`, `--local-code-limits` or `BANDYMAS-2026` on a server):

```sh
LT2_IMAGE_TAG=<deployed commit>
LT2_SECRETS_DIR=<LT2_HOME>/secrets
LT2_PROXY_NETWORK=<network of the server's Caddy>
LT2_PROXY_HOST=<the server Caddy's container name or alias on that network>
LT2_SITE_ADDRESS=lt2.example.org
LT2_HTTPS_PORT=443
COMPOSE_PROFILES=local
LT2_MAILPIT_PORT=8025
LT2_SMTP_HOST=mailpit
LT2_SMTP_PORT=1025
LT2_SMTP_SECURITY=None
LT2_MAIL_FROM=prisijungimas@lt2.example.org
LT2_MIGRATOR_ARGS=
LT2_SEIMAS_BASE_ADDRESS=http://seimas-standin:8080/sip/
LT2_OUTBOUND_EXTRA_HOST=seimas-standin
LT2_AGENDA_BASE_ADDRESS=http://seimas-standin:8080/
LT2_DEMO_DATA=true
LT2_MAIL_CAUGHT=true
LT2_MAIL_FROM_NOTICES=pranesimai@lt2.example.org
LT2_MAIL_FROM_MP=suvestines@lt2.example.org
LT2_MODERATION_CONTACT=moderatoriai@lt2.example.org
LT2_SECURITY_CONTACT=
LT2_FEEDBACK_ISSUES_URL=https://github.com/Lietuva2/LT2/issues
LT2_FEEDBACK_TEAM_ADDRESS=komanda@lt2.example.org
LT2_AI_PROVIDER=
```

With `LT2_HTTPS_PORT=443` the links in emails are `https://lt2.example.org/...` (`SiteLinks` drops the default port).
Every compose command on the server is, from `$LT2_HOME/app`:

```sh
docker compose --env-file $LT2_HOME/lt2.env -f deploy/compose.yaml -f deploy/compose.shared-proxy.yaml <command>
```

To deploy a commit of `master` (images are built on a developer's machine from a clean checkout of that commit and
copied over SSH; nothing goes to a registry):

1. On the developer's machine, with `C` the full commit hash: `git archive -o lt2-app.tar.gz C`, unpack it into an
   empty directory (a working tree would add its ignored `bin/` and `obj/` to the build context) and there run
   `LT2_IMAGE_TAG=C docker compose -f deploy/compose.yaml build web worker migrator seimas-standin`, then
   `docker save --platform linux/amd64 -o lt2-images.tar lt2/web:C lt2/worker:C lt2/migrator:C lt2/seimas-standin:C`;
   copy both files to the server with `scp`.
2. On the server: `docker load -i lt2-images.tar`; unpack `lt2-app.tar.gz` into a new directory and swap it with
   `$LT2_HOME/app` (keep the old one until the new one runs); write `C` to `$LT2_HOME/deployed-commit` and set
   `LT2_IMAGE_TAG=C` in `lt2.env`.
3. `run --rm --no-deps migrator` (the new migrations, while the old `web` still answers), then
   `up -d --no-build --no-deps web worker mailpit seimas-standin`: `web` is down only while it is replaced, which is
   when the server's Caddy logs 502s with visitors' addresses. (A plain `up -d --no-build` also works; it runs the
   migrator while `web` waits.) Check `/health/ready` through the site. The first deploy then appends the site block to
   the server Caddy's Caddyfile (append only: the file is often bind-mounted as a single file, so it must keep its
   inode), validates it with `caddy validate` and applies it with `caddy reload` (graceful; never restart that Caddy).
   Later deploys leave it alone.

Invitations and the demonstration data are made once with `run --rm migrator --seed-invitation CODE:USES` and, after
the worker's first import from the stand-in, `run --rm --no-deps migrator --demo-appeal`. A trial on the real Seimas data
(`LT2_SEIMAS_BASE_ADDRESS`, `LT2_OUTBOUND_EXTRA_HOST` and `LT2_AGENDA_BASE_ADDRESS` empty, `COMPOSE_PROFILES=mailpit`,
no stand-in) gets invented members' activity instead (design 8.26.5), with `LT2_DEMO_DATA=true` and `LT2_MAIL_CAUGHT=true`,
after the first import: `run --rm --no-deps migrator --demo-activity`.

**The caught emails** (design 9.2). Whoever reads Mailpit reads every sign-in code, so on a server Mailpit asks for a
name and password itself: `compose.shared-proxy.yaml` gives it the password file `mailpit_ui_auth` from the secrets
directory, and that holds on every route to it, the loopback port 8025 and the tunnel included. Without the override,
locally, Mailpit stays open (the end-to-end tests read its API). The password is in a root-only file on the server and in
one copy the owner keeps in their own private storage, to read the emails from another device; it is nowhere else, in
no repository and on no developer's machine (design 9.2). Make the file once on the server, as root (the hash comes
from a throwaway Caddy container; nothing is installed). The hash is taken first and the file written only when there
is one, so a failed `docker run` never leaves `lt2:` with an empty hash:

```sh
umask 077
python3 -c 'import secrets; print(secrets.token_urlsafe(24))' > $LT2_HOME/mailpit-password
if hash="$(docker run --rm -i --network none caddy:2 caddy hash-password < $LT2_HOME/mailpit-password)" && [ -n "$hash" ]; then
  printf 'lt2:%s\n' "$hash" > $LT2_HOME/secrets/mailpit_ui_auth && chmod 400 $LT2_HOME/secrets/mailpit_ui_auth
else
  echo "hashing failed: mailpit_ui_auth not written" >&2
fi
```

`caddy hash-password` reads one line from standard input and fails with `EOF` without its line end, so pass the file
as it is. Use a Caddy image already on the server if there is one.

The viewer gets its own hostname, never a path of the site, from the second block of `deploy/shared-proxy.Caddyfile`
(`mail.example.org` replaced), appended and reloaded like the first. The name is `lt2`; the owner takes the password
over SSH once (`cat $LT2_HOME/mailpit-password`) into their own private storage. To change it, write a new password
file and hash as above, run `up -d --no-build --no-deps --force-recreate mailpit` (Mailpit keeps no messages over a
restart), and replace the owner's copy with the new password.

## Conventions

- Pull requests into `master` are merged with merge commits, not squashed.
- Every user-facing string goes through `IStringLocalizer` (`*.resx` next to a marker type); Lithuanian only.
- Modules expose a small public interface and keep everything else `internal`; they never read each other's tables.
  `tests/Lt2.ArchitectureTests` enforces this.
- Domain rules are pure functions with unit tests whose names read as the rule.
- The site requests nothing from other hosts. Fonts (Sofia Sans, Sofia Sans Extra Condensed, Brygada 1918, SIL Open
  Font License) are served from `src/Lt2.Web/wwwroot/fonts`; the licence texts must stay next to the font files, and
  the content security policy allows fonts only from the site itself (`font-src 'self'`).
