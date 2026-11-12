# AGENTS.md – Lietuva 2.0 (LT2)

Working notes for anyone (human or AI agent) changing this repository. `CLAUDE.md` only imports this file; edit this
file, not that one.

## What this is

Lietuva 2.0 (LT2) is a Seimas (Lithuanian parliament) engagement platform run by VšĮ LT2.0. This branch holds the
product code. What is being built, in order:

| Stage | What |
|---|---|
| **M0 Foundation** | Solution skeleton, passwordless sign-in, security plumbing, deployment, monitoring. |
| **M1 MP profiles** | Every MP of the current term imported from Seimas open data: mandate, faction, committees, contacts, voting record, statistics. |
| **M2a Appeals without sending** | People take a side on a Seimas bill and give reasons; summaries for MPs are generated as previews only. |
| **M2b Sending** | Real summary emails to MPs, MP preferences, profile take-over, MP responses. |
| **Stage 3** | Voting with delegation, AI summaries, analytics. Not designed yet; do not build towards it beyond what the design says. |

Build one stage at a time. Do not start a later stage's features because they look close.

## The design is the source of truth

`design/system-design.md` is the agreed design: architecture, data model, security, every product rule and its reason.
Read it before changing behaviour. This is the maintained copy; the original on the `docs` branch
(`discovery/system-design.md`) is not updated any more.

- If the code and the design disagree, say so; do not silently pick one.
- A change to a rule is discussed with the owner first, then written into the design, then coded.
- Section 12 of the design lists what is decided and what is still open. Do not decide an open item in code.

Until M0 is merged, the layout and commands below describe what the design intends. Once code exists, correct this
file to what the code actually is.

## Branches

| Branch | Contents |
|---|---|
| `master` | Product code. Change it only through branches and pull requests, with **merge commits, not squash**. |
| `docs` | The concept website (`docs/`) and team notes (`discovery/`). Not needed for coding. Where the design mentions `../docs/` or `design-notes.md`, it means this branch. |
| `legacy` | The 2011–2020 ASP.NET MVC platform. Do not modify. |

Never push, open a pull request or publish anything without the owner asking.

## Stack

.NET 10 (LTS), C# 14 · PostgreSQL · EF Core with Npgsql, snake_case names, one `DbContext` and one schema per module ·
ASP.NET Core Minimal APIs under `/api/v1` · Blazor Web App (static server rendering for public pages, Interactive
WebAssembly for signed-in features) · ASP.NET Core Identity without passwords · Hangfire on PostgreSQL plus our own
outbox table · FluentValidation · Serilog and OpenTelemetry · xUnit, Testcontainers, Playwright · Docker Compose behind
Caddy on one EU VPS.

Not used, on purpose: MediatR, MassTransit, AutoMapper (commercial licences), Redis, a UI component library, any
JavaScript framework.

## Layout

```
design/                system-design.md, the agreed design
src/Lt2.Web            host: API endpoints (one per file), Blazor server rendering, auth, security middleware
src/Lt2.Web.Client     Blazor WebAssembly components and HTTP implementations of the API clients
src/Lt2.Api.Contracts  DTOs, routes, client interfaces, validators (shared by server, client and tests)
src/Lt2.SharedKernel   Result type, clock, identifiers, event base types
src/Modules/           Lt2.Identity, Lt2.Parliament, Lt2.Appeals, Lt2.Notifications, Lt2.Audit
src/Lt2.Worker         background jobs: imports, summaries, outbox dispatch
src/Lt2.Migrator       applies migrations and database grants; run once per deploy
tests/                 unit, integration (real PostgreSQL), import, architecture, end-to-end
deploy/                compose files, Caddyfile, backup, runbooks
```

```sh
dotnet build                                   # warnings are errors
dotnet test
docker compose -f deploy/compose.yaml up       # full stack locally, mail goes to Mailpit
```

## Code rules

- **Modules.** Everything in a module is `internal` except its interface, DTOs and events. No module reads another
  module's tables. Effects across modules go through events written to the outbox in the same transaction.
- **The API is complete.** The UI uses nothing but `/api/v1`. Components depend on client interfaces from
  `Lt2.Api.Contracts`; in the browser they call HTTP, on the server the same handler in-process. Every operation has an
  integration test over HTTP.
- **Public pages work without JavaScript** and are server-rendered. Only signed-in features use WebAssembly.
- **Counted rules are pure functions with unit tests** whose names read as the rule: statistics, thresholds, summary
  eligibility, the "at least 20" rule.
- **Runtime settings** live in `config.setting`. Every change is audited. Do not hard-code a number the design calls a
  setting.
- **Imports are repeatable.** Store every Seimas response raw, parse separately, upsert by Seimas identifiers.
- A feature is one folder: contract, validator, handler, endpoint, tests.
- Times are UTC in the database; schedules and display use `Europe/Vilnius`. Use `TimeProvider`, never `DateTime.Now`.
- User-facing text goes through `IStringLocalizer`. User input is plain text, encoded on output.

## Security and privacy rules

A person's side on a bill is a political opinion: special-category data under GDPR. These rules are not negotiable in
code review.

- No endpoint, page, email, log line or admin screen reveals another person's position. Administrators included.
- Do not store per-person history of changed positions. Trends come from daily aggregate counts.
- Logs carry no emails and no positions. IPs are hashed.
- Group numbers (for example supporters of a party) are shown only for groups of at least 20.
- Sign-in has no passwords: an emailed one-time code or a passkey. Staff and politicians need a second factor.
  Responses never reveal whether an email is registered.
