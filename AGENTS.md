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
| **M2b Sending** | Real summary emails to MPs, MP preferences, profile take-over, MP responses. Built first (owner, 2026-11-15): the link of design 8.5.1–8.5.4 (recognised at once, the take-over, the office), then the response (8.5.5–8.5.7). Then (2026-11-16) sending itself, design 8.19: `Digest:Sending`, approval, the real email, preferences without an account, the introduction letter, the suppression list; off by default. Then (2026-11-16) design 8.20: the followers' email about MPs' answers and known browsers for sign-in. |
| **Stage 3** | Voting with delegation, AI summaries, analytics. Not designed yet; do not build towards it beyond what the design says. |

Build one stage at a time. Do not start a later stage's features because they look close.

## Nothing is launched yet

**There is no production and there are no users (the owner, 2026-11-16).** Everything that runs, the local stack and
`test.lietuva2.lt` alike, is a test version with invented data. Until the owner says otherwise and removes this section:

- **No data is worth keeping.** A database may be emptied or recreated; a migration may rewrite or drop anything and
  needs no backfill, no careful "down", no step that preserves existing rows.
- **No existing members to protect or carry along.** Do not weigh a decision by what it does to people who registered,
  wrote or agreed to something "before": nobody did. No grandfathering, no asking anyone again, no notice of a change,
  no transition period.
- **No backward compatibility.** Old addresses, old links in old emails, old field names in the API and old stored
  values need not keep working; change the thing and its tests.
- **Do not argue from launch-time costs.** "Members would have to accept again", "followers would lose…", "this breaks
  links people saved" are not reasons here. Say what the rule should be at launch and build that.

What this does not loosen: the design's rules are the rules of the product as it will launch (privacy, security, wording,
tests), so they are built and tested as written; and nothing is pushed or published without the owner asking.

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
                           Security/TrustedProxy.cs; no-store on pages answered to a signed-in session; Security/TermsGate.cs,
                           design 8.22.2; Security/BrowserStorage.cs, the inventory of 8.22.1), the layout (Components/Layout:
                           the menu, AccountMenu with js/lt2-account.js, the footer) and the terms and privacy pages
                           (Components/Pages/Terms, Privacy, LegalText.resx)
src/Lt2.Hosting            what the web host and the worker set up the same way: Serilog and OpenTelemetry
                           (Observability.AddLt2Observability), with SecretPaths replacing an MP link's token in every log
                           line and trace; no module references it
