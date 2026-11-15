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
| **M2a+ Reasons to come** | The agenda page (`/darbotvarke`) and the front page built on it, following bills and MPs with emails, the AI draft of a description (Claude API, off until configured), the share line (design 8.11). Its asking for an appeal and appeals opened by moderators were replaced by M2a++. |
| **M2a++ One object** | Every active bill open at once, one page per bill, no proposals, requests or approval; lists and agenda on participation; moderation after the fact (design 8.12). Then descriptions with their labels and the AI summary job (8.13). |
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

M0 is merged: the layout and commands below describe the code as it is. When the code changes them, correct this file
in the same change. Where the design's section 4 differs (for example `deploy/backup`, runbooks or a staging compose
file, which do not exist yet), this file describes the code and the design describes the intent.

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
design/                    system-design.md, the agreed design; ui/ the interface prototype (tokens, sample pages)
src/Lt2.Web                host: API endpoints in Endpoints/<Module>/ (one per file), Blazor server rendering,
                           auth, security middleware (forwarding headers believed only from ReverseProxy:Address,
                           Security/TrustedProxy.cs; no-store on pages answered to a signed-in session)
src/Lt2.Hosting            what the web host and the worker set up the same way: Serilog and OpenTelemetry
                           (Observability.AddLt2Observability); no module references it
src/Lt2.Web.Client         Blazor WebAssembly components and HTTP implementations of the API clients; the interface's
                           shared pieces: Components/InteractiveBase<TText>, Display (numbers and dates, Vilnius),
                           StringLocalizerExtensions (Format), CopyLink, FieldError, Messages
src/Lt2.Api.Contracts      DTOs, routes, client interfaces, validators (shared by server, client and tests)
src/Lt2.SharedKernel       Result type, identifiers (UUID v7), outbox, audit and settings interfaces, connection
                           strings and the one EF Core set-up (Persistence/: UseLt2Npgsql), VilniusTime (the
                           Europe/Vilnius zone and its days), the signed-in account (ClaimsPrincipal.UserId()), the
                           staff actor (StaffActor) and staff statements (StaffStatements), the outbound allowlist
                           (Http/: hosts from Outbound:AllowedHosts plus AddOutboundHost; redirects followed only within
                           it); no clock of its own (TimeProvider is used)
src/Modules/               Lt2.Identity, Lt2.Notifications, Lt2.Audit, Lt2.Parliament (Seimas open-data client,
                           import, statistics); Lt2.Appeals (bills' records, positions, arguments, endorsements,
                           reports, moderation, rule-based pre-screening, flags, summaries content, descriptions written
                           by moderators, the AI draft in Ai/ with its public prompt file, the AI summary job
                           (Application/DescriptionSummaries.cs), the "unavailable" IBillTextSource, and organisations
                           with their statements on bills, design 8.14.14). Notifications
                           also owns the summaries (Digests/) and following (Follows/: follows, email switches, bill
                           events). Modules talk through interfaces in SharedKernel (IParticipants, IBillCatalogue,
                           IPlannedAgenda, IMpDirectory, IDigestSource, IPartySupporters, IAccountData (one per module),
                           IPositionErasure, IOutbox, IAuditLog, ISettings, IAiAssistant, IBillTextSource,
                           IOrganisationDirectory, IOrganisationCodeSwitch). The host
                           composes the agenda (GET /api/v1/agenda) and /me/follows from the modules' interfaces.
src/Lt2.Worker             Hangfire server: outbox dispatch loop, the parliament import, the agenda watcher (lrs.lt
                           pages; off unless Agenda:BaseAddress is set), the daily aggregates and closing of the bills' records, summaries
                           in dry-run (nothing is sent in M2a), the follow emails (every 15 minutes), the AI summaries
                           of bills (hourly; makes nothing until a text reader and a provider exist), a daily identity
                           clean-up (with the unsent mail of purged registrations) and a daily outbox clean-up (failed
                           messages after 30 days); the dispatcher sends sign-in and confirmation codes first, a batch
                           over one SMTP connection, and stores only an error's type and status code (design 6.8)
