# AGENTS.md – Lietuva 2.0 (LT2)

Working notes for anyone (human or AI agent) changing this repository. `CLAUDE.md` only imports this file; edit this
file, not that one. Keep it short: it is read at the start of every session. Detail belongs in the files below, read
when the task needs them.

## Read on demand

| When you… | Read |
|---|---|
| change behaviour, a rule, a screen or wording | the design: [design/README.md](design/README.md) maps each section ("8.12", "6.2") to its file; read only the sections the change touches |
| need to find where something is | [agents/code-map.md](agents/code-map.md) |
| change a feature | its part of [agents/features.md](agents/features.md): class, route and test names, owner's decisions as built |
| change a page, component or island | [agents/interface.md](agents/interface.md) |
| touch sign-in, sessions, cookies, codes, tokens or personal data | [agents/security.md](agents/security.md) |
| write or change a test | [agents/testing.md](agents/testing.md) |
| touch the Seimas client or import | [agents/seimas-data.md](agents/seimas-data.md) |
| decide whether something is settled | [design/12-open.md](design/12-open.md) (open) and [design/12-decisions.md](design/12-decisions.md) |

When the code changes what one of these files says, correct it in the same change.

## What this is

Lietuva 2.0 (LT2) is a Seimas (Lithuanian parliament) engagement platform run by VšĮ LT2.0. Build order (design 11):
M0 foundation · M1 MP profiles from Seimas open data · M2a appeals without sending · M2a+ reasons to come (agenda, front
page, following, AI draft) · M2a++ one object (every active bill open, one page per bill) · M2d profiles · email
rhythm · M2c comments · M2b sending to MPs · Stage 3 (voting with delegation; not designed, do not build towards it).
Build one stage at a time; do not start a later stage's features because they look close.

## Nothing is launched yet

There is no production and there are no users (owner, 2026-11-16); the local stack and `test.lietuva2.lt` hold
invented data. Until the owner removes this section: no data is worth keeping (migrations may drop anything, no
backfill), no existing members to protect (no grandfathering, re-consent or notice), no backward compatibility (old
links, fields and stored values need not work), and no arguing from launch-time costs. The design's rules (privacy,
security, wording, tests) still hold as written, and nothing is pushed or published without the owner asking.

## The design is the source of truth

`design/` is the agreed design, one file per section. If the code and the design disagree, say so; do not silently pick
one. A rule changes by discussion with the owner first, then the design, then the code. Do not decide an open item
(`design/12-open.md`) in code. Where the design's section 4 differs from the code, `agents/code-map.md` describes the
code and the design the intent.

## Branches and the owner

- `master`: product code, changed only through branches with **merge commits, not squash**. `docs`: the concept website
  and discovery notes (where the design says `../docs/` or `design-notes.md`). `legacy`: the 2011–2020 platform; never modify.
- Never push, open a pull request or publish anything without the owner asking.
- Discuss complex mechanics in chat first. The owner's suggestions are proposals: weigh them, say where you disagree.
  State each thing once, most important first, in plain language. Don't present undecided things as settled. Report
  what was verified and what was not; a test that did not run is not a pass.

## Stack

.NET 10, C# 14 · PostgreSQL, EF Core with Npgsql (snake_case, one `DbContext` and schema per module) · Minimal APIs under
`/api/v1` · Blazor Web App (static SSR for public pages, Interactive WebAssembly for signed-in features) · ASP.NET Core
Identity without passwords · Hangfire plus our own outbox · FluentValidation · Serilog, OpenTelemetry · xUnit,
Testcontainers, Playwright · Docker Compose behind Caddy on one EU VPS. Not used, on purpose: MediatR, MassTransit,
AutoMapper, Redis, a UI component library, any JavaScript framework.

## Commands

On Windows run every command in PowerShell, not Bash (Git Bash has broken mid-session); write multi-line scripts to a file
and run it. Commands must run unchanged on Linux too; scripts the build depends on are C# or Python.

```sh
dotnet build                                          # warnings are errors
dotnet test                                           # unit, architecture, integration (needs Docker)
dotnet test --project tests/Lt2.UnitTests             # one project (Microsoft.Testing.Platform)
python tools/run_tests.py e2e --order random          # a project in another order: default, forward, reverse, random[:SEED]
python deploy/init-secrets.py                         # once, and again when a secret is added
docker compose -f deploy/compose.yaml up --build      # full stack: https://lt2.localhost:8443, Mailpit :8025
```

If Windows has reserved port 8080, set `LT2_HTTP_PORT=8090` for every compose command. End-to-end tests run only with
`LT2_E2E_BASE_URL` set (`tools/run_tests.py e2e` sets it; `README.md` lists the variables).

**The end-to-end suite is not a merge gate (owner, 2026-11-18).** A change is checked by build, unit, architecture and
integration tests and a live look at the changed page on a running stack (Browser pane or screenshot), before merging
too. Run only the E2E classes that cover what changed, and only when it touches sign-in, codes, passkeys or sessions; how
an island starts (what a page does before WebAssembly is ready); or email flows through Mailpit. The full suite runs
before launch, or when the owner asks. An E2E test that keeps failing without finding a product fault (pixel layout
measurements, timing) is deleted, not patched again.

