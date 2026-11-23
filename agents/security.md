# Security and privacy in the code

Read before touching sign-in, sessions, cookies, codes and tokens, or anything that stores or shows a person's data. The short rules are in [AGENTS.md](../AGENTS.md); the design is 6 and 6.2 ([design/README.md](../design/README.md)).

A person's side on a bill is a political opinion: special-category data under GDPR. These rules are not negotiable in
code review.

- No endpoint, page, email, log line or admin screen reveals another person's position. Administrators included.
- Do not store per-person history of changed positions. Trends come from daily aggregate counts.
- Logs carry no emails and no positions. IPs are hashed.
- Group numbers (the supporters of the MP's party on a side, in a summary) are shown only for groups of at least the
  runtime setting `Digest:PartyGroupMinimum` (owner, 2026-11-17: default 10; 0 = no floor, any group of one or more; an
  empty group never), and only when the rest of that side is none or a group as large (`GroupRules`, Appeals). The value
  is read when a summary is made; `/metodika` ("Grupių skaičiai", `#groups`) and the party form state it as it holds now,
  the privacy notice points there. It is not extended to organisations, follower numbers or the analytics page's sample.
- Sign-in has no passwords: an emailed one-time code or a passkey. A staff role works only in a session signed in
  with a staff passkey; a code session is a member session, whatever roles the account has. Politicians need a
  passkey too: speaking for an MP needs a passkey session, read from the database per request (the link is not a role in
  the session), and on a linked account (the MP's or an office member's) a passkey is added only in a passkey session or
  by the take-over link. Responses never reveal whether an email is registered. Changing roles, resetting an account, saving
  a runtime setting, ending an MP's link, sending the introduction letter and approving an urgent summary need a fresh
  passkey confirmation (`FreshConfirmation`, the last ten minutes, checked by the endpoint and the in-process client alike;
  `passkey.confirmation-required`); a staff account is
  deleted only from its staff passkey session; what a staff member did keeps their account id after deletion (design 6.9).
  A code session on a staff account becomes a staff session without signing out (owner, 2026-11-16): `/prisijungti/raktas`,
  `POST /me/staff-session` (`StaffHandlers.StartStaffSessionAsync`), the signed-in account's own staff passkey only
  (anything else `passkey.staff-required`, nothing changed), a new session key (`Infrastructure/SessionRotation`: the old
  row is deleted, never renewed), a fresh confirmation, audited as `staff.session-started` without an email.
- **A session is what it was signed in as (design 6.2, owner 2026-11-17).** Every sign-in (code, passkey, the take-over, the
  office acceptance's passkey, a passkey or staff session started in place) goes through `SessionSignIn.SignInAsync`
  (Identity/Infrastructure): a new key by `SessionRotation`, the browser's session before it ended as by signing out; its
  answer `SignInResponse.AccountChanged` says when that was another account (the endpoints then forget the other account's
  analytics copy) and `SignInFlow` shows "Prisijungėte kaip kitas narys: {vardas}." with "Tęsti". `SessionTicketStore.RenewAsync`
  only renews (ticket, expiry, last seen): it never changes a row's account (it throws), method or passkey. The security
  stamp's refresh keeps the site's own claims (`SessionClaims`: `amr`, `lt2:passkey`, the session key) through
  `SecurityStampValidatorOptions.OnRefreshingPrincipal`; a changed stamp still signs out (`SessionRefreshTests`, on a factory
  that checks the stamp on every request). Where an action needs a passkey session and the session was signed in by a code,
  the island shows `<PasskeyStep>` ("Šiam veiksmui reikia prisijungimo prieigos raktu.", "Patvirtinti prieigos raktu"):
  `POST /me/passkey-session` (`StaffHandlers.StartPasskeySessionAsync`) with any passkey of the signed-in account (another
  account's: `passkey.other-account`, nothing changed), audited `passkey.session-started` (`staff.session-started` with the
  staff passkey); `Staff="true"` uses `/me/staff-session` and shows only to a staff account outside a staff session, under
  every staff screen's refusal and on the account screens.
- **Sign-in codes (design 6.2, 8.20.2).** Browsers the account does not know share five codes an hour and ten a day per
  address (`SignIn:CodesPerHour`, `SignIn:CodesPerDay`); a browser known to the account (it signed in to it, or finished the
  registration there) has its own five an hour and ten a day, the same two settings counted separately
  (`KnownBrowserRules`), and a code it asked for (`sign_in_code.known_device_id`) is accepted only from it: requests and wrong guesses elsewhere neither use it up nor cancel
  it. The rules are pure functions (`SignInCodeRules`, `KnownBrowserRules.CodesToTry`); the answers to a code request and a
  wrong code are the same whether the address has an account and whether the browser is known. The account page lists
  "Žinomos naršyklės" beside the sessions with "Pamiršti" (`GET|DELETE /me/known-browsers`), at most 20 per account, deleted
  after a year unseen, exported and deleted with the account; `identity.known_device` is withheld from `lt2_readonly`.
- **Bot check (design 6.2, 6.4).** A proof of work in the ALTCHA protocol, written here (`BotCheckRules`, pure: challenge,
  `Check`, expiry from the salt's `?expires=`; `Application/BotCheck.cs`: the key, issuing, the single-use insert). Registration
  and a code request (the resend too) carry it in `botCheck` of the body, checked first in `RegisterHandler` and
  `RequestCodeHandler` (so for every caller), before anything about the address; one answer, 400 `bot-check.failed`. Not on
  sign-in by code or passkey, nor on the email change (a signed-in account, its own limits). `GET /api/v1/bot-check/challenge`
  is no-store, per client address (`RateLimitPolicies.BotChallenge`), 204 while `BotCheck:Enabled` is off. The key is the
  secret `bot_check_key` (`Security:BotCheckKey`, web only; absent: a key of the instance, warned at start). A used challenge
  is `identity.bot_check_use` (primary key = the signature, until the challenge's expiry; withheld from `lt2_readonly`),
  deleted by `MaintenanceHandler`. The script `js/lt2-botcheck.js` keeps nothing in the browser and loads nothing from
  elsewhere (`BotCheckScriptTests`). Logs say only the outcome name, never the solution.
- **A locked account (design 8.3, owner 2026-11-19).** An administrator's full lock (`POST /admin/accounts/lock`, a staff-passkey
  session with a fresh confirmation, a statement) ends every session and the open codes in the same transaction and sets
  `identity.profile.locked_at`. No sign-in then works, and no answer differs from that for an address nobody holds: the code request
  answers 202 and sends nothing (`SignInCodeIssuer`), a typed code is `auth.code-invalid`, a passkey `passkey.invalid`; the public profile
  is 410 as if switched off; follow emails are skipped (`ParticipantFacts.IsLocked`). A staff account holding an active role and the
  administrator's own account are never locked. The audit entry and the notice email hold the statement and no address.
  The lock is also reached from an item its person wrote (`POST /admin/accounts/lock-author`, `{targetKind, targetId, statement}`:
  argument, comment, organisation's statement, reported profile): the owning module resolves the author (`IAuthorLookup`, Appeals; a
  profile's moderation id is the account id the moderators' list already carries) and only the id goes to Identity
  (`LockAccountByIdAsync`), so no address is typed or sent. `GET /admin/accounts/locked` and `POST /admin/accounts/{id}/unlock` serve
  the list (id, name, time, statement from `identity.profile.locked_statement`; never an address). All of it is `Policies.Administrator`
  with the fresh confirmation; a moderator gets 403 and the menus do not offer it (`MyAccountState.IsAdministratorAsync`, a convenience only).
- **Changing the email address (design 6.2).** Only a passkey session or a code to the address in use starts it (a staff
  account: its staff passkey, freshly confirmed; the MP's account: a passkey session; `EmailChangeRules.Proof`); the answer to an
  address is the same whether an account has it (that account is told instead, no code); the code to the new address follows
  the sign-in code's rules and limits, counted per new address under `IdentityLocks.EmailAddressAsync`; the change ends the
  other sessions and the open codes, changes the security stamp and re-keys this session; the old address's link back
  (`POST /me/email/restore`, no session, on `TermsExemptions.Routes`, `RateLimitPolicies.OneTimeCode`) ends every session. The
  link's row keeps the old address for its seven days only; audits and logs hold no address.
- The read-only database role reads only the tables on `DatabaseRoles.ReadableTables`; a new table goes on that list
  or on `DatabaseRoles.WithheldTables` in the same change (an architecture test fails otherwise).
- Session cookie only (`__Host-`, Secure, HttpOnly, SameSite=Lax). No tokens in the browser. The one other lasting cookie is a
  known browser's `__Host-lt2.device` (design 8.20.2, the same attributes, a year): it never authenticates, lengthens a session
  or skips a code; it only chooses which code allowance and which codes apply, and it is stored only as a keyed hash
  (`Security:DeviceHashKey`, the secret `device_hash_key`). Every state-changing
  call needs the antiforgery header and passes the Origin check, except the one-click stops that mail programs send: an MP's
  emails (design 8.19.4) and a member's notice emails (design 8.24.1). CORS stays off.
- **Nothing non-essential in the browser (design 8.22.1), so no cookie banner.** No third-party analytics, no third-party script, font or
  embed, no advertising or measurement cookie. What the site keeps is one list, `Security/BrowserStorage.Inventory` (the
  session, the passkey step, the request-forgery and the known-browser cookies; `lt2-theme` in local storage once a theme is
  chosen; the framework's `blazor-resource-hash:Lt2.Web.Client` in local storage (`BrowserKeys.FrameworkResourceHash`);
  `lt2.return-path` and `lt2.invitation-code` in session storage, `Lt2.Web.Client/BrowserKeys`; `lt2.invitation-code` is written by
  `SignInFlow` and by `js/lt2-invitation.js`, which takes a link's `#kodas=` on any page, design 8.11.5). The privacy notice's
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
  a share line's `/seimas/{number}#kodas=…`, a representative's `/organizacijos/kvietimas#…`, an MP's take-over link
  `/seimo-narys/perimti#…`, an office member's `/seimo-narys/biuras#…`, the old address's link back after the account's
  address changed `/paskyra/el-pastas/grazinti#…`, design 6.2); the page reads it and sends it in a request body
  (design 6.8). The old `?kodas=` form is not read any more (owner, 2026-11-17: no old links). A link preview's `og:url` is the
  page's path alone, never a query or a fragment (design 8.22.5).
  The secret of those last four links is a typeable one-time code (owner, 2026-11-17: `OneTimeCode` in SharedKernel, also
  compiled into Lt2.Web.Client: twelve characters of the invitation alphabet in three groups, stored only as `OneTimeCode.Hash`
  of its one form, one use, the link's lifetime): the email gives it under the link with the page to open, a representative sees
  it beside the copied link, and the page opened without a `#` asks for it (`<CodeEntry>`, which puts it after the page's own `#`).
  Every request presenting one is limited per client address (`RateLimitPolicies.OneTimeCode`, `RateLimits:OneTimeCode`); an
  unknown, malformed, used or expired code gets the link's one answer.
  Two exceptions the design names carry their token in the path, because the one-click stop a mail program sends needs it in
  the address: the MP's mail links (8.19.4) and a member's stop link (8.24.1, `/laiskai/nebegauti/{token}` and
  `POST /api/v1/notices/{token}/stop`). Their paths are logged with the token replaced (`Lt2.Hosting.SecretPaths`, the
  Caddyfile's filter), and their pages are `noindex` and `no-store` (`SecurityHeaders`). The account, staff, sign-in, MP
  and one-time-link pages are `noindex, nofollow` too (`SecurityHeaders.IsPrivatePage`, design 8.22.5) and carry no link
  preview.
- **The Hangfire dashboard (design 3, 6.3; owner 2026-11-18)**, `/administravimas/darbai` ("Foniniai darbai" in the account
  item, for administrators), `Lt2.Web/Jobs/JobsDashboard.cs`. Only `Policies.Administrator` (a staff-passkey session): the
  endpoint requires it and `AdministratorDashboardFilter` (Hangfire's `IDashboardAsyncAuthorizationFilter`) evaluates it again;
  Hangfire's default local-requests filter is removed, not kept beside it, and `IsReadOnlyFunc` is false only for a request
  that passed the filter. A visitor is sent to sign in, anyone else away; the dashboard is excluded from the "nerasta" status
  pages, so a refused form is a bare 403. Every POST passes the Origin check and `IAntiforgery.IsRequestValidAsync` in the
  filter (Hangfire then checks the token once more; it puts the token in the page's `csrf-token` meta, under our header name)
  and, except the counters' poll (`/stats`), is audited as `jobs.dashboard-action` with the actor, role and IP hash and no
  details. CSP there only: `style-src-attr 'unsafe-inline'` (state colours in `style` attributes) and `script-src 'self'`
  (`SecurityHeaders.JobsDashboardContentSecurityPolicy`; the `<style>` its Chart.js injects stays blocked, the graphs draw without it); every response under it `no-store` and `noindex`. The storage is the
  worker's `hangfire` schema through the web host's `NpgsqlDataSource` (no second pool, `PrepareSchemaIfNecessary` off, the
  connection string not shown); `lt2_app` has DML there and no `CREATE`, `lt2_readonly` nothing. Jobs take only a
  `CancellationToken` (stored as null), log counts only, and throw messages with kinds and codes, never an address; Npgsql
  redacts error detail and EF Core logs no sensitive data, so a failed run's stored exception holds no address or token. Emails
  (where addresses and SMTP errors are) are sent by the outbox loop, not by Hangfire.
- Forwarding headers count only from the proxy named in `ReverseProxy:Address`. A page answered to a signed-in session is
  `no-store`.