src/Lt2.Migrator           creates the database roles, applies migrations and grants; also seeds invitations, grants
                           the first staff role, empties invented parliament data and (local stack only) fills the
                           demonstration record on XVP-0412, three invented organisations with statements on it, and an
                           invented AI summary on XVP-0533 (--demo-appeal);
                           run once per deploy
tests/Lt2.UnitTests        rules and validators, plus bUnit tests of components (Components/)
tests/Lt2.IntegrationTests API over HTTP against a real PostgreSQL (Testcontainers)
tests/Lt2.ArchitectureTests module boundaries, static assets
tests/Lt2.E2E              Playwright and stack checks against a running compose stack; skipped without one
tests/Lt2.Parliament.ImportTests  Seimas client, parsers, register of assumptions (Seimas/ASSUMPTIONS.md), import against
                           the stand-in and PostgreSQL; live contract suite only with LT2_SEIMAS_LIVE=1
deploy/                    Dockerfile (restore layer first; .dockerignore at the root), compose.yaml, Caddyfile, .env (local defaults), init-secrets.py (optional target
                           directory; as root on Linux gives each secret to the container user reading it), secrets/;
                           compose.shared-proxy.yaml and shared-proxy.Caddyfile: behind a server's existing Caddy, with
                           the Mailpit viewer on its own hostname behind Mailpit's password file (design 9.2,
                           README.md; LT2_PROXY_HOST names that Caddy, the only sender whose forwarding headers count);
                           LT2_SECRETS_DIR and LT2_IMAGE_TAG move secrets and tag images
tools/Lt2.SeimasStandIn     local and test only: stand-in for the Seimas open data and the lrs.lt agenda page (/darbotvarke),
                           invented answers; never shipped. Recent/ (container only, never in tests) adds a sitting and
                           a stage vote dated two days before today, so the local agenda has recent votes