src/Lt2.Web.Client         Blazor WebAssembly components and HTTP implementations of the API clients; the interface's
                           shared pieces: Components/InteractiveBase<TText>, Display (numbers and dates, Vilnius),
                           StringLocalizerExtensions (Format), CopyLink, FieldError, Messages, SplitBar, Hint (the short answer in place, js/lt2-hints.js),
                           ProjectTabs and MenuSections (design 8.21), BrowserKeys (the storage keys, design 8.22.1),
                           LongText (a long member's text with "Rodyti daugiau")
src/Lt2.Api.Contracts      DTOs, routes, client interfaces, validators (shared by server, client and tests)
src/Lt2.SharedKernel       Result type, identifiers (UUID v7), outbox, audit and settings interfaces, connection
                           strings and the one EF Core set-up (Persistence/: UseLt2Npgsql), VilniusTime (the
                           Europe/Vilnius zone and its days), the signed-in account (ClaimsPrincipal.UserId()), the
                           staff actor (StaffActor) and staff statements (StaffStatements), the outbound allowlist
                           (Http/: hosts from Outbound:AllowedHosts plus AddOutboundHost; redirects followed only within
                           it), TextExcerpt (also compiled into Lt2.Web.Client); no clock of its own (TimeProvider is used)
src/Modules/               Lt2.Identity, Lt2.Notifications, Lt2.Audit, Lt2.Parliament (Seimas open-data client,
                           import, statistics); Lt2.Appeals (bills' records, positions, arguments, endorsements,
                           reports, moderation, rule-based pre-screening, flags, summaries content, descriptions written
                           by moderators, the AI draft in Ai/ with its public prompt file, the AI summary job
                           (Application/DescriptionSummaries.cs), the "unavailable" IBillTextSource, and organisations
                           with their statements on bills, design 8.14.14; MPs' responses, 8.5.5). Notifications
                           also owns the summaries (Digests/: making, approval, sending, SendingRules), MP mail (MpMail/:
                           the links' tokens, the MPs' choices, the introduction letter, the suppression list, MpSender)
                           and following (Follows/: follows, email switches, bill
                           events, the daily email about MPs' answers). Modules talk through interfaces in SharedKernel (IParticipants, IBillCatalogue,
                           IPlannedAgenda, IMpDirectory, IMpMailAddresses (the one way an official address leaves
                           Parliament, for sending), IDigestSource, IPartySupporters, IAccountData (one per module),
                           IPositionErasure, IOutbox, IAuditLog, ISettings, IAiAssistant, IBillTextSource,
                           IOrganisationDirectory, IOrganisationCodeSwitch, IMpLinks, IMpResponseFeed,
                           IWritingRestrictions). The host
                           composes the agenda (GET /api/v1/agenda) and /me/follows from the modules' interfaces.
                           Identity also owns MPs' links (Application/Politicians: politician_link, politician_token,
                           design 8.5); "whose official address is this" is IMpDirectory.OfficialAddressOwnerAsync
                           (Parliament's OfficialAddresses, the one definition), and the MP's own account is refused as a
                           participant through MpAccounts.Refusal (SharedKernel) wherever a participant is checked.
                           Identity also owns known browsers (Application/SignIn/KnownBrowsers.cs, design 8.20.2: the
                           __Host-lt2.device cookie, identity.known_device, the code allowance they choose).
src/Modules/Lt2.Usage      the site's own usage statistics (design 8.27): UsageRules and Sketch (HyperLogLog) pure,
                           UsageCounter in the web host's memory, written every minute; the host feeds it from
                           Services/UsageServices.cs; the screen /administravimas/lankomumas (UsageAdmin)
src/Lt2.Worker             Hangfire server: outbox dispatch loop, the parliament import, the agenda watcher (lrs.lt
                           pages; off unless Agenda:BaseAddress is set), the daily aggregates and closing of the bills' records, summaries
                           (every 15 minutes: waiting ones expire, due ones are made: a dry run, waiting or sent by
                           Digest:Sending), the follow emails (every 15 minutes; the one about MPs' answers once a day from Follows:ResponsesAt), the AI summaries
                           of bills (hourly; makes nothing until a text reader and a provider exist), a daily identity
                           clean-up (with the unsent mail of purged registrations and known browsers not seen for a year) and a daily outbox clean-up (failed
                           messages after 30 days); the dispatcher sends sign-in and confirmation codes first, a batch
                           over one SMTP connection per stream (MP mail on its own: Email:Mp), and stores only an error's
                           type and status code (design 6.8); a 5xx refusal of an MP email suppresses the address
src/Lt2.Migrator           creates the database roles, applies migrations and grants; also seeds invitations, grants
                           the first staff role, empties invented parliament data, switches on registration with an MP's
                           official address without an invitation (--official-addresses-without-invitation; only where
                           Demo:Allowed, the local stack and the trial) and (local stack only) fills the
                           demonstration record on XVP-0412, three invented organisations with statements on it, and an
                           invented AI summary on XVP-0533 and a closed invented bill XVP-0707 with invented positions
                           (design 8.26.5) (--demo-appeal);
                           run once per deploy
tests/Lt2.UnitTests        rules and validators, plus bUnit tests of components (Components/)
tests/Lt2.IntegrationTests API over HTTP against a real PostgreSQL (Testcontainers)
tests/Lt2.ArchitectureTests module boundaries, static assets
tests/Lt2.E2E              Playwright and stack checks against a running compose stack; skipped without one; E2ERun (the run's
                           invitation), E2ETest (every class's base: deletes what a test made), leftovers.sql
tests/Shared/TestOrder.cs  the order tests run in, from LT2_TEST_ORDER (compiled into every test project)
tests/Lt2.Parliament.ImportTests  Seimas client, parsers, register of assumptions (Seimas/ASSUMPTIONS.md), import against
                           the stand-in and PostgreSQL; live contract suite only with LT2_SEIMAS_LIVE=1
deploy/                    Dockerfile (restore layer first; .dockerignore at the root), compose.yaml, Caddyfile, .env (local defaults), init-secrets.py (optional target
                           directory; as root on Linux gives each secret to the container user reading it), secrets/;
                           compose.shared-proxy.yaml and shared-proxy.Caddyfile: behind a server's existing Caddy, with
                           the Mailpit viewer on its own hostname behind Mailpit's password file (design 9.2,
                           README.md; LT2_PROXY_HOST names that Caddy, the only sender whose forwarding headers count);
                           LT2_SECRETS_DIR and LT2_IMAGE_TAG move secrets and tag images
tools/run_tests.py          runs a test project in a chosen order (or each class alone), keeps the TRX report and the order,
                            and for end-to-end runs prints what tests could leave on the stack before and after
tools/Lt2.SeimasStandIn     local and test only: stand-in for the Seimas open data and the lrs.lt agenda page (/darbotvarke),
                           invented answers; never shipped. Recent/ (container only, never in tests) adds a sitting and
                           a stage vote dated two days before today, so the local agenda has recent votes, and an adoption
                           vote nine days before today on XVP-0707, for the analytics page
```

```sh
dotnet build                                          # warnings are errors
dotnet test                                           # unit, architecture, integration (needs Docker)
dotnet test --project tests/Lt2.UnitTests             # one project (Microsoft.Testing.Platform)
python tools/run_tests.py e2e --order random          # a project in another order: default, forward, reverse, random[:SEED]
python deploy/init-secrets.py                         # once, and again when a secret is added: missing ones in deploy/secrets/
docker compose -f deploy/compose.yaml up --build      # full stack: https://lt2.localhost:8443, Mailpit :8025
```

If Windows has reserved port 8080, set `LT2_HTTP_PORT=8090` for every compose command. End-to-end tests run only with
`LT2_E2E_BASE_URL` set (`tools/run_tests.py e2e` sets the local stack's addresses); `README.md` lists their variables. A run
needs nothing else: no clean-up before or after, no invitation to top up, no restart of the web container.

**The end-to-end suite runs only before a merge into master (the owner, 2026-11-16).** It is the last check on finished
work, run once on the branch about to be merged; when several branches are being worked on at the same time, once on
all of it together at the end. It is not run while building, after each change, or by an agent checking its own step.
During work: build, the unit, architecture and integration tests, and a look at the changed page (a screenshot or the
Browser pane) where the change is visible. One end-to-end class may be run alone to check the fix of a failure the
pre-merge run found.

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
- Limits that count rows (codes per address and per known browser, known browsers per account, one current invitation
  code, one urgent summary a week, follows per account) are counted under a PostgreSQL advisory lock in the same
  transaction (design 6.8); Identity's are in `IdentityLocks`, Appeals' in `AdvisoryLocks`.
- Every email is built by `EmailLayout` (Notifications/Templates) and rendered from its outbox kind by one
  `EmailRenderer<T>` registered in `NotificationsModule`; shared texts use the `Email.*` keys of `EmailTemplates.resx`.
  `Build` and `BuildMp` take the footer (`EmailLayout.Footer(l, site.Value.Address)`: the privacy notice's full address,
  design 8.22.3), so no email goes without it.
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
  `<StatementQueueItem>`; the positions consent before a first side is `<PositionConsentForm>`; the bill page's islands
  share the caller's answers through `MyBillState`, the front page's through `MyAgendaState` (its `Changed` event tells
  the others after a member's action). A list whose items
  hold state or re-sort is keyed (`@key`), and an element id is unique on the page (a component shown twice takes an
  `IdPrefix`).
- **Surfaces (design 8.16).** The ground carries what frames a page: title, lead, the way back, notices (`.note`), an
  introduction, a plain row of links. What a person comes to read or act on lies on a `.sheet`, one unit per sheet (a
  list, a table, a part of a bill's page, a form, a queue item); a sheet is never inside a sheet. A form or panel that
  opens inside a sheet is `.sheet--quiet`, and a quiet sheet never stands on the ground. A component used both inside
  and outside a sheet takes a `Panel` parameter and drops its own sheet inside one (`QueueReasonItem`,
  `StatementQueueItem`). Filters and sort links of a list sit on the ground directly above the list's sheet, on every
  list page.

## Security and privacy rules

A person's side on a bill is a political opinion: special-category data under GDPR. These rules are not negotiable in
code review.

- No endpoint, page, email, log line or admin screen reveals another person's position. Administrators included.
- Do not store per-person history of changed positions. Trends come from daily aggregate counts.
- Logs carry no emails and no positions. IPs are hashed.
- Group numbers (for example supporters of a party) are shown only for groups of at least 20.
- Sign-in has no passwords: an emailed one-time code or a passkey. A staff role works only in a session signed in
  with a staff passkey; a code session is a member session, whatever roles the account has. Politicians need a
  passkey too: speaking for an MP needs a passkey session, read from the database per request (the link is not a role in
  the session), and on a linked account (the MP's or an office member's) a passkey is added only in a passkey session or
  by the take-over link. Responses never reveal whether an email is registered. Changing roles, resetting an account and saving
  a runtime setting need a fresh passkey confirmation (`FreshConfirmation`, the last ten minutes); a staff account is
  deleted only from its staff passkey session; what a staff member did keeps their account id after deletion (design 6.9).
  A code session on a staff account becomes a staff session without signing out (owner, 2026-11-16): `/prisijungti/raktas`,
  `POST /me/staff-session` (`StaffHandlers.StartStaffSessionAsync`), the signed-in account's own staff passkey only
  (anything else `passkey.staff-required`, nothing changed), a new session key (`Infrastructure/SessionRotation`: the old
  row is deleted, never renewed), a fresh confirmation, audited as `staff.session-started` without an email.
- **Sign-in codes (design 6.2, 8.20.2).** Browsers the account does not know share five codes an hour and ten a day per
  address (`SignIn:CodesPerHour`, `SignIn:CodesPerDay`); a browser known to the account (it signed in to it, or finished the
  registration there) has its own five an hour and ten a day, the same two settings counted separately
  (`KnownBrowserRules`), and a code it asked for (`sign_in_code.known_device_id`) is accepted only from it: requests and wrong guesses elsewhere neither use it up nor cancel
  it. The rules are pure functions (`SignInCodeRules`, `KnownBrowserRules.CodesToTry`); the answers to a code request and a
  wrong code are the same whether the address has an account and whether the browser is known. The account page lists
  "Žinomos naršyklės" beside the sessions with "Pamiršti" (`GET|DELETE /me/known-browsers`), at most 20 per account, deleted
  after a year unseen, exported and deleted with the account; `identity.known_device` is withheld from `lt2_readonly`.
- The read-only database role reads only the tables on `DatabaseRoles.ReadableTables`; a new table goes on that list
  or on `DatabaseRoles.WithheldTables` in the same change (an architecture test fails otherwise).
- Session cookie only (`__Host-`, Secure, HttpOnly, SameSite=Lax). No tokens in the browser. The one other lasting cookie is a
  known browser's `__Host-lt2.device` (design 8.20.2, the same attributes, a year): it never authenticates, lengthens a session
  or skips a code; it only chooses which code allowance and which codes apply, and it is stored only as a keyed hash
  (`Security:DeviceHashKey`, the secret `device_hash_key`). Every state-changing
  call needs the antiforgery header and passes the Origin check, except the one-click stop of an MP's emails (design 8.19.4),
  which mail programs send. CORS stays off.
- **Nothing non-essential in the browser (design 8.22.1), so no cookie banner.** No third-party analytics, no third-party script, font or
  embed, no advertising or measurement cookie. What the site keeps is one list, `Security/BrowserStorage.Inventory` (the
  session, the passkey step, the request-forgery and the known-browser cookies; `lt2-theme` in local storage once a theme is
  chosen; `lt2.tikslas` and `lt2.kodas` in session storage, `Lt2.Web.Client/BrowserKeys`). The privacy notice's
  "Slapukai ir naršyklės atmintis" is built from it (`data-storage` on each row); `TermsTests` (E2E) visits the site as a
  visitor and as a member and fails on any cookie or storage key outside it, `BrowserStorageTests` (architecture) on any script
  that starts using storage. A new cookie or key is a design change, not a build detail; the account item's open state is not
  stored.
- No secrets in the repository or in images. No real personal data in tests or fixtures.
- No tracking pixels or click tracking in any email.
- **Usage statistics (design 8.27)** are counted on the server only: a visitor is the network and the user agent hashed
  with a random salt of the day, month and year into a sketch; a signed-in session counts its account. A page counts
  under its route (a token never), staff and MP pages stay out of the list. `usage.sketch` holds the salts and sketches
  only while their period lasts (a candidate can be tested against them until then, 8.27.2), only `lt2_app` reaches it,
  and it is withheld from `lt2_readonly`. Nothing is added to the browser for it.
- Outbound requests go only to the allowlisted hosts, and follow no redirect to another host.
- Codes and tokens are not sent in addresses: a link the site makes carries its secret after the `#` (`/prisijungti#kodas=…`,
  a representative's `/organizacijos/kvietimas#…`, an MP's take-over link `/seimo-narys/perimti#…`, an office member's
  `/seimo-narys/biuras#…`); the page reads it and sends it in a request body (design 6.8). Old `?kodas=` links still work.
  The MP's mail links are the exception the design names (8.19.4: the one-click stop needs its token in the address): their
  paths are logged with the token replaced.
- Forwarding headers count only from the proxy named in `ReverseProxy:Address`. A page answered to a signed-in session is
  `no-store`.

## Product rules to keep consistent

**Navigation (design 8.21, built 2026-11-16).** The menu is the logo, "Projektai", "Seimo nariai", and "Prisijungti" or the
account item "Paskyra"; one line from 360 px (`MenuTests`, E2E). "Projektai" opens `/darbotvarke` and is current (`aria-current`)
on its four tabs and everything under them (`MenuSections`); the four tabs ("Darbotvarkė", "Projektai", "Suvestinės",
"Analitika") are `<ProjectTabs>` under the heading of `/darbotvarke`, `/projektai`, `/suvestines`, `/seimas/analitika`. The history of votes is no tab (the
owner, 2026-11-16): the agenda's voted list links to it and it leads back by a breadcrumb (`#to-agenda`). The
account item (`Layout/AccountMenu`) is a `<details>` with "Mano paskyra" and "Atsijungti" (the server-rendered sign-out form);
`js/lt2-account.js` fills the name and the account's links (staff screens, an MP's projects, accepting changed terms; for a
staff account whose session is not a staff session, "Prisijungti darbuotojo prieigos raktu" to `StaffSessionPage` with the
page to come back to after the `#`) from `GET /me` when it opens and closes it on Escape, a press outside and focus
leaving. **No served page holds a member's name or role links**: the header is the same for every member apart from the antiforgery token (`NavigationTests`). The footer, on
every page (token pages from emails included), holds "Apie" (Metodika, Moderavimo taisyklės, Naudojimo taisyklės,
Privatumas), "Dalyvauti" (Organizacijos, Atsiliepimai), the theme switch and the trial line. `/metodika` is called "Metodika".

**Terms of use (design 8.22.2).** `/taisykles` and `/privatumas` are static pages from `LegalText.resx` (drafts for the lawyer;
"[bus įrašyta]" marks what the owner has not given). Registration has the required box "Sutinku su naudojimo taisyklėmis"
(`CompleteRegistrationRequest.AcceptsTerms`, checked by the validator and again in `CompleteRegistrationHandler`); the accepted
version is a consent of kind `terms` (`TermsOfUse.Version`, SharedKernel; the client's `TermsText.Version` is the same). A changed
text gets a new, later version: every member is asked again. A member whose accepted version is missing or behind
(`TermsOfUse.NeedsAcceptance`; `GET /me` says `termsRequired`) is sent to `/taisykles/sutikimas` after signing in and by
any island refused with `terms.required` (`InteractiveBase`); until they accept, reads work and every API write is refused in
one place, `Security/TermsGate` (before authorization, so a new write route is covered by default; `TermsTests` sweeps every
mapped write), except `TermsExemptions.Routes` (accepting, signing out, deleting the account, the MP mail token routes). The
export is a read. The server-rendered feedback form asks the same question. Pending accounts accept with their registration;
the invented demonstration members get the consent like their others (`--demo-appeal`, repeatable).

**Accounts.** Registration is by email only, by invitation during the closed trial, with a self-declared age of 18 or
older. An account is pending until its address is proven; the display name, the age declaration and the terms of use are
asked after the code, never before. One account per normalised address. Each account has a `verification_level` (`email` now); phone
verification is a later decision. Every member has one personal invitation code (replaceable; uses unlimited unless
`Registration:PersonalCodeUses` is set) and sees only how many registered with it; administrators see numbers per day,
code and level, never who invited whom, and can switch codes off. Invitations are never combined with sides.
A member changes the display name on `/sveiki` (`PUT /me/display-name`, `DisplayNameHandlers`; the designer's proposal
awaiting the owner): the registration's rule (60 characters, empty allowed), refused under a writing restriction (asked of
Appeals through `IWritingRestrictions`), as often as the member likes (owner, 2026-11-16; `RateLimits:Participation` guards
against a runaway script), audited without the names, no earlier names kept. Names are read live everywhere, so a
change shows beside older arguments too.

**The account page (`/sveiki`, design 8.21)** is four parts on the ground, reached from the `#account-parts` row of plain
links: "Profilis" (`#profilis`), "Sekimas ir laiškai" (`#sekimas-ir-laiskai`), "Dalyvavimas" (`#dalyvavimas`, with the staff
part: the staff links, or for a staff account not in a staff session the link to its staff passkey) and "Saugumas ir
duomenys" (`#saugumas-ir-duomenys`); sheets inside them have `h3` headings. Keep the anchors emails link to (`#sekami`,
`#zinomos-narsykles`, `#my-data`, `#my-invitation`).

