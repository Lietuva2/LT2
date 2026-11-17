# Where the code is

Read when you need to find your way around the solution. [AGENTS.md](../AGENTS.md) has the rules; the design is in [design/](../design/README.md).

```
design/                    the agreed design, one file per section (README.md is the index); ui/ the interface
                           prototype (tokens, sample pages)
agents/                    working notes read on demand: this map, interface, features, security, testing, Seimas data
src/Lt2.Web                host: API endpoints in Endpoints/<Module>/ (one per file), Blazor server rendering,
                           auth, security middleware (forwarding headers believed only from ReverseProxy:Address,
                           Security/TrustedProxy.cs; no-store on pages answered to a signed-in session; Security/TermsGate.cs,
                           design 8.22.2; Security/BrowserStorage.cs, the inventory of 8.22.1), the layout (Components/Layout:
                           the menu, AccountMenu with js/lt2-account.js, the footer) and the terms and privacy pages
                           (Components/Pages/Terms, Privacy, LegalText.resx)
src/Lt2.Hosting            what the web host and the worker set up the same way: Serilog and OpenTelemetry
                           (Observability.AddLt2Observability), with SecretPaths replacing an MP link's or a member's stop
                           link's token in every log line and trace; no module references it
src/Lt2.Web.Client         Blazor WebAssembly components and HTTP implementations of the API clients (logging at Warning
                           and above only, owner 2026-11-17: no console line per request); the interface's
                           shared pieces: Components/InteractiveBase<TText>, Display (numbers and dates, Vilnius),
                           StringLocalizerExtensions (Format), CopyLink, FieldError, Messages, SplitBar, Hint (the short answer in place, js/lt2-hints.js),
                           ProjectTabs and MenuSections (design 8.21), MoreMenu and Icon (the action row, design 8.16.1,
                           js/lt2-menus.js), Appeals/ModerationMenu with ModerationChoice, and ActionHeading (a moderator's
                           main action and "Kiti veiksmai", design 8.3), BrowserKeys (the storage keys, design 8.22.1),
                           TypedText (a typed text's paragraphs and lists, design 8.2) and LongText (a long member's text with "Rodyti daugiau", laid out before it is cut), TextLayoutHint (the list hint under a writing box), the joined-button choice (CSS `.switch`
                           over real radios, `.switch__half--for|against|neutral|plain`, `.switch-field` + `.switch--stack`
                           for three options: a bill's sides and a statement's stance only; other choices stay radios and
                           consents plain checkboxes, design 8.16), PasskeyStep ("Patvirtinti prieigos raktu" in
                           place, design 6.2) and MyAccountState (the browser's one GET /me: whether the caller is an MP's
                           own account, design 8.5.4)
src/Lt2.Api.Contracts      DTOs, routes, client interfaces, validators (shared by server, client and tests)
src/Lt2.SharedKernel       Result type, identifiers (UUID v7), outbox, audit and settings interfaces, connection
                           strings and the one EF Core set-up (Persistence/: UseLt2Npgsql), VilniusTime (the
                           Europe/Vilnius zone and its days), the signed-in account (ClaimsPrincipal.UserId()), the
                           staff actor (StaffActor) and staff statements (StaffStatements), the outbound allowlist
                           (Http/: hosts from Outbound:AllowedHosts plus AddOutboundHost; redirects followed only within
                           it), TextExcerpt and TextLayout (also compiled into Lt2.Web.Client), BillTextLinks (every link to a comment or an
                           argument, also compiled into Lt2.Api.Contracts); no clock of its own (TimeProvider is used)
src/Modules/               Lt2.Identity, Lt2.Notifications, Lt2.Audit, Lt2.Parliament (Seimas open-data client,
                           import, statistics); Lt2.Appeals (bills' records, positions, arguments, endorsements,
                           reports, moderation, rule-based pre-screening, flags, summaries content, descriptions written
                           by moderators, the AI draft in Ai/ with its public prompt file, the AI summary job
                           (Application/DescriptionSummaries.cs), the "unavailable" IBillTextSource, and organisations
                           with their statements on bills, design 8.14.14; MPs' responses, 8.5.5). Notifications
                           also owns the summaries (Digests/: preparing, review, sending, ChoiceRules, SendingRules), MP mail (MpMail/:
                           the links' tokens, the MPs' choices, the introduction letter, the suppression list, MpSender)
                           and following (Follows/: follows, email switches, bill
                           events, the follow kinds; Rhythm/: how often a member's emails come, design 8.24). Modules talk through interfaces in SharedKernel (IParticipants, IBillCatalogue,
                           IPlannedAgenda, IMpDirectory, IMpMailAddresses (the one way an official address leaves
                           Parliament, for sending), IDigestSource, IPartySupporters, IAccountData (one per module),
                           IPositionErasure, IOutbox, IAuditLog, ISettings, IAiAssistant, IBillTextSource,
                           IOrganisationDirectory, IOrganisationCodeSwitch, IMpLinks, IMpResponseFeed,
                           IWritingRestrictions, IStaffAddresses). The host
                           composes the agenda (GET /api/v1/agenda) and /me/follows from the modules' interfaces.
                           Identity also owns MPs' links (Application/Politicians: politician_link, politician_token,
                           design 8.5); "whose official address is this" is IMpDirectory.OfficialAddressOwnerAsync
                           (Parliament's OfficialAddresses, the one definition), and the MP's own account is refused as a
                           participant through MpAccounts.Refusal (SharedKernel) wherever a participant is checked.
                           Identity also owns known browsers (Application/SignIn/KnownBrowsers.cs, design 8.20.2: the
                           __Host-lt2.device cookie, identity.known_device, the code allowance they choose).
src/Modules/Lt2.Usage      the site's own usage statistics (design 8.27): UsageRules and Sketch (HyperLogLog) pure,
                           UsageCounter in the web host's memory, written every minute; the host feeds it from
                           Services/UsageServices.cs; the screen /administravimas/lankomumas (UsageAdmin; its day bars are
                           DayBars, own CSS size, a label per column on hover and focus, no chart library)
src/Lt2.Worker             Hangfire server: outbox dispatch loop, the parliament import, the agenda watcher (lrs.lt
                           pages; off unless Agenda:BaseAddress is set), the daily aggregates and closing of the bills' records, summaries
                           (every 15 minutes: prepared Digest:ReviewHours before their time, moderators emailed, then
                           gone at their time: a dry run or sent by Digest:Sending), the follow emails (every 15 minutes, in each member's rhythm: at once, the morning from Follows:ResponsesAt, or Monday), the AI summaries
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
tools/split_design.py       splits a pre-split copy of the old design/system-design.md into the section files (for a
                            branch made before 2026-11-17)
tools/Lt2.SeimasStandIn     local and test only: stand-in for the Seimas open data and the lrs.lt agenda page (/darbotvarke),
                           invented answers; never shipped. Recent/ (container only, never in tests) adds a sitting and
                           a stage vote dated two days before today, so the local agenda has recent votes, and an adoption
                           vote nine days before today on XVP-0707, for the analytics page
```

## Shared conventions

- Times are UTC in the database; schedules and display use `Europe/Vilnius`. Use `TimeProvider`, never `DateTime.Now`.
  Vilnius days come from `VilniusTime` in SharedKernel (`Zone`, `DateOf`, `Today`, `StartOf`); do not look the zone up again.
- The signed-in account is `principal.UserId()` (SharedKernel); a staff member acting is a `StaffActor`. A module's
  `DbContext` is configured with `UseLt2Npgsql` (SharedKernel/Persistence), nowhere by hand.
- Limits that count rows (codes per address and per known browser, known browsers per account, one current invitation
  code, one urgent summary a week, follows per account) are counted under a PostgreSQL advisory lock in the same
  transaction (design 6.8); Identity's are in `IdentityLocks`, Appeals' in `AdvisoryLocks`.
- Rules are checked where the data is written (design 6.8), not only by the HTTP validators: a module service enforces
  them for every caller. A moderator's statement goes through `StatementRules` in Appeals and `StaffStatements` elsewhere
  (the same rule); a moderator's action endpoint runs through
  `ModeratorEndpoint.ActAsync` (`src/Lt2.Web/Endpoints/Moderation`).