```

```sh
dotnet build                                          # warnings are errors
dotnet test                                           # unit, architecture, integration (needs Docker)
dotnet test --project tests/Lt2.UnitTests             # one project (Microsoft.Testing.Platform)
python deploy/init-secrets.py                         # once: local secrets in deploy/secrets/
docker compose -f deploy/compose.yaml up --build      # full stack: https://lt2.localhost:8443, Mailpit :8025
```

If Windows has reserved port 8080, set `LT2_HTTP_PORT=8090` for every compose command. End-to-end tests run only with
`LT2_E2E_BASE_URL` set; `README.md` lists their variables and the invitation to use.

## Running commands

- **On Windows, run every command in PowerShell, not Bash** (git, dotnet, docker, python). Bash there is Git Bash, an
  emulation layer that has broken mid-session on the owner's machine. Write multi-line scripts to a file and run the
  file; do not paste them inline.
- CI and cloud sessions run Linux. The commands in this file run unchanged in PowerShell and in a Linux shell; keep it
  that way, and keep scripts the build depends on in C# or Python, not in shell.

## Code rules

- **Modules.** Everything in a module is `internal` except its interface, DTOs and events. No module reads another
  module's tables. Effects across modules go through events written to the outbox in the same transaction.
- **The API is complete.** The UI uses nothing but `/api/v1`. Components depend on client interfaces from
  `Lt2.Api.Contracts`; in the browser they call HTTP, on the server the same handler in-process. Every operation has an
  integration test over HTTP.
- **Public pages work without JavaScript** and are server-rendered. Only signed-in features use WebAssembly.
- **Island controls stay disabled until the island is interactive** (`InteractiveBase<TText>.Locked`, or `!RendererInfo.IsInteractive`),
  fields included, and the page carries `<NeedsScript />`. `tests/Lt2.E2E/IslandsBeforeStartTests.cs` walks the pages with the
  WebAssembly runtime blocked; add a new island page there.
- **Counted rules are pure functions with unit tests** whose names read as the rule: statistics, thresholds, summary
  eligibility, the "at least 20" rule.
- **Runtime settings** live in `config.setting`. Every change is audited. Do not hard-code a number the design calls a
  setting.
- **Imports are repeatable.** Store every Seimas response raw, parse separately, upsert by Seimas identifiers.
- A feature is one folder: contract, validator, handler, endpoint, tests.
- Times are UTC in the database; schedules and display use `Europe/Vilnius`. Use `TimeProvider`, never `DateTime.Now`.
  Vilnius days come from `VilniusTime` in SharedKernel (`Zone`, `DateOf`, `Today`, `StartOf`); do not look the zone up again.
- The signed-in account is `principal.UserId()` (SharedKernel); a staff member acting is a `StaffActor`. A module's
  `DbContext` is configured with `UseLt2Npgsql` (SharedKernel/Persistence), nowhere by hand.
- Limits that count rows (codes per address, one current invitation code, one urgent summary a week, follows per
  account) are counted under a PostgreSQL advisory lock in the same transaction (design 6.8); Identity's are in
  `IdentityLocks`, Appeals' in `AdvisoryLocks`.
- Every email is built by `EmailLayout` (Notifications/Templates) and rendered from its outbox kind by one
  `EmailRenderer<T>` registered in `NotificationsModule`; shared texts use the `Email.*` keys of `EmailTemplates.resx`.
  Deleting an account or purging a pending one calls `IOutbox.ForgetAsync` in the same transaction.
- Rules are checked where the data is written (design 6.8), not only by the HTTP validators: a module service enforces
  them for every caller. A moderator's statement goes through `StatementRules` in Appeals and `StaffStatements` elsewhere
  (the same rule); a moderator's action endpoint runs through
  `ModeratorEndpoint.ActAsync` (`src/Lt2.Web/Endpoints/Moderation`).
- User-facing text goes through `IStringLocalizer`. User input is plain text, encoded on output.
- **One way to do each thing in the interface.** An interactive component inherits `InteractiveBase<TText>`
  (`Lt2.Web.Client/Components`; `TText` is its resource marker): `RunAsync` for every action (one at a time, failures
  become the message), `Succeeded`/`Show` for an answer, `Valid` for the shared validator, `IsSignedInAsync`, and
  `ErrorClass`/`Invalid`/`DescribedBy` with `<FieldError For="input-id">` for field errors. A text with placeholders is
  `L.Format(key, …)`; numbers and dates are `Display` (`Number`, `Day`, `DayTime`, `Today(TimeProvider)`), on the
  server pages too; the moderators' contact address is `Configuration.ModeratorContact()`. A link to copy is
  `<CopyLink>`; "Atkurti" is `<RestoreForm>`; a queued argument is `<QueueReasonItem>`, a queued statement
  `<StatementQueueItem>`; the bill page's islands share the caller's answers through `MyBillState`. A list whose items
  hold state or re-sort is keyed (`@key`), and an element id is unique on the page (a component shown twice takes an
  `IdPrefix`).

## Security and privacy rules

A person's side on a bill is a political opinion: special-category data under GDPR. These rules are not negotiable in
code review.

- No endpoint, page, email, log line or admin screen reveals another person's position. Administrators included.
- Do not store per-person history of changed positions. Trends come from daily aggregate counts.
- Logs carry no emails and no positions. IPs are hashed.
- Group numbers (for example supporters of a party) are shown only for groups of at least 20.
- Sign-in has no passwords: an emailed one-time code or a passkey. A staff role works only in a session signed in
  with a staff passkey; a code session is a member session, whatever roles the account has. Politicians need a
  passkey too. Responses never reveal whether an email is registered. Changing roles, resetting an account and saving
  a runtime setting need a fresh passkey confirmation (`FreshConfirmation`, the last ten minutes); a staff account is
  deleted only from its staff passkey session; what a staff member did keeps their account id after deletion (design 6.9).
- The read-only database role reads only the tables on `DatabaseRoles.ReadableTables`; a new table goes on that list
  or on `DatabaseRoles.WithheldTables` in the same change (an architecture test fails otherwise).
- Session cookie only (`__Host-`, Secure, HttpOnly, SameSite=Lax). No tokens in the browser. Every state-changing
  call needs the antiforgery header and passes the Origin check. CORS stays off.
- No secrets in the repository or in images. No real personal data in tests or fixtures.
- No tracking pixels or click tracking in any email.
- Outbound requests go only to the allowlisted hosts, and follow no redirect to another host.
- Codes and tokens are not sent in addresses: a link the site makes carries its secret after the `#` (`/prisijungti#kodas=…`,
  a representative's `/organizacijos/kvietimas#…`); the page reads it and sends it in a request body (design 6.8). Old
  `?kodas=` links still work.