- Session cookie only (`__Host-`, Secure, HttpOnly, SameSite=Lax). No tokens in the browser. Every state-changing
  call needs the antiforgery header and passes the Origin check. CORS stays off.
- No secrets in the repository or in images. No real personal data in tests or fixtures.
- No tracking pixels or click tracking in any email.
- Outbound requests go only to the allowlisted hosts.

## Product rules to keep consistent

**Accounts.** Registration is by email only, by invitation during the closed trial, with a self-declared age of 18 or
older. One account per normalised address. Each account has a `verification_level` (`email` now); phone verification is
a later decision.

**MP profiles.** Compiled automatically from Seimas open data for every MP, joined or not. Two statistics:
participation (share of recorded votes in which the MP voted for, against or abstained) and voted with the faction
(only votes where at least two thirds of the faction's voting members chose the same option; free votes are excluded
and their count shown). No expenses, rankings or scorecards. An MP takes over a profile through a link sent only to the
official `@lrs.lt` address from open data; the link is tied to the Seimas person id.

**Appeals.**
- One appeal per bill, always tied to a Seimas bill. Two sides: asks to vote for, asks to vote against.
- One position per person per bill, private, changeable until the appeal closes at the final Seimas vote.
- Reasons: plain text up to 500 characters, public with the author's display name, at most two per person per bill.
  "Man svarbu" endorses a reason on your own side, at most three per bill.
- The initiator opens the appeal with a side, a first reason, a neutral description and a threshold. A moderator
  publishes it.
- **Threshold:** per appeal, public, set by the initiator, default 100, never below the administrator's minimum. The
  initiator can change it until the appeal first enters a summary. A number changed by a moderator is final. There is
  no objection flow in the product and no automatic override before a vote.

**Summaries to MPs.**
- Go to every sitting MP with an official address, joined or not. Nobody chooses recipients.
- Weekly on Monday morning in sitting weeks, and the working day before a sitting with a relevant bill. At most two a
  week per MP.
- Always both sides, always with the count for accounts older than 30 days, with the most endorsed reasons a moderator
  has seen.
- Every summary has a public copy with the same numbers. Whether an MP stopped the summaries is never shown publicly.
- Timing is corrected by a moderator when people flag a wrong date: correct, hold, or approve one urgent summary.

**Moderation.** Anything that reaches MPs has passed a person. Every action is audited and the author gets the reason.
A report button is on every appeal and reason.

## Wording and tone

- The interface is in **Lithuanian**. New copy is read by a native speaker before it is shown externally.
- Neutral-to-positive towards politicians: connection and explanation, not control. Prefer "skirtumas" over
  "atotrūkis"; neutral chips, not warning colours, for differences.
- Numbers are a signal from self-selected "Lietuva 2.0 naudotojai", never "the public", "voters" or a referendum. Every
  place that shows them says they are not a representative poll.
- Planned Seimas dates are "Planuojama …", never a countdown: agenda items slip.
- Proposed terms: *kreipimasis* (appeal), *pozicija* (position), *priežastis* (reason), *atsakymas* (an MP's
  response), *suvestinė* (summary), *iniciatorius*. Existing terms: "Man svarbu", *sritis* for subject areas,
  *patikėtinis* = "delegate" in English (stage 3).
- Do not promise in any text: that the service is free or without ads, release dates, a public API, municipal councils
  or the European Parliament, a specific licence. Never name the 2013 referendum initiative.

## Test and example data

All fictional, and the same everywhere: bill **XVP-0412** "Gyventojų pajamų mokesčio įstatymo pataisa", asks to vote
for 6 800, against 4 280; MP **Jonas Petraitis** (Reformų partija), whose position on it is "prieš"; MP **Ona
Jonaitienė** (Pilietinė sąjunga); expert **Rūta Mockutė**. Recorded Seimas responses used as fixtures are real public
data; everything about users is invented.

## Seimas open data

- Base `https://apps.lrs.lt/sip/` (CC BY 4.0; show the attribution). Chain: `p2b.ad_seimo_kadencijos` →
  `p2b.ad_seimo_sesijos?kadencijos_id=` → `p2b.ad_seimo_posedziai?sesijos_id=` → `p2b.ad_sp_darbotvarke?posedzio_id=` →
  `p2b.ad_sp_klausimo_svarstymo_eiga` → `p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=`. MPs:
  `p2b.ad_seimo_nariai?kadencijos_id=` (`kadencijos_id=10` is 2024–2028; `asmens_id` is stable across terms).
- Votes approved by consensus have no per-MP votes: show them without numbers. Some results carry an official note that
  per-MP votes do not match the protocol totals: show it next to the result.
- Open data lists only sittings that have started. Upcoming agendas are on lrs.lt about 10–12 days ahead and change
  often.
- The service is slow at times and drops connections: retry, and never let an outage take the site down.
- Confirm any field you have not seen in a live response before relying on it.

## Working with the owner

- Discuss complex mechanics in chat first; change the design and the code only after agreement.
- The owner's suggestions are proposals: weigh them, say where you disagree and why, then apply.
- State each thing once, most important first, in plain language.
- Don't present undecided things as settled.
- Report what was verified and what was not. A test that did not run is not a pass.