**MP profiles.** Compiled automatically from Seimas open data for every MP, joined or not. Two statistics:
participation (share of recorded votes in which the MP voted for, against or abstained) and voted with the faction
(only votes where at least two thirds of the faction's voting members chose the same option; free votes are excluded
and their count shown). No expenses, rankings or scorecards. An MP takes over a profile through a link sent only to the
official `@lrs.lt` address from open data; the link is tied to the Seimas person id (details below). Each MP's ballot on a vote is listed
in one place, the vote's page (`/seimas/balsavimai/{id}#balsai`, design 8.17: `VoteBallots`, filtered by `BallotList` in
Lt2.Web.Client): a name search without case or diacritics, a faction dropdown (with "Be frakcijos"), the ballot, 20 rows
then 40 more, its state in the address (`q`, `frakcija`, `balsas`, `rodyti`) with no script; a closed bill's page only
links to it. The history of votes (`/seimas/balsavimai`, design 8.18: `VoteHistoryPage`, its address state in
`VoteHistory` in Lt2.Web.Client, `GET /api/v1/votes` read and counted in the database a part at a time) groups votes by
Vilnius day, 30 then 30 more up to 300, with no participants' numbers; its rows share `VotedItemMain` with the start
page's "Balsuota".

**MPs on the platform (design 8.5, built 2026-11-15: the link 8.5.1–8.5.4, then the response 8.5.5–8.5.7).**
- The official address: an "El. p." contact of open data on `lrs.lt` (not a sub-domain), compared ignoring case, of an MP
  whose mandate has not ended; listed for two or more such MPs, it belongs to nobody (`OfficialAddresses` in Parliament,
  also used for the summaries' recipients). An account whose own address is one gets the offer in its own `GET /me`
  (`mpOffer`), shown on `/sveiki` by `MyMp` in the browser only; the page the server renders holds nothing of it
  (`ServerMeApi` leaves `mpOffer` and `mp` out). Nothing is linked or public before the take-over.
- `Registration:OfficialAddressesWithoutInvitation` (off by default, the designer's proposal awaiting the owner): both
  invitation gates ask `OfficialAddressGate`. The local `.env` switches it on through the migrator.
- "Perimti profilį" (`POST /mps/{slug}/claim`) sends a 24-hour one-time link (token hashed, a new one replaces the old,
  three a day per MP) to the account's own address. `POST /me/mp/takeover` (token and passkey) does everything in one
  transaction: token and address checked, the passkey stored and the others removed with their sessions, the link made,
  the account's sides on records not closed withdrawn (`IPositionErasure.WithdrawOpenSidesAsync`; the front page's copy
  marked stale), audited, the official address told; the session becomes a passkey session (`SessionTicketStore.RenewAsync`
  takes the new ticket's method and passkey). A lost passkey: the same again; the link stays.
- One account per MP and one MP per account; an office member of at most one MP, five per MP, by a seven-day one-time
  link accepted in a passkey session. Giving the profile up (`DELETE /me/mp`), an administrator's end
  (`POST /admin/mps/{slug}/link/end`, statement, fresh confirmation; no screen yet) and deleting the account end the
  link and the office with it. Every change is audited and told to the MP's account's own address (`AccountNotices.Mp*`),
  never naming an office member.
- The MP's own account takes no part (the designer's proposal awaiting the owner): no side, argument, "Man svarbu", party
  support or organisation (`mp.takes-no-part`); it follows like anyone; office members are not restricted.
- The only public trace: "Profilį tvarko Seimo narys arba jo biuras" with the date on the profile (`managedSince` of
  `GET /mps/{slug}`). A profile nobody took over says nothing about joining. Who is linked and who the office members are
  is in no public answer, page or log.
- Texts carrying an MP's name put it in the nominative after a colon ("Tvarkote Seimo nario profilį: {0}.", "Seimo narys:
  {0}."): names are not declined by the software.
- **The response** (8.5.5): one per MP per bill (`appeals.mp_response`, keyed by the Seimas person number and the bill's
  number, withheld from `lt2_readonly`; `written_by_user_id` is never public and is emptied with the account, which also
  exports the responses it wrote). The host composes it (`Lt2.Web/Services/MpResponseServices.cs`): `IPoliticians.SpeakerAsync`
  says whom the account speaks for and whether the session may write (read from the database per request), Parliament gives
  names, factions and whether the mandate has ended, Appeals (`MpResponsesService`) stores it. Writing needs the MP's account
  or an office member in a passkey session, an MP whose mandate has not ended, an open bill (until the adoption vote, as a
  side) and no writing restriction; a response a moderator hid is the moderators' to restore (`response.hidden`). Public at
  once; every change keeps the replaced text as a public version (`revision`, kind `response`, without a person); a withdrawn
  one stays as "Atsakymas atsiimtas" with its versions; written again it is the same row. After the adoption vote the MP's
  ballot at it is given beside it ("Atsakė: … Balsavo: …", `nebalsavo`, consensus said so); nothing adds answers and ballots
  up anywhere. Never counted: no count, threshold, list, statistic or summary changes (`MpResponseTests`).
- Moderation as for statements: flagged by pre-screening and not yet seen, or reported, it is in the queue
  (`GET /moderation/responses`, `ResponseModerationView` on `/moderavimas` and `/moderavimas/paslepti`); "Palikti" (`keep`),
  hide with a statement shown in its place and emailed to the MP's account's own address (`MpResponseNoticeEmail`, through
  `IMpLinks`), restore while the bill is open. Report target `response`.
- Where it shows (8.5.6): the bill's page part `BillResponses` (its own sheet after the organisations, server-rendered and the
  same for everyone; the counts line; "už", "prieš", "neapsisprendė", by last name, then withdrawn and hidden ones) and the
  profile's `MpAnswers` (newest first; no part without responses); both show 10 and "Rodyti daugiau" by `?atsakymai=` landing
  on `#atsakymas-{n}` (`ResponsePaging`). Each item is `MpResponseItem` with the one mark "Seimo narys arba jo biuras". The
  linked account's form is the island `MyMpResponse`, filled in the browser only; reports are `ReportResponse` islands.