- Forwarding headers count only from the proxy named in `ReverseProxy:Address`. A page answered to a signed-in session is
  `no-store`.

## Product rules to keep consistent

**Accounts.** Registration is by email only, by invitation during the closed trial, with a self-declared age of 18 or
older. An account is pending until its address is proven; the display name and the age declaration are asked after the
code, never before. One account per normalised address. Each account has a `verification_level` (`email` now); phone
verification is a later decision. Every member has one personal invitation code (replaceable; uses unlimited unless
`Registration:PersonalCodeUses` is set) and sees only how many registered with it; administrators see numbers per day,
code and level, never who invited whom, and can switch codes off. Invitations are never combined with sides.

**MP profiles.** Compiled automatically from Seimas open data for every MP, joined or not. Two statistics:
participation (share of recorded votes in which the MP voted for, against or abstained) and voted with the faction
(only votes where at least two thirds of the faction's voting members chose the same option; free votes are excluded
and their count shown). No expenses, rankings or scorecards. An MP takes over a profile through a link sent only to the
official `@lrs.lt` address from open data; the link is tied to the Seimas person id.

**Appeals (design 8.12, decided by the owner on 2026-11-14; replaces proposals, requests and initiators).**
- The bill and the appeal are one object: the bill's page `/seimas/{number}`. Every active bill (in the catalogue, no
  final vote) is open at once: anyone signed in takes a side, writes and marks arguments, follows, shares, reports.
  Nobody proposes, opens, asks for or approves an appeal. The old `/seimas/{n}/kreipimasis` addresses redirect.
- The Appeals module keeps one record per bill (`appeals.bill_record`: open, closed or hidden), created when first needed (the
  first side, argument or description, or a moderator's action) under the bill's lock and a unique index. It has no
  initiator, and nothing public says who took part first. Answers for an active bill without a record are the empty open
  state, not "not found".
- The code calls that row the bill's record (design 8.12, owner 2026-11-15): the entity `BillRecord`, columns `record_id`,
  error codes `record.*`, the stored kind `record`. "Appeal" in the singular names no object in the code; the module,
  its schema `appeals`, the `Appeals:` settings and the word *kreipimasis* stay.
- One position per person per bill, private, changeable until the bill closes at the final Seimas vote.
- Reasons: plain text up to 500 characters, public with the author's display name, for either side, whatever the
  author's own (no side needed; the writing box preselects the author's side if any). Nothing public may tell whether
  a reason is on its author's side. The author can withdraw their own reason while the bill is open. Writing a reason
  needs no positions consent: it is the author's own deliberate publication, public with name and chosen side.
- "Man svarbu" endorses a reason on your own side.
- Reasons and marks per person per bill are settings (`Appeals:ReasonsPerPerson`, `Appeals:EndorsementsPerPerson`);
  0 = no limit, the default. A limit switched on later removes nothing and counts only shown or waiting reasons.
- **Threshold:** one for all bills, `Digest:ThresholdDefault`, never below `Digest:ThresholdMinimum`; a moderator may set
  a bill's own number with a public statement (who set it: `default` or `moderator`). A bill once in a summary really sent
  stays eligible. No objection flow in the product and no automatic override before a vote.
- **Descriptions** ("Apie ką projektas", design 8.13): one per bill, written or corrected by a moderator on any active
  bill with a statement, or made by the summary job from official text, shown at once, with a public history; labelled
  "Sukurta DI · žmonių neperžiūrėta" (the job's, read by nobody), "Sukurta DI · taisė žmonės" (a moderator confirmed or
  corrected AI text), "Parašė moderatorius", or "Aprašymą parašė …" for an initiator's text from before 2026-11-14 (no
  rights). Beside it: the day it was made, the official text it was made from when known, the history and "Pranešti
  apie klaidą"; a report reaches the queue, where a moderator confirms (an `ai` text becomes `ai-draft`), corrects or
  removes it. A description a person ever wrote, corrected or removed is never written over by the job. Without one the
  page says so and links to the Seimas page. Descriptions never go to MPs.