## Code rules

- **Modules.** Everything in a module is `internal` except its interface, DTOs and events. No module reads another
  module's tables; effects across modules go through outbox events in the same transaction.
- **The API is complete.** The UI uses only `/api/v1`, through client interfaces in `Lt2.Api.Contracts` (HTTP in the
  browser, the same handler in-process on the server). Every operation has an integration test over HTTP.
- **Public pages work without JavaScript** and are server-rendered; only signed-in features use WebAssembly. An action
  never reloads the page. UI conventions: `agents/interface.md`.
- **Counted rules are pure functions** with unit tests named as the rule (statistics, thresholds, eligibility, `GroupRules`).
- **Runtime settings** live in `config.setting`, every change audited; never hard-code a number the design calls a setting.
- **Imports are repeatable:** store every Seimas response raw, parse separately, upsert by Seimas identifiers.
- A feature is one folder: contract, validator, handler, endpoint, tests.
- Times are UTC in the database; display and schedules use `Europe/Vilnius` through `VilniusTime`. Use `TimeProvider`,
  never `DateTime.Now`.
- Rules are checked where the data is written (a module service, for every caller), not only by HTTP validators.
  Limits that count rows are counted under a PostgreSQL advisory lock in the same transaction.
- Every email goes through `EmailLayout` and one `EmailRenderer<T>` per outbox kind (details: `agents/features.md`).
- User-facing text goes through `IStringLocalizer`. User input is plain text, encoded on output; a typed text is shown
  through `TypedText`/`LongText` as paragraphs and lists (design 8.2, "A text's layout"), never Markdown; a new writing
  box for one carries `<TextLayoutHint>`.
- A new table goes on `DatabaseRoles.ReadableTables` or `WithheldTables` in the same change.

## Security and privacy (not negotiable in review)

A person's side on a bill is a political opinion: special-category data under GDPR. Details: `agents/security.md`.

- No endpoint, page, email, log line or admin screen reveals another person's position, administrators included. No
  per-person history of changed positions; trends come from daily aggregates.
- Logs carry no emails, positions, addresses or tokens; IPs are hashed. No real personal data in tests or fixtures; no
  secrets in the repository or images.
- Party group numbers only as `GroupRules` allows (`Digest:PartyGroupMinimum`).
- No passwords: an emailed code or a passkey. A staff role works only in a staff-passkey session; speaking for an MP
  needs a passkey session. Responses never reveal whether an email is registered.
- Session cookie only (`__Host-`, Secure, HttpOnly, SameSite=Lax), no tokens in the browser. Nothing non-essential in the
  browser, no third-party script, font, analytics or tracking (no cookie banner); a new cookie or storage key is a
  design change. No tracking in emails.
- Codes and tokens travel after the `#` of a link and in request bodies, never in addresses, except the one-click stop
  links mail programs send (MP mail, design 8.19.4; members' notices, 8.24.1): logged with the token replaced.
- Outbound requests go only to allowlisted hosts. Forwarding headers count only from `ReverseProxy:Address`. Pages for a
  signed-in session are `no-store`. CORS stays off; every write needs antiforgery and the Origin check, except those one-click stops.

## Wording and tone

- The interface is in **Lithuanian**; new copy is read by a native speaker before it is shown externally. Explanatory
  text follows design 8.15 (one short paragraph, the rest one click away); how: `agents/interface.md`.
- Nothing is worded as new or changed ("dabar", "nuo šiol", "nauja", "pasikeitė", "atnaujinta"): nobody has seen the site.
- Neutral-to-positive towards politicians: connection and explanation, not control; "skirtumas" over "atotrūkis";
  neutral chips, not warning colours.
- Numbers come from self-selected "Lietuva 2.0 naudotojai", never "the public" or a referendum; every place that shows
  them says they are not a representative poll. Planned Seimas dates are "Planuojama …", never a countdown.
- Dates as numbers are `yyyy-MM-dd` (`HH:mm`, Vilnius); a date in a form is a text field in that format, never the
  browser's date control.
- Terms: *kreipimasis* (appeal), *pozicija*, *argumentas* (reason), *atsakymas* (an MP's response), *suvestinė*
  (summary), "Man svarbu", *sritis*, *patikėtinis* (delegate, stage 3). Names are not declined: put an MP's name after a colon.
- Never promise that the service is free or ad-free, release dates, a public API, municipal councils or the European
  Parliament, or a licence. Never name the 2013 referendum initiative.

## Test and example data

All fictional and the same everywhere: bill **XVP-0412** "Gyventojų pajamų mokesčio įstatymo pataisa", for 6 800, against
4 280; MP **Jonas Petraitis** (Reformų partija), "prieš" on it; MP **Ona Jonaitienė** (Pilietinė sąjunga); expert **Rūta
Mockutė**. Recorded Seimas responses in fixtures are real public data; everything about users is invented. Addresses end
in `@pavyzdys.test`, hosts are under `.test`; only rules about public mail services name real providers.