- `/seimo-narys/projektai` (8.5.7, `MpBills`, linked from `MyMp`; `GET /me/mp/bills`): the bills in summaries now in the
  summaries' order (`Lt2.Notifications.DigestLists.InSummariesNow`), with the MP's own response or "Atsakyti"; a code session
  may read it; nothing about other MPs.

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
- Reasons: plain text, public with the author's display name, for either side, whatever the
  author's own (no side needed; the writing box preselects the author's side if any). Nothing public may tell whether
  a reason is on its author's side. The author can withdraw their own reason while the bill is open. Writing a reason
  needs no positions consent: it is the author's own deliberate publication, public with name and chosen side.
- **One length for members' texts (owner, 2026-11-16):** `Texts:MaxLength` (characters, 2 000; 0 = no limit) for
  arguments, organisations' statements and MPs' responses (and comments and "Apie mane" when built), checked where each
  is written (`AppealRules.CheckReasonText`, `OrganisationRules.CheckStatement`, `ResponseRules.CheckResponse`, all through
  `AppealRules.TooLong`; the refusal names the number); the one technical ceiling, 20 000 (`MemberTextLength.Ceiling` =
  `SettingKeys.MemberTextCeiling` = `MemberTextLimits.Ceiling` in the contracts), holds whatever the setting says and is all
  the shared validators check. The columns are `text`. A lower number later shortens nothing. The forms read the number
  from `AppealRulesResponse.TextMaxLength`. A bill's description, moderators' statements, reports, feedback, names and
  links keep their own limits. A long text shows its beginning and "Rodyti daugiau", opened "Rodyti mažiau" under the
  rest (`TextExcerpt`, one file in SharedKernel compiled also into Lt2.Web.Client; `Components/LongText`): an argument
  about 300 characters, a statement or a response about 1 500; the moderators' queue keeps texts whole.
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
- The agenda (`/darbotvarke`, where the menu item "Projektai" leads) and the front page's "Artimiausiu metu Seime" come from one public,
  cacheable answer that holds nothing about the caller, and show both sides for every bill (a bill nobody took part in
  says so); the answer also names, per bill, the organisations with a public statement (as on the bill's page, never
  counted). The front page (design 8.7, reworked 2026-11-15; mockup `design/ui/pradzia.html`) is that agenda as a
  timeline of sitting days: a visitor first gets "Kaip tai veikia" (the same texts as `/metodika#kaip-tai-veikia`) and
  links to register or sign in, never a form; a member gets "Jūsų projektai" and their marks in the rows, islands filled
  in the browser from `GET /me/bills` (one request, never prerendered; it also says whether the caller gave the positions
  consent), so the HTML is the same for every member. The timeline is on a `.sheet` (design 8.16); the page has no
  "Organizacijos" block and the introduction no paragraph on following (owner, 2026-11-16: the footer links to
  organisations, the account page registers one, "Sekti" says what it sends); the visitor's trial note sits under the
  logo and title with the invented-data notice's edges. "Kas yra riba?" is one hint beside the note under the timeline, as on `/darbotvarke`. A coming bill with an open record offers "Prašau balsuoti" with "už" and "prieš" in its row (owner,
  2026-11-15): a visitor's are `LandingPath.SignInLink` links to the bill's page; a member's side column is one island
  (`MyAgendaSide`, disabled before it starts) that takes, changes ("Atsiimti" withdraws) the side with the bill page's
  request, asks the first-time consent across the row with the shared `<PositionConsentForm>` (also in `SidePicker`), then
  shows the bill's live numbers from `GET /bills/{n}/record`; the other islands follow through `MyAgendaState.Changed`.
  The figures are one component (`Lt2.Web.Client/Agenda/RecordFigures`, with `SplitBar` in `Lt2.Web.Client/Components`),
  rendered by the server and by the island; with a side and a copy that says zero, the island reads the live numbers once on
  start. The server keeps the front page's agenda in memory for 60 s (`FrontPageAgenda`; a test removes
  `FrontPageAgenda.CacheKey` instead of waiting). A side taken, changed or withdrawn marks the copy out of date
  (`FrontPageFreshness`, marked by `TakingSides` for every API client): the first request once the copy is 2 s old reads
  the agenda again; other changes wait for the minute. The agenda page, the bill's page and the API stay live.
  "Projektai" (`/projektai`; `/kreipimaisi` redirects there) lists active bills, sorted by activity (participants
  plus published arguments; the default), participants, arguments or planned date (owner, 2026-11-14), with a search
  and "Planuojami artimiausiu metu".