- **The summary job** (worker, hourly): active bills without a description, planned first (soonest first), then the most
  participants; at most `Ai:SummariesPerDay` a Vilnius day (30; 0 = off); the text comes only through `IBillTextSource`,
  which has **no reader** yet (only the "unavailable" one: e-seimas is unverified, design 7.1), so it makes nothing and
  says so once in the log. An unavailable or insufficient text waits seven days. Audited per summary with the system as
  the actor, never the text. Only the bill's number, title and official text go to the provider.
- **Moderation after the fact:** nothing waits for approval except arguments flagged by pre-screening or reports. Hiding
  a bill's record closes taking part on it (sides and arguments kept unseen; "Atkurti" brings them back). A moderator
  in a staff-passkey session gets the queue's argument actions on the bill's page too (owner, 2026-11-14), from the
  moderators-only answer `GET /moderation/bills/{number}`; nobody else sees a control or anything from it.
- **After the final vote** (design 6.9, owner 2026-11-15): a report about an argument or the description of a closed
  record reaches the queue. On a closed record a moderator can only hide and restore an argument, correct or remove the
  description and answer a report with "Palikti" (and hide the whole record, as before); each with a statement, audited,
  the author told. The frozen counts and summaries already made never change. Everything else (threshold, holds,
  urgent summaries, merging, publishing a waiting argument, confirming a description) stays refused; the rules live in
  `RecordRules.CheckRecordAction` / `CheckReasonAction`, and the bill's moderators' panel and the queue offer exactly them.

**Reasons to come (design 8.11).**
- The agenda (`/darbotvarke`, first in the menu) and the front page's "Artimiausiu metu Seime" come from one public,
  cacheable answer that holds nothing about the caller, and show both sides for every bill (a bill nobody took part in
  says so). The front page's cards are the six bills with the most activity, left out while none has any.
  "Projektai" (`/projektai`; `/kreipimaisi` redirects there) lists active bills, sorted by activity (participants
  plus published arguments; the default), participants, arguments or planned date (owner, 2026-11-14), with a search
  and "Planuojami artimiausiu metu".
- Following a bill or an MP is private: shown to nobody, no follower count, never combined with sides or invitations,
  in the export, deleted with the account, at most `Follows:MaxPerAccount`. Emails (planned, voted; each switchable on
  the account page) go once per event to those who followed before it; nothing older than three days, nothing about the
  person's side, the bill's counts or other people.
- Links in emails: a message carries the site path, and the template makes it a full address with
  `SiteLinks.Absolute` from `Site:Address` (design 5.6). That is deployment configuration, never a runtime setting.
- The share line: the personal invitation link with `tikslas` beside the code after the `#` (a path under `/seimas/`, anything else ignored), landing
  on the bill's page, kept only in the browser (session storage) through the sign-in steps; the same link whichever
  side; no link when the code is off or used up.
- Sign-in links on a bill's page (side picker, follow, report; owner 2026-11-15) carry the page the same way
  (`LandingPath.SignInLink`: `/prisijungti#tikslas=...`), so signing in and registering both end on the bill.
- The AI draft is only in a moderator's "write or correct the description" form, on any active bill, from pasted official
  text: the Claude API (owner, 2026-11-13), the model the setting `Ai:DraftModel`, on only with `Ai:Provider` = `claude`
  and a key in `deploy/secrets/ai_api_key`; otherwise nothing mentions a draft. The prompt is the public file
  `src/Modules/Lt2.Appeals/Ai/description-draft.prompt.md`. Audited without text, 20 an hour per moderator; a description
  that started from a draft says "Sukurta DI · taisė žmonės" for its whole life. The worker is configured the same way
  for the summary job (compose passes `Ai__Provider` and the key to both). Pre-screening of users' texts stays
  rule-based. Tests never call the real API.
**Organisations (design 8.14, stage A of 8.14.14; built 2026-11-14).**
- A voice on a bill, not control. A signed-in member registers an organisation (`/organizacijos/nauja`; at most
  `Organisations:PerAccount`, 3, not counting closed ones), shown at once with nobody approving it, and is its first
  representative. Profile `/o/{slug}` (the address never changes), list `/organizacijos`; no menu item.
- Labels: "Nepatvirtinta", "Patvirtinta per el. pašto adresą @domenas" (a code sent to an address on the website's host
  or a name under it; never a public mail service or shared platform; only the domain is kept), "Patvirtino
  moderatorius". After a moderator withdraws a confirmation only a moderator confirms that organisation.
- Representatives come only by a one-time link (seven days, at most ten); they see each other's display names and
  nobody else does, staff included, except a moderator restricting a statement's writer. Nothing ties a
  representative's own side to the organisation. The last one cannot leave, only close it; deleting that account closes it.
- One statement per organisation per bill (`for`, `against`, `remarks`; up to 1 500 characters; an https link; with a
  side an optional goal "Siekiame {N} dalyvių · dabar {n}"), in "Organizacijų pozicijos" after the arguments. Never
  counted: counts, thresholds, lists, agenda, front page, statistics and summaries are the same with and without them,
  and nothing about organisations goes to MPs yet. Edits keep public versions; frozen when the bill closes; none on a
  hidden record.
- Moderation after the fact: new and changed organisations and waiting, flagged, reported or unseen statements are in
  the queue; block and lift (lifting returns exactly what was shown), hide and restore a statement, publish a waiting one,
  confirm by hand, withdraw a confirmation, "Palikti"; each emailed to every representative. Report ground "Apsimeta kita
  organizacija". Not built: "keep out of summaries".
- A statement still waiting when its bill closes (owner, 2026-11-15) can no longer be published (error
  `statement.waiting-bill-closed`); the queue still shows it and offers only "Palikti" (`/moderation/statements/{id}/keep`:
  it stays unpublished, leaves the queue unless reported, representatives are told) and hiding.
- Stage B (built 2026-11-14): one invitation code per organisation (Identity, kind `organisation`, no account on it; the
  representatives' part of the profile shows its link, a link per bill with a statement and one number, filled in only in
  the browser); a registration with it leaves only the level on the account (the pending account holds no such code: the
  browser sends it again at completion); blocking switches it off, lifting on, closing for good, apart from an
  administrator's switch; `Registration:OrganisationCodeUses`. Following an organisation (`follow` kind `organisation`, a
  third email switch): one email when a statement first becomes public (`first_public_at`), never its text; no follower
  count anywhere.

**Summaries to MPs.**
- Go to every sitting MP with an official address, joined or not. Nobody chooses recipients.
- Weekly on Monday morning in sitting weeks, and the working day before a sitting with a relevant bill. At most two a
  week per MP.
- Always both sides, always with the count for accounts older than 30 days, with the most endorsed reasons a moderator
  has seen, without their authors' names (email, public copy and snapshot alike).
- The weekly one holds appeals on that week's agenda, or never summarised, or whose participants changed by at least
  the `Digest:NotableChange` share (default 10 %) since the last summary that held them.
- Every summary has a public copy with the same numbers. Whether an MP stopped the summaries is never shown publicly.
- Timing is corrected by a moderator when people flag a wrong date: correct, hold, or approve one urgent summary.

**Feedback.** A balloon on every page (and `/atsiliepimai`) lets members write to the team; an item is linked to the
account only when the member allows a reply, otherwise nothing leads back; staff reply through the outbox; not public.

**Moderation.** Anything that reaches MPs has passed a person. Every action is audited and an argument's author gets the reason.
A report button is on every bill's description and reason. A moderator can restrict an account from writing (reasons and
date flags), until a date or until lifted, with a statement emailed to the person; it never touches sides,
marks, counts or sign-in, is never shown to anyone else, and goes with the account (no address hash is kept).

## Wording and tone