- Following a bill or an MP is private: shown to nobody, no follower count, never combined with sides or invitations
  (the person's own `/me/bills` gives them both), in the export, deleted with the account; there is no limit on how much a
  person follows (owner, 2026-11-15: `Follows:MaxPerAccount` and `follow.limit` are gone). Emails (planned, voted; each
  switchable on the account page) go once per event to those who followed before it; nothing older than three days,
  nothing about the person's side, the bill's counts or other people.
- "Seimo narių atsakymai" (design 8.20.1, the fourth switch `responses`): one email per followed bill per Vilnius day, the
  morning after, from `Follows:ResponsesAt` (08:00, a runtime setting under "Laiškai sekantiems"): the MPs whose response
  became public or changed stance that day (`appeals.mp_response.announced_at`, `ResponseRules.Announces`; a changed
  explanation with the same stance is no event) and are still public, with name, faction and stance and the link to
  `#atsakymai`, never the explanation. Read through `IMpResponseFeed` (Appeals); each person is told only about answers from
  after they followed (`FollowRules.ResponsesFor`), and the MP's own account and office members never about their own MP
  (`IMpLinks.SpeaksForAsync`). Recorded in `bill_event` kind `response` (key: the day); a day with nothing left sends nothing.
- Taking a side follows the bill (design 8.11.2, owner 2026-11-15): a member's first side on a bill (no side before)
  follows it in the same request, composed by the host (`Lt2.Web/Services/TakingSides`: the Appeals module saves the
  side, then `IFollows.FollowBillAsync`), on the bill's page and in a front-page row alike. The side is never refused or
  undone because the follow failed. Changing a side adds nothing, withdrawing it keeps the follow, "Nebesekti" keeps the
  side; sides from before were not backfilled. The follow row holds nothing about the side.
- Taking, changing or withdrawing a side, following and unfollowing (bills, MPs, organisations), "Man svarbu" and changing
  the display name share one per-account request limit, `RateLimits:Participation` (60 a minute): a guard against a runaway script, not a rule.
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
**Analytics (design 8.26, owner 2026-11-16).** `/seimas/analitika` (`Components/Analytics/AnalyticsPage`), public and
server-rendered from `GET /api/v1/analytics` (composed in `Lt2.Web/Services/AnalyticsServices.cs`): the four figures, the
Seimas's share "for" beside the members' frozen counts on closed bills, faction unity (`FactionUnityRule` in Parliament).
"Kas Seime balsuoja taip, kaip prašote jūs" is the island `MyAgreement` from `GET /api/v1/me/analytics/agreement`: the caller's own sides
against the MPs' ballots, the only place MPs are ordered by a number; the server-side API client refuses it, so no page
holds it. Kept copies (owner, 2026-11-16) live only in memory (`AnalyticsCopies`): the public answer per filter and data
version, served as it is while one background refresh runs once it is an hour old; a member's comparison per account until
12 idle hours, sign-out, account deletion or consent withdrawal (`Forget`). Every rule is
`AnalyticsRules` (SharedKernel); one sample of 20 (`AnalyticsRules.SmallSample`, not a setting) below which no share is
shown, only counts and "maža imtis"; the Seimas's majority is that of those who voted, a consensus counting as "for".
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
- One statement per organisation per bill (`for`, `against`, `remarks`; as long as `Texts:MaxLength` allows, owner 2026-11-16; an https link; with a
  side an optional goal "Siekiame {N} dalyvių · dabar {n}"), in "Organizacijų pozicijos" after the arguments. Never
  counted: counts, thresholds, lists, statistics and summaries are the same with and without them (the agenda and the
  front page name them beside a bill, never with a number), and nothing about organisations goes to MPs yet. Edits keep public versions; frozen when the bill closes; none on a
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
  has seen, without their authors' names (email, public copy and snapshot alike). A reason longer than about 300
  characters is shortened in the email (and its preview) by `TextExcerpt`, with "Visas argumentas: {link}" to it on the
  public copy (`MpMailPaths.ReasonOnPublicCopy` = `DigestRoutes.ReasonAnchor`); the snapshot keeps the whole text (owner,
  2026-11-16). The email prints no other member-written text (the MP's own response as stance and date only).
- The weekly one holds appeals on that week's agenda, or never summarised, or whose participants changed by at least
  the `Digest:NotableChange` share (default 10 %) since the last summary that held them.
- Every summary has a public copy with the same numbers. Whether an MP stopped the summaries is never shown publicly.
- Timing is corrected by a moderator when people flag a wrong date: correct, hold, or approve one urgent summary.
- **Sending (design 8.19, built 2026-11-16).** `Digest:Sending` `off` (default: dry runs, nothing for MPs in the outbox),
  `approval` (a weekly or before-vote summary waits; an urgent one goes at once) or `automatic`. Two locks keep it off whatever
  the setting says (`SendingRules.State`, `MailLocks`): no introduction letter sent yet, and invented data (`Demo:Allowed`, or any synthetic parliament data: `IMpMailAddresses.HoldsInventedDataAsync`) without
  `Email:Caught` (deployment configuration; false in code and compose, true only in the local `.env`, where Mailpit catches
  everything). Run statuses `dry-run`, `waiting`, `sent`, `cancelled`, `expired`; while sending is on only waiting and sent runs
  count for the ceiling, "changed notably", "covered by Monday" and "never yet in a summary" (`SendingRules.Counts`). A sent run
  marks its records summarised (`IDigestSource.MarkSummarisedAsync`, in the same transaction).
- The moderators' screen `/moderavimas/suvestines` (`DigestModeration`): waiting first, the preview (MPs by name), "Siųsti" (a fresh passkey confirmation, the number of MPs
  in the confirmation; under the summaries' lock, so two presses send once) and "Nesiųsti" (a statement of 20 characters or
  more, public on the copy). A waiting run expires after `Digest:ApprovalHours` or when its sitting's day begins. Recipients are
  worked out when it is let go (`SendingRules.DeliveryStatus`: suppressed, the MP's choice, the letter first, the ceiling); one
  delivery row per run, MP and recipient, never an address. The public copy says what happened in one line, never which MPs.
- The email (`MpMailTemplates`, through `EmailLayout.BuildMp`, the MP stream: `Email:Mp:FromAddress`, optional `Email:Mp:Smtp`)
  holds both sides with the 30-day counts, the MP's own published response or the link to answer, the closing lines, and
  `List-Unsubscribe` with `List-Unsubscribe-Post`. The address comes only from `IMpMailAddresses` and travels only in the outbox
  row. The moderators' preview is the same text.
- The MP's link (8.19.4): `/suvestines/nustatymai/{token}`, server-rendered forms (no script); the token is derived from the
  person number and a generation under `Security:MailHashKey` and stored hashed; "Atsiųsti naują nuorodą" moves to the next
  generation. The one-click stop `POST /api/v1/mp-mail/{token}/stop` is the only write mapped outside the API group's
  same-origin and antiforgery filter (`OneClickStopEndpoint`); `MpMailTests` proves the other token routes still need it. Token
  routes and pages are limited per address and per token (`RateLimits:MpMail`, `RateLimits:MpMailToken`) and answer the same for
  an unknown and a wrong token. Every change is confirmed to the official address. An extra recipient is confirmed from the
  official address (a page with a button, 24 hours, one use) and each copy has its own "Nebegauti kopijų" page. A linked account
  changes the same choice under "Seimo narys" on `/sveiki` (`MyMp`, `GET|PUT /me/mp/mail`, passkey session).
- The introduction letter (`/administravimas/pristatymas`; public `/suvestines/pristatymas`): written and signed by a person,
  a test copy to the administrator's own address, sent with a fresh passkey confirmation to MPs who have had none; a changed
  text is a new version. An MP who had none gets it in place of a summary (`introduced`).
- The suppression list (`/administravimas/nesiunciama`): keyed hashes only, reasons `bounced` (a 5xx refusal, automatic),
  `complaint`, `staff`; the screen lists the sitting MPs on it and counts the rest.
- No address, token or MP's choice in any log line or audit detail: request paths with a link's token are logged as `***`
  (`Lt2.Hosting.SecretPaths`, the Caddyfile's `regexp` filter).

**Feedback.** A balloon on every page (and `/atsiliepimai`) lets members write to the team; an item is linked to the
account only when the member allows a reply, otherwise nothing leads back; staff reply through the outbox; not public.

**Moderation.** Anything that reaches MPs has passed a person. Every action is audited and an argument's author gets the reason.
A report button is on every bill's description and reason. A moderator can restrict an account from writing (reasons,
date flags, statements, responses and, as proposed, changing the display name), until a date or until lifted, with a statement emailed to the person; it never touches sides,
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
names what opens ("Kaip tai veikia"; a bare "Plačiau" only when it is the only one on the screen).
**New screens follow the rule from the start.** `design/text-audit-2026-11-15.md` is the screen-by-screen record.

**A hint (design 8.15): the short answer without leaving the page, the first place for a term or a number.** A term or a
number whose answer fits one or two sentences gets `<Hint Id="…" Text="Kas yra riba?" Answer="…" More="/metodika#…" />`
(`Lt2.Web.Client/Components/Hint.razor`; on server pages write it `<Lt2.Web.Client.Components.Hint …/>`): a dotted-underlined
`<button>` and the browser's own popover, so a press opens it without JavaScript; `js/lt2-hints.js` (every page, listeners
on the document) adds hover and keyboard focus after a delay, the place beside the word inside the screen, Escape back to
the trigger. The answer says what the thing is and does first, then the number; a setting is said as what holds now
("Dabar – 100"), and a bill's own value when it has one. `More` ("Plačiau") only where there is more to say. A longer
explanation, a list of steps or a form stays a `details.more`; how the site works in general stays on `/metodika`; a link
that would leave the page only to explain a term is a hint instead. In a list, one hint beside the list (near its note
line), never one per row. An island never opens a hint from code; a hint is phrasing content and may stand in a paragraph.

- The interface is in **Lithuanian**. New copy is read by a native speaker before it is shown externally.
- **Nothing is worded as new or changed (the owner, 2026-11-16).** Nobody has seen the site yet and everything launches
  at once: what is built during development is not a change for any reader. No text says "dabar", "nuo šiol", "nauja",
  "pasikeitė" or "atnaujinta" about the product, its rules or its pages; write each thing as how it normally is. A text
  about something that really differs from what a person saw before (a moved planned date, a changed answer) is fine.
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
  (`AppealSteps`, `OrganisationSteps`, `MpSteps` and `MpResponseSteps` (take a profile over, office members, code sessions,
  responses, an adoption vote, the responses part without its islands), `MpSendingSteps` (design 8.19: `ResetMpMailAsync`,
  `IntroduceAsync`, settings after a fresh passkey confirmation, the MP mail in the outbox and its links' tokens),
  `ReasonsToComeSteps` with `VilniusToday` and `MondayInWeeks`, `SettingsSteps`,
  `StaffMembers`, `LoggingFactory`). End-to-end tests: `tests/Lt2.E2E/E2ESteps.cs` (registration from any link, ticking the
  age and the terms of use, `Mailpit`, screenshots, API calls from the page, closing a test's organisation, `SqlAsync` against
  the stack's database).
- **Terms of use in tests (design 8.22.2).** Every member made through the shared steps accepts the terms (`TestBrowser`'s
  completion sends `AcceptsTerms`); an account older than them is made by deleting its `terms` consent (integration
  `TermsTests`) or setting an earlier `text_version` (E2E `TermsTests`). A new API write route needs nothing: the sweep in
  integration `TermsTests` sends every mapped write as such an account and expects `terms.required`; put a route on
  `TermsExemptions.Routes` only when the design says the write must work before acceptance.
- **Navigation in end-to-end tests (design 8.21).** "Mano paskyra" and "Atsijungti" are inside the account item: open it with
  `#account-toggle` first; the four tabs are `#project-tabs`; the theme switch is in the footer. The account page's own
  "Atsijungti" (`main`) is unchanged.
- **Staff:** `ModeratorAsync()` / `AdministratorAsync()` of `ApiTest` give the run's shared staff members. Only a test about
  the staff account itself (passkeys, sessions, a reset, a per-moderator limit) makes its own with `StaffMembers.ActiveAsync`.
- **Settings:** change one through the application that reads it (each keeps a short cache) with `SetSettingAsync`, and
  put it back in a `finally` with `ResetSettingAsync` (the design's default) or `WithSettingAsync` (the value before);
  never with a number written into the test.
- **A check that something is absent must be able to fail.** Assert the status of every set-up step and of every read a
  privacy sweep makes; compare an answer's exact set of fields rather than searching a payload that holds random ids for a
  short string; prove the swept answer held what it is about.
- **Every test is independent (owner, 2026-11-16).** It passes alone, with its class alone and in any order of the
  suite; it relies on nothing an earlier test left and leaves nothing a later one could trip on. Run the suites in another
  order to check: `LT2_TEST_ORDER` = `forward`, `reverse`, `random` or `random:SEED` (`tests/Shared/TestOrder.cs`, compiled
  into every test project; xUnit's own order is stable per build and folder, and its `--seed` does not change it), most
  simply through `python tools/run_tests.py unit|architecture|integration|e2e --order reverse` (also `--each-class`, every
  class alone in its own process on the stack as the others left it; `--repeat N`; `--class NAME`). The script keeps every
  failure's full message (TRX report), the order the classes ran in and, for end-to-end runs, what tests could leave on the
  stack before and after the run (`tests/Lt2.E2E/leftovers.sql`): the two must read the same.
- **Shared data:** the integration tests share one database and run one at a time; a test frees the bill it uses
  (`FreeBillAsync`) and keeps to its own summary weeks (`MondayInWeeks`). A test that sends MP mail starts and ends with
  `ResetMpMailAsync` and puts `Digest:Sending` back: the letter, the MPs' choices, the links and the suppression list are
  shared by the run. A shared staff member (`ModeratorAsync()`, `AdministratorAsync()`) comes with a fresh passkey
  confirmation, so a setting can be saved whatever ran before. An MP's official address is shared by every test that uses
  the MP: a test reads only the notices sent after it started (`MpNoticesAsync(address, before)`). The shared factory's
  per-address request limits are out of reach (`Lt2Factory`), so no test depends on how many requests came before it.
- **End-to-end tests clean up after themselves; nothing outside the repository prepares or repairs the stack.** The
  classes share one stack and run one at a time. Every class derives from `E2ETest`: after each test, passed or failed,
  the accounts it registered through the shared steps (`E2ESteps.RegisterHereAsync`, `PasskeyTests.RegisterAsync`) are
  deleted through the product (`DELETE /api/v1/me`, from the session the browser ended with, else by a code sign-in;
  `TestAccounts`), and first what the product keeps on purpose and a test must not leave: moderators' actions and reports on
  their arguments, the organisations they registered (`E2EData`). A staff fixture forgets its moderators' actions and deletes
  its account from its staff passkey session when the class is over. The run makes its own invitation through the migrator
  and deletes it at the end (`E2ERun`, an assembly fixture; `LT2_E2E_INVITATION` names an existing code instead, for a
  stack whose migrator cannot be run), and waits for the web host before the first test. A test that changes a
  demonstration bill brings it to a known state before and puts it back in a `finally` (`E2EData.RestoreDescriptionAsync`;
  a summary it made, `DeleteSummaryAsync`); something the product keeps unlinked from the account (feedback without a reply
  address) carries a marker and is removed by it. Every name a run gives an organisation carries "(E2E)" and every
  description "(E2E bandymas)". `MpSendingTests` deletes everything of MP mail it made (runs, deliveries, choices, links,
  suppression rows, letters). When a test fails, the last screen of every browser it used and of the class's staff pages
  is kept in `output/failures/` beside the test assembly.
- **Settings in end-to-end tests** are changed and put back through the settings screen
  (`AdministratorFixture.ChangeSettingAsync`, `RestoreSettingAsync` in a `finally`), never by SQL: the site drops its cached
  copy (30 s) only for a change made through it, so a SQL change leaves the next test to the cache's timing. The worker
  keeps its own copy for 30 s either way; a test that needs the worker to see a value waits for the result (an email),
  longer than that.
- **Days are Vilnius calendar days.** "Yesterday" in a test is noon of the Vilnius day before, never `now - 24 hours` (two
  days back on the morning after the clocks go forward); a job that decides by the day is run at a time the test chose
  from the day it started on, not at the real time.
- **Known browsers (design 8.20.2).** A `TestBrowser` that completed a registration or signed in to a member is known to that
  account (the test factory sets `Security:DeviceHashKey`): its later code requests use the known browser's allowance and
  codes, not the shared ones. A test about the shared limits asks from a new browser (`NewBrowser()`, `CodeSessionAsync`).
- **The "Seimo narių atsakymai" email (design 8.20.1)** goes the morning after: a test sets `Follows:ResponsesAt` to 00:00
  (and back), moves the bill's `announced_at` to yesterday and runs the job at the real time (`ResponseNoticeTests`); the
  end-to-end step in `MpResponseTests` moves the response by SQL, sets the setting through the settings screen and puts it
  back, and removes the bill's `bill_event` rows of kind `response`.
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