**Explanatory text (design 8.15, owner 2026-11-15).** A screen shows its title, at most one short paragraph that says
what it is, the fields with their labels, and the action; every other explanation is one click away: collapsed on the
page, or on the methodology page (`/metodika`) or the moderation rules, reached by a short link to the part that says
it (the part must exist; give it an `id`). What always stays inline: what becomes public and under whose name, what
cannot be undone, a consent and what it covers, the invented-data notice, "not a representative poll" beside the
numbers, that summaries are a trial and nothing is sent, status and error messages, labels, a field's hint needed to
fill it (format, limit), and what the design requires beside data (a description's label and date, the 30-day count,
a moderator's statement). **The one collapsing pattern** is the browser's own
`<details class="more stack stack--tight"><summary class="linkish small">…</summary>…</details>`: no script, so it
opens on server-rendered pages and in an island before it starts; an island never sets `open` from code (`js/lt2-disclosures.js`,
loaded on every page, gives a disclosure the person opened before the island started the same state once the island
has replaced the markup); the summary
names what opens ("Kaip tai veikia", "Kas yra riba?"; a bare "Plačiau" only when it is the only one on the screen).
**New screens follow the rule from the start.** `design/text-audit-2026-11-15.md` is the screen-by-screen record.

- The interface is in **Lithuanian**. New copy is read by a native speaker before it is shown externally.
- Neutral-to-positive towards politicians: connection and explanation, not control. Prefer "skirtumas" over
  "atotrūkis"; neutral chips, not warning colours, for differences.
- Numbers are a signal from self-selected "Lietuva 2.0 naudotojai", never "the public", "voters" or a referendum. Every
  place that shows them says they are not a representative poll.
- Planned Seimas dates are "Planuojama …", never a countdown: agenda items slip.
- Dates shown as numbers are `yyyy-MM-dd` (with `HH:mm`, Vilnius time); dates written in words inside a sentence stay
  in words. A date typed into a form is a text field in the form `yyyy-MM-dd` (placeholder, validated, numeric keyboard
  on phones), never the browser's date control.
- Proposed terms: *kreipimasis* (appeal), *pozicija* (position), *argumentas* (reason), *atsakymas* (an MP's
  response), *suvestinė* (summary), *iniciatorius*. Existing terms: "Man svarbu", *sritis* for subject areas,
  *patikėtinis* = "delegate" in English (stage 3).
- Do not promise in any text: that the service is free or without ads, release dates, a public API, municipal councils
  or the European Parliament, a specific licence. Never name the 2013 referendum initiative.

## Test and example data

All fictional, and the same everywhere: bill **XVP-0412** "Gyventojų pajamų mokesčio įstatymo pataisa", asks to vote
for 6 800, against 4 280; MP **Jonas Petraitis** (Reformų partija), whose position on it is "prieš"; MP **Ona
Jonaitienė** (Pilietinė sąjunga); expert **Rūta Mockutė**. Recorded Seimas responses used as fixtures are real public
data; everything about users is invented.

Addresses in tests end in `@pavyzdys.test`; hosts (an organisation's website, the site's own address) are invented names
under `.test`. Only the rules about public mail services name real providers (gmail.com, inbox.lt …), because those
rules are about them.

## Writing tests

- **Use the shared steps; add to them rather than copy.** Integration tests: `tests/Lt2.IntegrationTests/Infrastructure/`
  (`AppealSteps`, `OrganisationSteps`, `ReasonsToComeSteps` with `VilniusToday` and `MondayInWeeks`, `SettingsSteps`,
  `StaffMembers`, `LoggingFactory`). End-to-end tests: `tests/Lt2.E2E/E2ESteps.cs` (registration from any link,
  `Mailpit`, screenshots, API calls from the page, closing a test's organisation).
- **Staff:** `ModeratorAsync()` / `AdministratorAsync()` of `ApiTest` give the run's shared staff members. Only a test about
  the staff account itself (passkeys, sessions, a reset, a per-moderator limit) makes its own with `StaffMembers.ActiveAsync`.
- **Settings:** change one through the application that reads it (each keeps a short cache) with `SetSettingAsync`, and
  put it back in a `finally` with `ResetSettingAsync` (the design's default) or `WithSettingAsync` (the value before);
  never with a number written into the test.
- **A check that something is absent must be able to fail.** Assert the status of every set-up step and of every read a
  privacy sweep makes; compare an answer's exact set of fields rather than searching a payload that holds random ids for a
  short string; prove the swept answer held what it is about.
- **Shared data:** the integration tests share one database and run one at a time; a test frees the bill it uses
  (`FreeBillAsync`) and keeps to its own summary weeks (`MondayInWeeks`). The end-to-end classes share one stack and run one
  at a time; a test that hides or changes a demonstration bill restores it in a `finally`, and every name a run gives an
  organisation carries "(E2E)".
- **Waits:** no fixed sleeps. In end-to-end tests a step after a navigation or a sign-in waits up to `E2ESteps.Slow`
  (60 s); Playwright's default of 5 s failed when an integration run shared the machine.
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
