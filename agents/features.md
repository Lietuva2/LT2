# Where each feature lives in the code

Read the part for the feature you change, after its design file ([design/README.md](../design/README.md) maps section numbers to files). The design is the rule; this file says how the code carries it out (class, route and test names) and records the owner's decisions as built.

**What is built.**

| Part | What |
|---|---|
| **Foundation** | Solution skeleton, passwordless sign-in, security plumbing, deployment, monitoring. |
| **MP profiles** | Every MP of the current term imported from Seimas open data: mandate, faction, committees, contacts, voting record, statistics (design 7). |
| **Bills and what people ask** | Every active bill open at once, one page per bill, no proposals, requests or approval: people take a side and give arguments; lists and agenda on participation; moderation after the fact (design 8.12). Descriptions with their labels and the AI summary job (8.13). |
| **Reasons to come** | The agenda page (`/darbotvarke`) and the front page built on it, following bills and MPs with emails, the AI draft of a description (Claude API, off until configured), the share line (design 8.11). |
| **Profiles** | The account area as four screens; public profiles by the member's choice, "Apie mane", the name's card, following a member, follower numbers on every followed thing's own page (design 8.21, 8.25, 8.11.2); the supported party on the public page by its own box and consent (8.25.9). |
| **Email rhythm** | How often a member's emails come, one delivery for every member email kind, the gathered email (design 8.24). |
| **Comments** | "Aptarimas" under arguments, statements and responses, "Sutinku", moderation, the argument author's notice in the rhythm; names link to public profiles (design 8.23, 8.25). |
| **Summaries to MPs** | The MP's link (recognised at once, the take-over, the office) and the response (design 8.5); sending (8.19): `Digest:Sending`, review a day ahead, the real email, preferences without an account, the introduction letter, the suppression list; off by default. The followers' email about MPs' answers and known browsers for sign-in (8.20). |

Voting with delegation is not designed; do not build towards it.

**Emails.**
- Every email is built by `EmailLayout` (Notifications/Templates) and rendered from its outbox kind by one
  `EmailRenderer<T>` registered in `NotificationsModule`; shared texts use the `Email.*` keys of `EmailTemplates.resx`.
  `Build` and `BuildMp` take the footer (`EmailLayout.Footer(l, site.Value.Address)`: the privacy notice's full address,
  design 8.22.3), so no email goes without it. In `Build`'s HTML part (owner, 2026-11-17) a line ending "text: https://…" is that
  text (its last sentence) as a bold green link (in the footer, every link the footer's grey, underlined: `color:#64748b;text-decoration:underline`, the MP footer too), the address not printed (owner, 2026-11-17: noise), any other address a plain link that may break anywhere
  (`EmailLayout.LinkedHtml`). Every HTML part is one shell (`EmailLayout.Shell`: a centred column of at most 36rem, the
  wordmark as text over a thin rule, the footer under a rule; inline styles, no image). `BuildMp` takes `EmailBlock`s (text
  plus optional HTML of its own: `TypedHtml` for the letter, `SignatureHtml`, a bill's card, `SystemHtml` for the
  takeover button and the quiet preferences link); `MpMailTemplates.Closing` builds them, `Digest.TakeoverLead` and
  `Digest.TakeoverButton` are the button's texts, the text part keeps `Digest.Takeover` with its address; `EmailLookTests`
  also writes samples to `output/email-samples/`; a template writes an action line with `EmailLayout.LinkLine(label, url)`; the plain part is unchanged
  and nothing is rewritten or tracked. An email of the member's rhythm is built by `EmailLayout.BuildNotice` (design 8.24.1):
  its footer holds the reason (`*.Reason` keys, no link), "Keisti periodiškumą arba atsisakyti laiškų" (`Email.Notices.*`;
  plain part "Keisti periodiškumą: … · Atsisakyti laiškų: …") and the privacy line, with the stop headers.
  Deleting an account or purging a pending one calls `IOutbox.ForgetAsync` in the same transaction.
- **Three streams (design 8.19.3, owner 2026-11-19).** The stream of an outbox kind is chosen in one table, `EmailStreams`
  (Notifications/Outbox), and the dispatcher stamps it on the message (`EmailMessage.Stream`); `EmailStreamTests` fails until a
  new kind is in the table. **`codes`** (sender `Email:Smtp:FromAddress`, account `Email:Smtp`): `email.sign-in-code`,
  `email.account-notice` (passkey added, role granted or activated, account reset, MP link and office changes),
  `email.email-change-code`, `-taken`, `email.email-changed`, `email.email-restored`, `email.organisation-code`,
  `email.mp-takeover-link`. **`notices`** (`Email:Notices:FromAddress`, `Email:Notices:Smtp` when its host is set, else the common
  account; secret `notices_smtp_password`): every other `email.*` kind (follow, gathered, comment, moderation, report-decided,
  writing-restriction, feedback reply and notice, organisation notice, profile notice, MP-response notice, office
  invitation, digest-review). **`mp`**: the three `email.mp-*` kinds (`Email:Mp`). Each stream has a daily ceiling, the
  settings `Email:CeilingCodesPerDay`, `Email:CeilingNoticesPerDay`, `Email:CeilingMpPerDay` (0 = none, group "Laiškų siuntimo
  ribos" on the settings screen), the rule `StreamCeilings.Remaining`; the dispatcher counts `notify.stream_send` (one row
  per email sent, purged after two days by the daily clean-up) under an advisory lock and leaves a stream at its ceiling out
  of the batch (`kind <> ALL(held)`), its rows untouched. `SmtpRefusals.Classify` (Notifications/Transports) says whether a
  failed send is about the recipient's `Mailbox`, the `Sender` (a failed login, a reply to `MAIL FROM`, enhanced 4.7.x or
  5.7.x, reply text about a limit, quota or rate) or `Other`: only `Mailbox` on an MP kind suppresses; `Sender` leaves the row
  due and untried and pauses that stream for `SmtpRefusals.StreamPause` (one hour, in the worker's memory) while the others
  go on. Tests: `EmailStreamTests` (unit), `EmailStreamDispatchTests` (integration, fake transport).
- **Demonstration emails (owner, 2026-11-20).** `migrator --demo-emails` (`DemoEmails`, Notifications/Outbox) queues one email of every
  outbox kind in `EmailStreams`, and of each variant a kind has, from invented data to `NAME@pavyzdys.test`, written with the real
  payload types and `OutboxMessage.Create` through `OutboxWriter`, so the worker renders them with the real renderers and Mailpit
  lists them by name. Refuses unless `Demo:Allowed` and `Email:Caught` are both true (the migrator gets `Email__Caught` from
  `LT2_MAIL_CAUGHT`); writes outbox rows only. `DemoEmailTests` fails for a kind without a demonstration, renders every one and
  checks no bare long address, every linked address also in the plain part, no placeholder left. How to look: `agents/testing.md`.


**Navigation (design 8.21, built 2026-11-16; owner, 2026-11-17, 2026-11-19; 8.28.2).** **One list,** `MenuSections.Items`
(`Lt2.Web.Client/Components`), holds every item of the menu and the footer: its address, its `MenuPlace` (the dropdown of
"Projektai", the menu's line, the account item, the footer's "Apie" and "Dalyvauti"), and its texts in `UiText`:
`Menu.{Key}` the name and `Menu.{Key}.Line` the one line that says what the page is. The dropdown, the menu's line, the tab
row, the footer and "Kur ką rasti" on `/metodika` (`Components/Parliament/WhereToFind`, `#where-to-find`, 8.28.4) are drawn
from it; element ids are `nav-{Slug}`, `{Slug}` (tab row), `foot-{Slug}`, `where-{Slug}`. Unit `MenuTests` fails for an item
without a name or a line. Each dropdown entry shows its line under its name (`.drop__label`, `.drop__desc`; `.drop--desc`, the
panel as wide as the page under 32rem); the menu's line, the tab row and the footer show names only. The menu is the logo, "Projektai", "Seimo nariai", "Statistika"
(`/statistika`, design 8.26) and "Prisijungti" or the account item "Paskyra"; one line from 360 px (`MenuTests`, E2E). "Projektai" is
one item `div.drop#projects-menu` (owner, 2026-11-19): where the device can hover (`@media (hover: hover) and (pointer: fine)`) the
link `#nav-projects` to `/darbotvarke` and the panel `#projects-panel` on `:hover` and `:has(:focus-visible)` (Escape sets
`data-dismissed`, `js/lt2-account.js`); on a touch screen the `<details>` `#projects-touch` (`#nav-projects-touch`) whose open
state shows the panel beside it (`.drop__touch[open] ~ .drop__panel`), closed by `js/lt2-account.js` like the account item.
The panel holds its four tabs (links `#nav-tab-…` with their lines) and is current (`aria-current` on its summary) on them and everything under them (`MenuSections`); the four tabs ("Darbotvarkė",
"Projektai", "Balsavimai", "Suvestinės") are also `<ProjectTabs>` (`#tab-…`) under the heading of `/darbotvarke`, `/projektai`,
`/seimas/balsavimai`, `/suvestines`. "Balsavimai" is a tab again (owner, 2026-11-19; out from 2026-11-16); the link under the
agenda's voted list to it stays. A page under a tab (a vote, a summary's date page) has no tab row and no marked dropdown item,
only the current menu item and its breadcrumb. The
account item (`Layout/AccountMenu`) is a `<details>` with "Mano paskyra" and "Atsijungti" (the server-rendered sign-out form);
`js/lt2-account.js` fills the name and the account's links (staff screens, an MP's projects, accepting changed terms; for a
staff account whose session is not a staff session, "Prisijungti darbuotojo prieigos raktu" to `StaffSessionPage` with the
page to come back to after the `#`) from `GET /me` when it opens and closes it on Escape, a press outside and focus
leaving. **No served page holds a member's name or role links**: the header is the same for every member apart from the antiforgery token (`NavigationTests`). The footer, on
every page (token pages from emails included), holds "Naršyti" (the menu's sections in its order, `MenuSections.Browse`, ids `foot-tab-…`, `foot-mps`, `foot-statistics`,
nothing marked current; owner, 2026-11-19: no scrolling back to the top; the heading awaits a native read), "Apie"
(Metodika, Moderavimo taisyklės, Naudojimo taisyklės, Privatumas), "Dalyvauti" (Organizacijos, Atsiliepimai), the theme
switch and the trial line. `/metodika` is called "Metodika".

**Terms of use (design 8.22.2).** `/taisykles` and `/privatumas` are static pages from `LegalText.resx` (drafts for the lawyer;
"[bus įrašyta]" marks what the owner has not given). Registration has the required box "Sutinku su naudojimo taisyklėmis"
(`CompleteRegistrationRequest.AcceptsTerms`, checked by the validator and again in `CompleteRegistrationHandler`); the accepted
version is a consent of kind `terms` (`TermsOfUse.Version`, SharedKernel; the client's `TermsText.Version` is the same). A changed
text gets a new, later version: every member is asked again. A member whose accepted version is missing or behind
(`TermsOfUse.NeedsAcceptance`; `GET /me` says `termsRequired`) is sent to `/taisykles/sutikimas` after signing in and by
any island refused with `terms.required` (`InteractiveBase`); until they accept, reads work and every API write is refused in
one place, `Security/TermsGate` (before authorization, so a new write route is covered by default; `TermsTests` sweeps every
mapped write), except `TermsExemptions.Routes` (accepting, signing out, deleting the account, the MP mail token routes, a
member's stop link `POST /notices/{token}/stop`, the old address's link back `POST /me/email/restore` (design 6.2), and
(owner, 2026-11-17) withdrawing a consent: `DELETE /me/profile/public`, `DELETE /me/consents/positions`,
`DELETE /me/party-support`, `DELETE /me/party-support/public`; giving one again stays refused, and so does the "Profilis"
form, `PUT /me/profile`, even when it only unticks a box). The export is a read. The version stays 2026-11-17 until the
launch although the text changed while building (owner, 2026-11-17): nobody accepted an earlier one. The server-rendered feedback form asks the same question. Pending accounts accept with their registration;
the invented demonstration members get the consent like their others (`--demo-appeal`, repeatable).

**Bot check (design 6.2, 6.4).** `GET /api/v1/bot-check/challenge` (`BotChallengeEndpoint`, `ApiRoutes.BotCheck.Challenge`,
`BotChallengeResponse` in `Lt2.Api.Contracts/Auth`) and `botCheck` on `RegisterRequest` and `RequestCodeRequest`, which
`RegisterHandler` and `RequestCodeHandler` check first through `BotCheck.CheckAsync` (`Identity/Application/BotCheck.cs`,
rules in `Domain/BotCheckRules.cs`); `IIdentityModule.RequestSignInCodeAsync(email, botCheck)` and `NewBotChallengeAsync`. The
island: `SignInFlow` calls `BotCheckBrowser.PrepareAsync` when first shown (interactive, not signed in), takes the
solution with `SendCheckedAsync` for registering, asking for a code and asking again, retries once on `bot-check.failed`
with a new solution, shows `BotCheck.Working` only after 400 ms, and prepares the next solution after each request; the script
is `Lt2.Web.Client/wwwroot/js/lt2-botcheck.js` (`prepare`, `take`; no worker, no storage; `crypto.subtle` in batches of 256).
Settings `BotCheck:Enabled` / `MaxNumber` / `ChallengeMinutes` in "Saugumo ribos". Tests: `BotCheckRuleTests` (unit),
`BotCheckTests` (HTTP), the island's in `SignInComponentTests`, `BotCheckScriptTests` (architecture); `TestBrowser.PostAsync`
solves a challenge for a `RegisterRequest`/`RequestCodeRequest` whose `BotCheck` is null (`BotCheckSteps`), and a test of the
refusal sends `BotCheck = ""` or a raw string body; end-to-end steps that post straight to the API use `E2ESteps.BotCheckAsync`.

**Accounts.** Registration is by email only, by invitation during the closed trial (an MP office member's invitation counts
as one, owner 2026-11-17, design 8.5.3), with a self-declared age of 18 or older. An account is pending until its address is proven; the display name, the age declaration and the terms of use are
asked after the code, never before. One account per normalised address. Each account has a `verification_level` (`email` now); the EU Digital Identity Wallet
as a further level is designed, not built (owner, 2026-11-19). Every member has one personal invitation code (replaceable; uses unlimited unless
`Registration:PersonalCodeUses` is set) and sees only how many registered with it; administrators see numbers per day,
code and level, never who invited whom, and can switch codes off. Invitations are never combined with sides.
A member changes the display name with the "Profilis" form (`PUT /me/profile`, `ProfileHandlers.SaveAsync`, design
8.28.5; the designer's proposal awaiting the owner): the registration's rule (60 characters, empty allowed), refused under a
writing restriction (asked of Appeals through `IWritingRestrictions`), as often as the member likes (owner, 2026-11-16; the
form's rate limit guards against a runaway script), audited without the names (`profile.display-name-changed`), no earlier
names kept. Names are read live everywhere, so a
change shows beside older arguments too.

**Changing the email address (design 6.2; owner, 2026-11-19: "Yes, build it", the five points in `design/12-decisions.md`).**
`Identity/Application/Email/EmailChangeHandlers` behind four routes (`Lt2.Web/Endpoints/Me/EmailChangeEndpoints`), codes only in
bodies: `POST /me/email/proof-code` (202) sends the code to the address in use: the account's own sign-in code
(`SignInCodeIssuer`, its limits and known-browser allowance) with `SignInCodeEmail.Purpose` = `email-change`, which only changes
the email's words (`EmailProofCode.*`); `POST /me/email/change` (`EmailChangeRequest`: `email`, `proofCode`; 202, empty) checks
the address (`email.invalid`, `email.disposable`, `email.same` by `EmailAddress.Same`, normalised), then the proof
(`EmailChangeRules.Proof`, pure, `EmailChangeRuleTests`): a passkey session; a code session only with `proofCode`
(`email.proof-required` without it, `auth.code-invalid` on `proofCode` for a wrong one, used up by `SignInCodeUse`, shared with
the code sign-in); a staff account only its staff-passkey session freshly confirmed (`passkey.staff-required`,
`passkey.confirmation-required`), the MP's own account only a passkey session (`mp.passkey-required`). Then, under
`IdentityLocks.EmailAddressAsync` (the new normalised address), the sign-in code's two settings count every row of
`identity.email_change` to that address (beyond them nothing is sent, same answer); the account's earlier rows are replaced; a
code goes to the address (`email.email-change-code`, `EmailChangeCodeEmail`), or, when a member's account holds it, that
account's own address is told and no code is made (`email.email-change-taken`, a row without a hash still counted).
`POST /me/email/confirm` (`EmailConfirmRequest`, 200 `EmailChangedResponse`) takes the newest open row, only from the session that
asked (`session_id`), with `SignInCodeRules.Check` (ten minutes, five attempts, single use; `auth.code-invalid` for all), and in
one transaction: an account only pending on the address is deleted with its unsent emails (`outbox.ForgetAsync`), a member's
account that took it meanwhile refuses; `profile.normalized_email`, Identity's `Email`/`NormalizedEmail` and the security stamp
change (`UserName` is the id, not the address); every open sign-in code and change code ends; the other sessions are deleted;
`identity.email_restore` gets the old address with the hash of a `OneTimeCode` (seven days, `EmailChangeRules.RestoreLifetime`);
audit `account.email-changed` (`proof`, `sessionsEnded`, `pendingAccountRemoved`; no address); the old address is told
(`email.email-changed`, `EmailChangedEmail`: when, `EmailAddress.Mask` of the new one, `/paskyra/el-pastas/grazinti#CODE`, the
page and the code to type). After the commit the session is signed in again under a new key as what it was
(`SessionSignIn` with the row's method and passkey). Passkeys, known browsers, invitation codes, an MP's link stay; the MP offer
reads the current address (`PoliticianHandlers.MineAsync`). `POST /me/email/restore` (`EmailRestoreRequest`, 204;
`AllowAnonymous`, the group's antiforgery and Origin filter, `RateLimitPolicies.OneTimeCode`, on `TermsExemptions.Routes`)
puts the old address back under its lock, refuses with one answer (`email.restore-invalid`) an unknown, malformed, used or
expired link and an old address a member's account holds by then (an account only pending on it goes), ends the account's
other links back, its codes and every session (and signs the asking browser out if it was that account), audits
`account.email-restored` (no actor, no address) and tells the address it replaced (`email.email-restored`). The four emails are
`EmailChangeTemplates` (Notifications). The maintenance job deletes change rows after a day and links back once used or
expired; both tables go with the account and are withheld from `lt2_readonly`; the export holds only the address in use.
The part is `MyEmail` (`Lt2.Web.Client/Pages`, `#email-address`, first on "Saugumas ir duomenys"): it reads `GET /me` in the
browser only (nothing personal in the served page), "Keisti adresą" opens the steps in place (proof: `PasskeyStep` with its
`Text`, or "Siųsti kodą"; a staff account's step is the staff `PasskeyStep`; then the address; then the code), a staff
account's `passkey.confirmation-required` is answered by a passkey confirmation and the same press again, and on success the
part shows the new address and the page reads its sessions again (no reload). The link's page is `EmailRestore`
(`/paskyra/el-pastas/grazinti`, no sign-in, `<CodeEntry>` without a `#`, one press "Grąžinti ankstesnį adresą", then a full load
to `/prisijungti`). Tests: `EmailChangeTests` (integration, E2E), `EmailChangeRuleTests`, `EmailChangeTemplateTests`,
`MyEmailTests` (bUnit).

**A full lock of an account (design 8.3; the owner, 2026-11-19: administrators lock, the recommendations as written).**
`POST /admin/accounts/lock` and `/unlock` (`AccountLockEndpoints`, `Policies.Administrator`, `FreshConfirmation`,
`AccountLockRequest`: `email`, `statement`, validator in `Lt2.Api.Contracts/Admin/RoleDtos.cs`; the in-process `ServerAdminApi`
checks the same) call `IIdentityAdministration.LockAccountAsync|UnlockAccountAsync` (`RoleAdministration.SetLockAsync`,
Identity): `StaffStatements.Check`, the account by address (`account.not-found`), not oneself (`account.lock-self`), no active
staff role by `StaffAccess.ActiveRolesAsync` (`account.lock-staff`: reset it first), then in one transaction `identity.profile.locked_at`
set by a conditional update (`account.locked` / `account.not-locked` when it changed nothing), the account's sessions deleted and
its open codes used up, audit `account.locked` (`statement`, `sessionsEnded`) or `account.unlocked` (target the id, no address),
and an `AccountNoticeEmail` (`AccountNotices.AccountLocked|AccountUnlocked` with `Statement` and `Contact` = `Moderation:ContactAddress`;
`AccountNoticeTemplate`: the lock's text says to write to the team and that erasure is asked for by writing, no "was it you"
line). The lock is read where the account would sign in or be sent a code: `AccountLocks.IsLockedAsync` in `SignInCodeIssuer.IssueAsync`
(code request, registration of an existing address, email-change code: nothing sent, same answer), `CodeSignInHandler` (`auth.code-invalid`)
and `PasskeyHandlers.SignInAsync` (`passkey.invalid`); public profiles by `PublicProfileDirectory` (`locked_at IS NULL` in every
query, `FindAsync` answers `Retired` so the page and `GET /members/{id}` say 410) and `ProfileModerationHandlers.ReportAsync`;
follow emails by `ParticipantFacts.IsLocked` (`ParticipantDirectory`), skipped in `NoticeDelivery.WriteAsync`. Statements of reasons
and report notices still reach a locked account (they use `IParticipants.FindAsync`, not the follow path). The screen is
`AccountsAdmin` (`/administravimas/paskyros`; its first section ends an MP's link, design 8.5.2: `GET /admin/mps/links`
(`EndMpLinkEndpoint`, `MpLinkList` joins `IPoliticians.LinkedAsync` with `IMpDirectory.MpsAsync`, `AdminMpLinkResponse`: slug, name,
since, nothing of the account) fills `Links`; "Baigti ryšį" opens one statement form at a time under its MP (`_ending`), `EndMpLinkAsync`
with the same passkey dance, then the list is read again; `MpLinksScreenTests` (integration), `AccountsAdminPageTests`);
`Nav.Accounts` in the account item, `data-account-link="accounts"` in
`js/lt2-account.js`): it asks `GET /me` for an active administrator role (`Administrator` persistent state), else `#accounts-refused`
and the staff `PasskeyStep`; address and statement, "Užrakinti paskyrą" / "Atrakinti paskyrą", the staff passkey asked on the same press
when `passkey.confirmation-required`, the fields kept until the action goes through and then cleared (the address is never shown
again). Tests: `AccountLockTests` (integration), `EmailRhythmTests.A_locked_accounts_follows_send_nothing`, `AccountsAdminPageTests`
(bUnit), `NotificationTests` (the lock's email).

**The lock from an item and the list of locked accounts (design 8.3; the owner, 2026-11-19: administrators cannot find an address to type).**
Same file `AccountLockEndpoints` (`Policies.Administrator`, `FreshConfirmation` through the one `ActAsync`): `POST /admin/accounts/lock-author`
(`AuthorLockRequest`: `targetKind` of `LockTargets` = `reason`, `comment`, `statement`, `profile` (`Lt2.SharedKernel/LockTargets.cs`, linked
into the contracts); `targetId`; `statement`; validator in `Lt2.Api.Contracts/Admin/RoleDtos.cs`) goes through `AuthorLock.LockAsync`, shared with the in-process `ServerAdminApi`: for the first three
kinds `IAuthorLookup.AuthorOfAsync` (Appeals, `AuthorLookup`: `reasons.author_user_id`, `comments.author_user_id`,
`organisation_statements.written_by_user_id`, the same columns the writing restriction reads; `reason.not-found`, `comment.not-found`,
`statement.not-found`), for `profile` the id is the moderation id `ProfileModerationResponse.Id` (the account id the profile queue already
carries); then only the id goes to `IIdentityAdministration.LockAccountByIdAsync` (the same `SetLockAsync` as by address: statement, not
oneself, no active staff role, one transaction, audit, one email; `account.not-found` for an id no completed account holds). The kinds
are exactly the places the restriction reaches an author, plus the profile; an MP's response has none, an organisation's statement locks
the person who wrote it (`written_by_user_id`), as `RestrictWriterOfStatementAsync` does. `GET /admin/accounts/locked`
(`LockedAccountResponse`: `id`, `displayName`, `lockedAt`, `statement`; `IIdentityAdministration.LockedAccountsAsync`, newest first) and
`POST /admin/accounts/{id}/unlock` (`StatementRequest`, `UnlockAccountByIdAsync`) serve the list; the statement is kept in
`identity.profile.locked_statement` (migration `LockStatement`, cleared on unlock). The menus: `MyAccountState.IsAdministratorAsync`
(`GET /me` active role `administrator`, asked once, never while prerendering) lets `ModerationActions` (argument, where "restrict" is
offered), `CommentModerationActions`, `OrgModerationActions` (a statement where "restrict" is offered) and `ProfileModerationView` add
`lock` last, after hiding in the one run set apart; `<LockAuthor>` (`Lt2.Web.Client/Appeals`) is the form (the sentence `Acc.Lock.Sentence`,
the statement, the passkey asked again on the same press, then the same request once more, the statement kept). `AccountsAdmin` shows the
list ("Užrakintos paskyros": `Reasons.Anonymous` for a person without a name, `Acc.Locked.*`) between the MPs and the address form
(the secondary way, "Pagal el. pašto adresą"); "Atrakinti" opens one statement form at a time. Tests: `AuthorLockTests` (integration),
`LockAuthorTests` and `AccountsAdminPageTests` (bUnit).
**The account screens (design 8.21; the owner, 2026-11-16: "split into several forms", replacing the one page in four
parts; under `/paskyra`, the owner, 2026-11-16: "the url sveiki reads strange, change it"; `/sveiki` is gone, no redirect).** Four screens, each its own page with its own forms, joined by `<AccountTabs>` (`Lt2.Web.Client/Components`, the
`#account-parts` row of plain links, the current one `aria-current`, no script), with the same title and the terms note:
"Profilis" `/paskyra` (level and roles, then one form, `MyProfile`, below), "Sekimas ir laiškai" `/paskyra/sekimas` (`FollowList`, `#followed-items`; `EmailRhythm`, `#email-frequency`), "Dalyvavimas" `/paskyra/dalyvavimas`
(`MyMp`, `MyOrganisations`, `MyInvitationCode` `#my-invitation`, the staff part), "Saugumas ir duomenys" `/paskyra/saugumas`
(the email address `MyEmail` `#email-address`, passkeys, sessions, known browsers `#known-browsers`, the positions consent
`#positions-consent` for any consent not withdrawn, export and deletion `#my-data`, signing out).
The addresses are `AccountScreens` (`Lt2.SharedKernel/AccountScreens.cs`, also compiled into Lt2.Web.Client): links in emails, legal texts and pages are built from it, never
written by hand. A visitor sent to sign in from one comes back to it (`LandingPath` accepts `/paskyra…`). Without a page to
return to, signing in, a staff session and accepting the terms land on the front page `/` (owner, 2026-11-17); a
registration just completed lands on "Profilis" with the mark of that visit (`AccountScreens.AfterRegistration`,
`/paskyra#po-registracijos`, only when `SignInFlow` falls back to it; one begun from a bill's page ends on the bill), the MP's
take-over on "Dalyvavimas". Every screen is
`no-store` and carries `<NeedsScript />`; `IslandsBeforeStartTests` walks all four.

**"Profilis", one form (design 8.28.5).** `MyProfile` (`Lt2.Web.Client/Pages`) is one `.sheet` holding one form with one
button (`#profile-save`): the display name (`#name`), "Apie mane" and "Viešas profilis" (`#public-profile`, gone for the MP's
own account), the party with its consent box and "Rodyti viešame profilyje" (`#party-support`, offered while the form's switch
is ticked). Each consent is its own box, unticked until ticked, with its text inline before it is given. `GET /me/profile`
answers the whole form (`MyProfileResponse`: the name, "Apie mane" and its hiding, the public id and address, both consent
versions in force, `Texts:MaxLength`, the party, the parties to choose from, whether it is shown, `Digest:PartyGroupMinimum`;
composed by the host's `MyProfileForms` from `IMemberProfiles.MineAsync`, `IMpDirectory.PartiesAsync` and the setting).
`PUT /me/profile` takes the whole form (`ProfileRequest`: `displayName`, `about`, `public` + `publicConsentVersion`, `party` +
`partyConsent`, `partyPublic` + `partyPublicConsentVersion`) and saves it whole or not at all (`ProfileHandlers.SaveAsync`):
`ProfileFormRules.Check` (Identity `Domain`, pure, `ProfileFormRuleTests`) finds every refusal first, each with the request's
field; an unchanged part is not checked, written or audited (`ProfileFormRules.Changes`), so a writing restriction refuses only a
changed name or a written text. One refusal answers with its own status and code; several answer 400 `validation`; either way
`errors` names every refused field (`Error.Refusals`, `Error.All`; `ErrorMapper` keeps a field's own status). What changes is
written in one transaction with each part's consent and audit entry (`profile.display-name-changed`, `profile.about-changed`,
`profile.public-on|off`, `party.public-on|off`). The HTTP validator holds only technical ceilings, so the module names every
field. The separate writes (`PUT /me/display-name`, `PUT /me/party-support`, `PUT /me/party-support/public`) and
`GET /me/party-support` are gone; the withdrawals stay (`DELETE /me/profile/public`, `PartyWithdrawalEndpoints`:
`DELETE /me/party-support`, `DELETE /me/party-support/public`). On the visit after a registration (`#po-registracijos`, read
by the island in the browser, nothing stored) the button reads "Išsaugoti ir tęsti" and a successful save, an empty form
included, navigates to `AccountScreens.ProfileSavedHome` (`/#profilis-issaugotas`); a refusal stays. `ProfileFormTests`
(integration) cover the whole form.

**Public profiles (design 8.25; the owner, 2026-11-16: "implement those", before comments).**
- A profile on under an earlier consent text stays on (owner, 2026-11-19; design 8.25.1): `AccountHandlers.MineAsync` sets
  `AccountSummary.OlderPublicProfileConsent` (the profile is on and `PublicProfileConsents.CurrentAsync` finds no live consent to the
  text in force), `MeResponse.PublicProfileConsentOutdated` carries it to the person alone, `Hello` hands it to `MyProfile`
  (`ConsentOutdated`, `ConsentAccepted` re-reads `GET /me`), which shows `#profile-consent-outdated`, "Skaityti galiojantį tekstą"
  (`#profile-consent-read`) and in place the text, a box and "Sutinku" (`#profile-consent`); a save that switches the profile on or off
  settles it locally. `PUT /me/profile/consent` (`ProfileConsentRequest`, `MemberEndpoints`, `IMemberProfiles.AcceptConsentAsync`,
  `ProfileHandlers.AcceptConsentOwnAsync`, behind the terms like any write) refuses another version (`profile.consent-outdated`) and a
  profile that is off (`profile.not-public`); otherwise under `IdentityLocks.ProfileAsync` it ends the earlier `public-profile`
  consents, records one to `PublicProfileConsent.TextVersion` and audits `profile.consent-accepted` (no details); accepting twice
  changes nothing. Tests: `ProfileConsentVersionTests` (integration), `SignInComponentTests` (bUnit).
- Only by the member's box "Viešas profilis" in the "Profilis" form (`PUT /me/profile`), which is the consent `public-profile`
  (`PublicProfileConsent.TextVersion`, SharedKernel; the form's text in `UiText` under that version): the page `/nariai/{name-slug}-{id}`
  (a random ten-character id, `ProfileRules.NewId`; the name part from the current display name by `AddressSlugs.Of`,
  the one slug function MPs' `/p/{slug}` use too; owner, 2026-11-16), indexed, followable, with "Sekėjai: n". The id
  decides: any other name part (a renamed member, the id alone) answers 301 to the current address
  (`Components/Members/MemberAddresses`, before the page); a retired id 410 and an unknown one 404 whatever the name part.
  Every link is `ProfileRoutes.Page(id, name)` (`MemberAddress.Page` in the modules), and the profile's answers carry
  `address`. Switching off withdraws the
  consent and, in one transaction, retires the id (`identity.retired_profile_id`: it answers 410 for ever, never reused),
  ends every follow of it (`IMemberFollowEnd`, Notifications) and removes the link beside every text. An id never issued
  answers 404. Switching on again makes a new id. A display name is needed while public (`profile.name-required`).
- "Apie mane" (`about`): plain text up to `Texts:MaxLength`, written while off too and shown only with the profile;
  `https://` links shown with their host, `rel="nofollow ugc noopener noreferrer"`; own contacts allowed; a personal code
  refused (`ProfileRules.CheckAbout`, `about.personal-code`); a writing restriction blocks writing or changing it (an existing
  text stays); clearing it and switching off stay open (owner, 2026-11-17). "Viešas profilis" off alone is
  `DELETE /me/profile/public` (`ProfileHandlers.SwitchOffOwnAsync`); taking over an MP's profile switches it off too. Reported on the page (`POST /members/{id}/report`, `identity.profile_report`); a moderator hides it with a statement
  emailed to the person, keeps it or restores it (`/moderation/profiles…`, `ProfileModerationView`); a hidden text shows
  nowhere until the person writes another.
- The initials badge (design 8.25.4, owner 2026-11-19): `<MemberBadge ProfileId Name />` (`Lt2.Web.Client/Components`; pure rules `MemberBadges.Initials`
  and `MemberBadges.Colour`, tests `MemberBadgesTests`, `MemberBadgeTests`) stands before every name that links to a profile: an argument (`RecordReasons`),
  a comment's author and the "Sutinka" names (`CommentThread`), the follows list (`FollowList`) and beside the page's heading (`MemberPage`, `div.member-head`).
  It renders only for a non-null profile id and a name with a letter; colour from the profile's public id (not the account id); no picture, no column, no
  API field. Not in the hover card, the moderators' queue, or beside organisations and MPs. Members have no uploaded picture.
- The page (`Lt2.Web/Components/Members/MemberPage`, composed by the host from Identity, Notifications and Appeals) shows the
  name, "Apie mane", the follower number and the member's texts (`IMemberTexts`, Appeals): published arguments and the
  comments they wrote in their own voice that the bill's page shows, on bills whose record is not hidden, newest first, 10 and "Rodyti daugiau" +50 by `?rodyti=` (`ProfileLimits.More`, no script). Never sides, "Man svarbu", follows, invitations,
  organisations, anything written as an organisation or MP, "Sutinku" (the designer's proposal), counts of the person's
  texts, or who follows.
- **The party on the page (design 8.25.9, owner 2026-11-17).** The party already on "Profilis" (`identity.party_support`,
  the form's party), never a free field, shown as "Palaikoma partija: …" on `/nariai/…` only while the profile is public and
  the box "Rodyti viešame profilyje" is on: its own versioned consent `party-public` (`PartyPublicConsent`, SharedKernel; the
  form's `PartyPublicLimits.ConsentVersion`), given by the form's `partyPublic` (`PartySupportHandlers.GiveShownAsync`),
  withdrawn by unticking it or `DELETE /me/party-support/public` (`PartySupportHandlers.HideAsync`, on `TermsExemptions.Routes`), read for the page only through `IPublicProfiles.ShownPartyAsync`
  (`MemberProfileResponse.Party`, null otherwise). Unticking, removing or changing the party and switching the profile off
  (`ProfileHandlers.SwitchOffAsync`, also on deletion and take-over) withdraw it at once; switching on again does not bring it
  back. Never in the card, beside a text or in any list; nothing for the MP's own account (`mp.takes-no-part`); the party
  group rule and `IPartySupporters` unchanged. Audited `party.public-on|off` without the party; in the export.
- Beside an argument, a comment and a "Sutinku" name, the name of a member with a public profile is a link with a card
  (`js/lt2-cards.js`; `RecordReasons` with one `<template>` per profile id from `BillRecordResponse.Cards`, `CommentThread`
  with one per person and thread from `CommentThreadResponse.Cards`); `ReasonResponse.AuthorProfileId`,
  `CommentAuthorResponse.ProfileId` and `CommentAgreerResponse.ProfileId` are null for everyone else (and for an
  organisation's or MP's voice), and no public answer carries an account id. The moderators' queue keeps the plain name. The MP's own account cannot switch a profile on (`mp.takes-no-part`).
- Following a member (`follow` kind `member`, key the profile id; `PUT|DELETE /members/{id}/follow`; `member.not-found` for
  anything not public): a line by email when the member publishes an argument or an own-voice comment (bill number, title,
  link, never the text; the designer's proposal), in the follower's rhythm (`MemberNotices`), the switch `members` ("Kai
  sekamas narys paskelbia argumentą ar komentarą"). Exported and deleted with the account; the profile, the text, the
  consent and the reports a person made are in the export.

**MP profiles.** Compiled automatically from Seimas open data for every MP, joined or not. Two statistics:
participation (share of recorded votes in which the MP voted for, against or abstained) and voted with the faction
(only votes where at least two thirds of the faction's voting members chose the same option; free votes are excluded
and their count shown). No expenses, rankings or scorecards. An MP takes over a profile through a link sent only to the
official `@lrs.lt` address from open data; the link is tied to the Seimas person id (details below). Each MP's ballot on a vote is listed
in one place, the vote's page (`/seimas/balsavimai/{id}#ballots`, design 8.17: `VoteBallots`, filtered by `BallotList` in
Lt2.Web.Client): a name search without case or diacritics, a faction dropdown (with "Be frakcijos"), the ballot, 20 rows
then 50 more, its state in the address (`q`, `frakcija`, `balsas`, `rodyti`) with no script; a closed bill's page only
links to it. The history of votes (`/seimas/balsavimai`, design 8.18: `VoteHistoryPage`, its address state in
`VoteHistory` in Lt2.Web.Client, `GET /api/v1/votes` read and counted in the database a part at a time) groups votes by
Vilnius day, 30 then 50 more up to 300, with no participants' numbers; its rows share `VotedItemMain` with the start
page's "Balsuota". Its sheet has no heading, so its `div.page-head#page-head` (the h1 and `ProjectTabs`, a child of
`main`) is sticky (design 8.16.5), the days' dates under it, the lead and the form in `.page-head__rest`; an MP's votes
(`MpVotes`) have the h1 alone in it, the crumbs above it.

**The Seimas import's jobs (design 7.2; owner 2026-11-18).** The scheduled `parliament-import` (`ParliamentImportJob`,
every 15 minutes, imports when `IParliamentImport.DueAsync` says so) is unchanged; when not due it logs "Parliament import
not due (last start …); skipped" and sets the Hangfire job parameter `Skipped`, shown on the job's page of the dashboard.
Four manual jobs, `ParliamentImportPartsJob` (`MembersAsync`, `SittingsAsync`, `VotesAsync`, `EverythingAsync`; ids
`parliament-members`, `parliament-sittings`, `parliament-votes`, `parliament-everything`), are registered with
`Cron.Never()` (listed with "Trigger now", never scheduled) and English `[JobDisplayName]`s, as the dashboard's own text is
English; each calls `RunAsync(ImportParts.…)`, sets the parameter `Outcome`, and fails the job when the run was skipped
(another import holds the lock) or failed. Their triggers are audited as `jobs.dashboard-action` like any other. Tests:
`ImportTests.Forcing_*`, `Only_one_import_runs_at_a_time_a_forced_one_included`, `ImportScheduleTests`,
`ParliamentImportJobTests`, `JobsDashboardTests.The_imports_manual_jobs_are_listed_never_scheduled_and_an_administrator_triggers_one_audited`.

**A moderator corrects a vote's classification (design 7.2, owner 2026-11-20).** Storage: `parliament.vote` keeps the
correction apart from the rules' result (`kind_override`, `stage_override`, `overridden_at`, `overridden_by`,
`override_statement`; migration `VoteClassificationOverride`), and `kind`/`stage` hold the classification in force,
written only through the pure `VoteClassification.Effective` (Parliament `Domain/VoteClassification.cs`, with
`Corrected`, `CheckCorrection`, `CheckReturn` and the codes) by `ParliamentImporter.ApplyProceedings` on every parse and by
the moderator's action, so every reader (the queries, `BillCatalogue`, the closing job, analytics) needs nothing of its
own. The action: `IVoteClassifications` (`VoteClassifications.cs`), `Application/VoteCorrections` (`StateAsync`,
`CorrectAsync`, `ResetAsync`: `StaffStatements`, the codes by `Codes.VoteKindOf`/`StageOf`, the vote's row locked `FOR
UPDATE`, the rules recomputed from the stored wording and the agenda item's stage, audited `vote.reclassified` /
`vote.classification-reset` with before, after and the statement in the same transaction); whether the vote closed a
record is Appeals' `ClosingVotes` (`IClosingVotes` in SharedKernel: any record whose `closing_vote_id` it is). Routes
`ParliamentRoutes.VoteClassification` (`GET`/`PUT`/`DELETE /api/v1/moderation/votes/{id}/classification`,
`Endpoints/Parliament/VoteClassificationEndpoints`, policy `Moderator`, `ModeratorEndpoint.ActAsync`), contracts
`VoteClassificationRequest`/`VoteClassificationResetRequest` with their validators and `VoteClassificationResponse`
(`Lt2.Api.Contracts/Parliament/VoteModerationDtos.cs`), `IVoteModerationApi` (`ServerVoteModerationApi` in
`ParliamentServices.cs`, `HttpVoteModerationApi`). The page: `VotePage` asks whether the caller is a moderator and renders
`Lt2.Web.Client/Parliament/VoteModerationPanel` (`#vote-moderation`, `ModerationMenu` `#vote-mod-more` with
`reclassify`/`reset`, the form `#vote-action-form`, radios `#vote-kind-{code}`/`#vote-stage-{code}`, `#vote-statement`;
`#vote-classification-closing` in place of the actions on a record's closing vote; `AppealUi` `VoteMod.*`); everyone sees
the hint `#classification-corrected` ("Klasifikaciją pataisė moderatoriai", `VoteResponse.ClassificationCorrected`,
`ParliamentText` `Vote.Corrected*`) beside the stage, and `/metodika#stages` says so (`Method.Classification.2`). Tests:
unit `VoteClassificationTests`; integration `VoteClassificationTests`; import
`ImportTests.A_moderators_correction_of_a_votes_classification_survives_a_re_parse_and_a_full_re_import`.

**MPs on the platform (design 8.5, built 2026-11-15: the link 8.5.1–8.5.4, then the response 8.5.5–8.5.7).**
- The official address: an "El. p." contact of open data on `lrs.lt` (not a sub-domain), compared ignoring case, of an MP
  whose mandate has not ended; listed for two or more such MPs, it belongs to nobody (`OfficialAddresses` in Parliament,
  also used for the summaries' recipients). An account whose own address is one gets the offer in its own `GET /me`
  (`mpOffer`), shown on `/paskyra/dalyvavimas` by `MyMp`, as one line on "Profilis" by `MyMpLine` and (owner, 2026-11-17) as the
  notice `#mp-offer-notice` under the header of every page but the take-over page and "Dalyvavimas" (`MainLayout`, hidden markup
  the same for every member, shown by `js/lt2-account.js` from one `GET /me` per page load), in the browser only; the page the
  server renders holds nothing of it (`ServerMeApi` leaves `mpOffer` and `mp` out). Nothing is linked or public before the take-over.
- `Registration:OfficialAddressesWithoutInvitation` (off by default; on for the local stack and the trial site, owner
  2026-11-19): both invitation gates ask `OfficialAddressGate`. The local `.env` and the trial's deploy switch it on
  through the migrator (`--official-addresses-without-invitation`).
- "Perimti profilį" (`POST /mps/{slug}/claim`) sends a 24-hour one-time link (its secret a typeable one-time code, hashed, a
  new one replaces the old, three a day per MP) to the account's own address; the email also gives `/seimo-narys/perimti` and
  the code to type there. `POST /me/mp/takeover` (token and passkey) does everything in one
  transaction: token and address checked, the passkey stored and the others removed with their sessions, the link made,
  the account's sides on records not closed withdrawn (`IPositionErasure.WithdrawOpenSidesAsync`; the front page's copy
  marked stale), audited, the official address told; the session becomes a passkey session under a new key
  (`SessionSignIn`). A lost passkey: the same again; the link stays.
- One account per MP and one MP per account; an office member of at most one MP, five per MP, by a seven-day one-time
  link accepted in a passkey session. The MP's account may have the site email it (`MyMp`: an address typed, used only for the
  outbox row; `POST /me/mp/office/invitations` with `email`, `MpOfficeInvitationEmail`), or take the link and code; at most ten
  invitations a day per MP (`mp.office-invitation-limit`). The invitation counts as the closed trial's invitation
  (`OfficeInvitationGate`, Identity, in both registration gates; `RegisterRequest`/`CompleteRegistrationRequest.OfficeInvitation`;
  the account keeps only a level, one below the MP's account). Owner, 2026-11-17: the link's page `/seimo-narys/biuras#…` (`MpOfficeInvitation`) does
  everything: `<SignInFlow ReturnTo=…>` signs in or registers there and reloads the page with its `#` intact; a code session
  sends `NewPasskey` (made now) or `Passkey` (an assertion) with the token in `POST /me/mp/office/acceptance`, checked before
  anything is stored, and the session becomes a passkey session under a new key (`SessionRotation`); `GET /me` says
  `passkeySession`. Giving the profile up (`DELETE /me/mp`), an administrator's end
  (`POST /admin/mps/{slug}/link/end`, statement, fresh confirmation; the screen is the first section of `AccountsAdmin`, below) and deleting the account end the
  link and the office with it. An office member gets copies of the MP's summaries by their own choice (owner, 2026-11-17; see
  "Copies for the MP's office" under Summaries to MPs); any end of the office link ends them. Every change is audited and told to the MP's account's own address (`AccountNotices.Mp*`),
  never naming an office member.
- The MP's own account takes no part (decided by the owner, 2026-11-17): no side, argument, "Man svarbu", party
  support or organisation (`mp.takes-no-part`, `MpAccounts` in SharedKernel); it follows like anyone; office members are not
  restricted. Its forms are not shown to it (design 8.5.4): the islands ask `MyAccountState.IsMpAccountAsync` (the browser's
  `GET /me`, `mp.part` = `mp`, never while prerendering, so the served HTML is the same for every member) and the front
  page's rows `GET /me/bills`'s `mpAccount`; `SidePicker` shows one line to the responses instead, `RecordReasons` the
  number without "Man svarbu" and no "Rašyti argumentą", `MyAgendaSide` the numbers only; `MyProfile`'s party part (unless a party
  is still set) and its public profile part, `MyOrganisations`'s "Registruoti" and `/organizacijos/nauja`'s form go; `CommentThread` already
  hides by `MyComments.MpAccount`. The server's refusal stays the safety net.
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
  once; every change keeps the replaced text as a public version (`revision`, kind `response`, without a person), shown
  with what it changed (`TextChanges`, design 8.16.2, as are the description's, the statements' and the letter's); a withdrawn
  one stays as "Atsakymas atsiimtas" with its versions; written again it is the same row. After the adoption vote the MP's
  ballot at it is given beside it ("Atsakė: … Balsavo: …", `nebalsavo`, consensus said so); nothing adds answers and ballots
  up anywhere. Never counted: no count, threshold, list, statistic or summary changes (`MpResponseTests`).
- Moderation as for statements: flagged by pre-screening and not yet seen, or reported, it is in the queue
  (`GET /moderation/responses`, `ResponseModerationView` on `/moderavimas` and `/moderavimas/paslepti`); "Palikti" (`keep`),
  hide with a statement shown in its place and emailed to the MP's account's own address (`MpResponseNoticeEmail`, through
  `IMpLinks`), restore while the bill is open. Report target `response`.
- Where it shows (8.5.6): the bill's page part `BillResponses` (its own sheet, the first of the texts at the page's full width
  before the organisations and the arguments, and only when an MP answered (owner, 2026-11-17); server-rendered and the
  same for everyone; the counts line; "už", "prieš", "neapsisprendė", by last name, then withdrawn and hidden ones) and the
  profile's `MpAnswers` (newest first; no part without responses); both show 10 and "Rodyti daugiau atsakymų" (`<ShowMore>`, +20, `MpResponseLimits.Step`) by `?atsakymai=` landing
  on `#response-{n}` (`ResponsePaging`). Each item is `MpResponseItem` with the one mark "Seimo narys arba jo biuras". The
  linked account's form is the island `MyMpResponse`, filled in the browser only (on an open bill nobody answered yet it
  stands alone with `Section`, bringing the part's sheet and heading for a linked account); reports are `ReportResponse` islands.
- `/seimo-narys/projektai` (8.5.7, `MpBills`, linked from `MyMp`; `GET /me/mp/bills`): the bills in summaries now in the
  summaries' order (`Lt2.Notifications.DigestLists.InSummariesNow`), with both sides' numbers, one each
  (`#mp-bill-counts-{number}`; no count for accounts older than 30 days and no `#not-a-poll`, owner 2026-11-21; the API
  still returns them), and the MP's own response or "Atsakyti"; then (owner,
  2026-11-17) every other bill the MP answered, any state, newest first, without the counts ("Dabar suvestinėje nėra",
  "Atsakyti" only while open); a code session may read it; nothing about other MPs. The sheet's `h2#h-mp-bills` stays
  visually hidden; the page's h1 is its sticky `div.page-head#page-head` (design 8.16.5). A first response follows the bill for
  the account that wrote it (`MpResponseReads.SaveAsync`, as `TakingSides` for a first side; never refused or undone by the
  follow; editing or withdrawing adds nothing).

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
- One position per person per bill, private, changeable until the record closes (the final Seimas vote, or a
  moderator's close of a rejected or withdrawn bill, below).
- Reasons: plain text, public with the author's display name, for either side, whatever the
  author's own (no side needed; the writing box preselects the author's side if any). Nothing public may tell whether
  a reason is on its author's side. The author can withdraw their own reason while the bill is open. Writing a reason
  needs no positions consent: it is the author's own deliberate publication, public with name and chosen side.
- **Too short arguments (owner, 2026-11-17; design 8.3).** Fewer words than `Appeals:MinReasonWords` (3; 0 = off; `ReasonWords`
  in SharedKernel, also compiled into Lt2.Web.Client): `RecordReasons` asks in place before publishing ("Papildyti", "Vis tiek
  skelbti"); the server adds the flag `too-short` (`AppealsService.ReasonFlagsAsync`, writing and editing) and the argument
  waits for a moderator ("Per trumpas"). Arguments only.
- **Editing until someone reacts (owner, 2026-11-17; design 8.2, 8.23.3).** The author edits a reason's text and side
  (`PUT /reasons/{id}`, `AppealsService.EditReasonAsync`) or a comment's text (`PUT /comments/{id}`, `CommentsService.EditAsync`,
  in the voice it was written in) while the bill is open and nobody reacted: `AppealRules.CheckEdit` (no "Man svarbu", no
  comment under it, hidden ones included; `reason.reacted`), `CommentRules.CheckEdit` (no "Sutinku"; `comment.reacted`).
  Checked under `AdvisoryLocks.ReasonAsync` (taken by "Man svarbu" and a comment under the reason too) or the comment's lock.
  The rules of writing hold (length, pre-screening: a flagged reason waits again, `AppealRules.StatusAfterEdit`; a writing
  restriction); every edit clears the reason's `seen_at`; `edited_at` makes it new again (8.11.8) and moves the last
  activity; no earlier text kept; audited `reason.edited` / `comment.edited` without text. "Redaguoti" in "⋯" only for ids in
  the caller's never-cached `MyBillRecordResponse.Editable` / `MyCommentsResponse.Editable`, never shown disabled.
- **One length for members' texts (owner, 2026-11-16):** `Texts:MaxLength` (characters, 2 000; 0 = no limit) for
  arguments, organisations' statements and MPs' responses (and comments and "Apie mane"), checked where each
  is written (`AppealRules.CheckReasonText`, `OrganisationRules.CheckStatement`, `ResponseRules.CheckResponse`, all through
  `AppealRules.TooLong`; the refusal names the number); the one technical ceiling, 20 000 (`MemberTextLength.Ceiling` =
  `SettingKeys.MemberTextCeiling` = `MemberTextLimits.Ceiling` in the contracts), holds whatever the setting says and is all
  the shared validators check. The columns are `text`. A lower number later shortens nothing. The forms read the number
  from `AppealRulesResponse.TextMaxLength`. A bill's description, moderators' statements, reports, feedback, names and
  links keep their own limits. A long text shows its beginning and "Rodyti daugiau", opened "Rodyti mažiau" under the
  rest (`TextExcerpt`, one file in SharedKernel compiled also into Lt2.Web.Client; `Components/LongText`): an argument
  about 300 characters, a statement or a response about 1 500; the moderators' queue keeps texts whole.
- "Man svarbu" endorses a reason of either side, with or without a side of one's own, never one you wrote, and needs the
  positions consent, whose text names the marks since version 2026-11-18 (`PositionConsent.TextVersion`, `Consent.*` in
  `AppealUi`, `PositionConsent.*` in `UiText`; a consent to an older text counts as none: `CurrentPositionConsent.Of`;
  the account page shows any not withdrawn, `CurrentPositionConsent.Live`, with its withdrawal, and for an older text
  (`MeResponse.PositionConsentOutdated`, owner 2026-11-19) the line `#consent-outdated` and the text in force in place,
  `PositionConsentForm ForAccount`, accepted through `PUT /me/consents/positions`; the public-profile consent is asked the same way,
  below)
  (owner, 2026-11-18; 2026-11-17 for one's own: `AppealRules.CheckEndorsement`, `endorsement.own-reason`,
  `consent.required`; the island disables it on one's own argument from the never-cached answer and asks for the consent in
  place, `PositionConsentForm ForMark`, before a first mark; a merge drops a mark that would land on its giver's own
  argument). **A mark counts only while its giver's current side is the argument's side** (`AppealRules.MarkCounts`):
  `AppealReads.EndorsementCountsAsync` joins `endorsement` to `reason` and `position` on read, so the figure, the order on
  the bill's page, `MineAsync`'s own arguments, the similar arguments, the moderators' merge candidates and the summaries'
  pool (`DigestSource.ReasonPoolAsync`, hence `ChoiceRules` and the snapshot) all use counted marks; nothing is stored, so
  taking, changing or withdrawing a side (`TakePositionAsync`, `ClearPositionAsync`) deletes no mark. Editing until someone
  reacts counts every mark (`AppealReads.ReactionCountsAsync`, `EditReasonAsync`), and so does the limit. The caller's own
  uncounted marks are `MyBillRecordResponse.Uncounted`; `RecordReasons` shows them pressed with a note under the row
  (`Reasons.Uncounted.NoSide.{side}` / `.Other.{side}`, `#mark-note-{id}`, the button's `aria-describedby`). Withdrawing
  the consent (`PositionErasure`) still deletes marks. How often marks do not count (owner, 2026-11-18), counts only, no
  person; run as the owner role (`endorsement` and `position` are withheld from `lt2_readonly`); the last row (no bill) is
  the total:

  ```sql
  SELECT b.bill_number,
         count(*)                                                    AS marks,
         count(*) FILTER (WHERE p.side = r.side)                     AS counted,
         count(*) FILTER (WHERE p.side IS NOT NULL AND p.side <> r.side) AS other_side,
         count(*) FILTER (WHERE p.side IS NULL)                      AS no_side
  FROM appeals.endorsement e
  JOIN appeals.reason r ON r.id = e.reason_id
  JOIN appeals.bill_record b ON b.id = e.record_id
  LEFT JOIN appeals.position p ON p.record_id = e.record_id AND p.user_id = e.user_id
  GROUP BY ROLLUP (b.bill_number)
  ORDER BY b.bill_number NULLS LAST;
  ```
- **Sides by pressing (design 8.7, owner 2026-11-18).** `SidePicker` and `MyAgendaSide`: the pressed button, pressed again,
  sends `DELETE /bills/{n}/record/position` with `{ side }` (`IAppealsApi.ClearPositionAsync(number, PositionRequest?)`;
  the module's `ClearPositionAsync` withdraws only that side, `AppealRules.WithdrawalApplies`, so a late or stale press is a
  no-op; no body withdraws whatever side there is). No "Atsiimti" for sides (`Side.Withdraw`, `Agenda.Withdraw` removed);
  `Side.PressAgain` is only the pressed button's `title` (owner, 2026-11-19: no line under the buttons, after a press
  either; a later onboarding is to explain it, design 12-open); `#my-side` on the bill's page says whose side it is.
  Disabled while a request runs (`Busy`); `SidePresses.Ignored` drops a double click's second press (`MouseEventArgs.Detail`).
  The pair (`.sides`) carries `aria-busy` while disabled for waiting (`Locked`; `MyAgendaSide.Waiting` also until its first
  read ends), which keeps the buttons in full colour (lt2.css "Waiting", owner 2026-11-18).
- **The arguments a page at a time (design 8.12.1, owner 2026-11-19).** `GET /bills/{n}/record` carries each side's first
  two (`ReasonLimits.First`) and `ReasonTotals`; `GET /bills/{n}/record/reasons?side=&cursor=&limit=` (`GetReasonsEndpoint`,
  `IAppealsModule.ReasonsAsync`, ordered in SQL by the counted marks, then `CreatedAt`, then id; `ReasonLimits.Max` 100)
  the rest; `GET /reasons/{id}` (`GetReasonEndpoint`, `ReasonAsync`; one 404 `reason.not-found`) one published argument.
  `RecordReasons` keeps each side as a column (`Columns`, a `ReasonPageResponse` per side, persisted from the prerender;
  `ReasonPaging`, unit-tested): the address asks how many (`?argumentai-uz=` / `?argumentai-pries=`, `ReasonLimits.Parameter`,
  read on the server, and in the browser up to it should enhanced navigation have grown it before the island started);
  "Rodyti daugiau argumentų" under each column is `<ShowMore>` (`#reasons-more-{side}`, status `#reasons-more-{side}-shown`),
  the island's `MoreAsync` reads 20 more (`ReasonLimits.Step`) in place and focuses the first added without scrolling. The linked argument (`?argumentas=`), one a comment link or
  `?komentarai=` names, and the one just published are pinned first in their side (`ReasonPaging.Pin`), read alone when
  their pages would not bring them; "Rodyti tik naujus" reads the new ones the pages lack (`NewTexts.Reasons`) and adds them
  last. The server renders the discussions of every argument it shows; one a later page brought reads its own on opening.
- A "Man svarbu" moves nothing on the page: `RecordReasons` keeps the shown order
  after the person's own action (`ReasonPaging.Refresh`: each column read again through as many as it had read, the
  order by marks returns on the next read) and gives the pressed button
  the focus back without scrolling. It is a toggle whose button stays neutral, only the heart (`Icon` `heart`, `.icon__fill`)
  filling in the brand's green while marked (owner, 2026-11-17); a visitor's is a `LandingPath.SignInLink` back to the bill
  of the same look. One hint beside the arguments' heading, "Kas yra „Man svarbu“?" (`Reasons.EndorseWhat`, owner
  2026-11-17), says when it can be marked (either side, not one's own argument, counted only on the side one asks for; owner,
  2026-11-18), "Plačiau" to
  `/metodika#arguments`; the same served markup for everyone, gone for the MP's own account once the island runs.
- **The bill's page (design 8.12.1, owner 2026-11-17).** The header: "Projektai / Įstatymo projektas XVP-0412" (no label
  line), the title, one `.follow-line` (the stage tags, "Planuojama …", then `FollowButton`); header and body in a
  `.stack--snug` (16px apart, owner 2026-11-18). Beside the side column: what it is about, the sides (with `#not-a-poll`
  under `SidePicker`, above `#threshold`; owner, 2026-11-18), a closed bill's result; the side column: "Stadijos Seime"
  with `#planned-source` ("Iš kur ši data?", the agenda link) and `DateFlagForm` under the stages, the
  moderators' panels, the summaries. The side column is the page's last part (`HasAside`, `.bill-body`; only `SourceLine`
  follows), placed beside the main column on a wide screen and after the votes on a phone (owner, 2026-11-18). The e-seimas link stands once: in `#description-source` or under `#no-description`
  while the page has a side column; only the full-width `StagesSheet(inAside: false)` (hidden record, finished bill nobody
  took part in) carries "Projektas e-seime" (owner, 2026-11-18). Then at the page's full width: `BillResponses`, `OrganisationPositions` (each only
  when there is something to show) and `RecordReasons` (always). An argument reads name, text, action row; "Aptarimas"
  opens in its side's column (no stacking of the sides); a chosen action of "⋯" opens under the row, above an open
  discussion (`.act > .act__panel` before `.discuss__panel` by `order`).
- Reasons and marks per person per bill are settings (`Appeals:ReasonsPerPerson`, `Appeals:EndorsementsPerPerson`);
  0 = no limit, the default. A limit switched on later removes nothing and counts only shown or waiting reasons.
- **Threshold:** one for all bills, `Digest:ThresholdDefault`, never below `Digest:ThresholdMinimum`; a moderator may set
  a bill's own number with a public statement (who set it: `default` or `moderator`). A bill once in a summary really sent
  stays eligible. No objection flow in the product and no automatic override before a vote.
- **Descriptions** ("Apie ką projektas", design 8.13): one per bill, written or corrected by a moderator on any active
  bill with a statement, or made by the summary job from official text, shown at once, with a public history; labelled
  "Sukurta DI · žmonių neperžiūrėta" (the job's, read by nobody), "Sukurta DI · taisė žmonės" (a moderator confirmed or
  corrected AI text), or "Parašė moderatorius". Beside it: the day it was made, the official text it was made from when known, the history and "Pranešti
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
  description and answer a report with "Palikti" (and hide the whole record, as before, or undo a moderator's close,
  below); each with a statement, audited,
  the author told. The frozen counts and summaries already made never change. Everything else (threshold, holds,
  urgent summaries, merging, publishing a waiting argument, confirming a description) stays refused; the rules live in
  `RecordRules.CheckRecordAction` / `CheckReasonAction`, and the queue offers exactly them. The bill's page asks
  whether the caller is a moderator on an open and on a closed record (`BillPage.razor`; owner, 2026-11-19), so a closed
  record's page shows the "Moderavimas" panel and "Moderuoti" with only those actions (design 8.3).
- **Who reads a moderator's explanation (design 8.3, owner 2026-11-22).** Each statement field's label says it:
  `AppealUi` `Mod.Statement.Author` (emailed to the author: an argument's hide, merge and restore, a comment's, "Apie
  mane", restoring an MP's response), `Mod.Statement.Public` with `Mod.Statement.Public.Hint.bill|response|organisation`
  (hiding a record, its threshold; hiding a response; blocking an organisation) and `Mod.Statement.Staff` with
  `Mod.Statement.Staff.Hint` (`.description` for writing or removing a description, which answers its reports); an
  organisation's other actions keep `OrgMod.Statement`. `ModerationActions.StatementLabel`/`StatementHint` pick them for a
  bill's and an argument's actions; `RestoreForm.StatementHint`, `DigestModeration`'s `StatementForm` and
  `VoteModerationPanel` carry the line under the label (`field__hint`, in `aria-describedby`). On the server one
  `StatementRequest` serves both: `StatementRequestValidator` (the shared `IValidator<StatementRequest>`) refuses a missing
  statement with `Statement.RequiredPlain` "Parašykite paaiškinimą.", and the endpoints whose statement goes to the author
  pick `StatementToAuthorRequestValidator` by type (`WithValidation<StatementRequest, StatementToAuthorRequestValidator>()`,
  `Statement.Required` "Parašykite paaiškinimą autoriui."): `HideReasonEndpoint`, `RestoreReason`, `LiftRestriction`,
  the comment's and the profile's hide and restore, `RestoreMpResponse`; `MergeReasonRequestValidator` and
  `RestrictionRequestValidator` name the author too (the helper `Statement(l, required, toAuthor)`). The code stays
  `validation` on `statement`. The modules' own `statement.required` already reads "Parašykite paaiškinimą."
  (`ProblemText`). Tests: `ModerationActionsTests`, `AppealContractTests` (unit),
  `ModerationApiTests.A_missing_statement_is_refused_naming_the_author_only_where_the_author_receives_it` (integration).
- **Closing a rejected or withdrawn bill (design 8.1 step 6).** `BillCatalogue.RejectionsAsync` reads an explicit
  approved rejection motion from the most recent vote on its bill. `CloseFinishedAsync` freezes the counts and records
  the date, vote source and system audit. `last_automatic_rejection_vote_id` is the rejection vote last handled
  (`ClosingRules.IsPendingRejection`): set by that close, and by a moderator's reopen to the rejection known then
  (`BillRecords.RejectionAsync`, also when a moderator had closed the record by hand), and kept on reopening so that
  decision cannot close the record again. A moderator can close the open record with "Uždaryti: projektas atmestas ar atsiimtas"
  under "Seime" in the record's menu
  (`ModerationActions`, `ClosingReasons` in the contracts), `POST /api/v1/moderation/records/{id}/close`
  (`CloseRecordEndpoint`, `CloseRecordRequest` + `CloseRecordRequestValidator`: `reason` `rejected`|`withdrawn`,
  `decidedOn`, `sourceUrl` by `OfficialTextAddress` and required, `statement`; `IModerationApi.CloseRecordAsync`).
  `ModerationService.CloseRecordAsync` checks `RecordRules.CheckClose` (the day not after today in Vilnius,
  `closing.date-future`; `closing.reason`, `closing-url.invalid`; `moderation.wrong-state` unless open and the bill has
  no pending automatic close) under the bill's lock and freezes the counts with `ClosingRules.Close`. Audited
  `record.closed` (moderator, reason, day, source, statement) through
  `ModerationRecorder`'s `auditAction`, a `moderation_action` row `close`. Columns `closed_reason`, `closed_decided_on`,
  `closed_source_url` (migration `RecordClosedReason`); `BillRecordResponse.ClosedReason|ClosedDecidedOn|ClosedSourceUrl`,
  `ModerationBillResponse.ClosedReason`. The page (`BillPage`, `#seimas-result`) shows "Seimas projektą atmetė {data}."
  or "Iniciatoriai projektą atsiėmė {data}." (`#closed-decision`) with "Šaltinis" (`#closed-source`, `rel="external
  noopener"`) and the frozen counts "Skaičiai užfiksuoti …" (`Closed.Frozen.Ended`); no ballots. "Atšaukti uždarymą"
  (`POST …/reopen`, `ReopenRecordEndpoint`, `StatementRequest`; `RecordRules.CheckReopen`, never `vote`;
  `ClosingRules.Reopen` restores the open state; audited `record.reopened`; nobody told). Analytics and
  `MyClosedSides` keep to records with a `ClosingVoteId`; summaries take open records only (`DigestSource`). Followers
  are told by the follow kind `ClosedNotices` (Notifications; `bill_event` kind `closed`, key `FollowRules.ClosedKey`,
  the moment it closed; read through `IRecordClosureFeed`, Appeals' `RecordClosureFeed`; switch `voted`; a close undone
  before the email is dropped), outbox kind `email.follow-closed` (`FollowClosedEmail`, `FollowTemplates.Closed`, part
  `ClosedPart` "Seimas nebesvarsto:", order 25). Tests: `RecordCloseTests` (integration), `RecordCloseRuleTests` and
  `ModerationActionsTests` (unit).

**Reasons to come (design 8.11).**
- The agenda (`/darbotvarke`, where the menu item "Projektai" leads) and the front page's "Artimiausiu metu Seime" come from one public,
  cacheable answer that holds nothing about the caller, and show both sides for every bill (a bill nobody took part in
  says so only in a row without side buttons, owner 2026-11-19); the answer also names, per bill, the organisations with a public statement (as on the bill's page, never
  counted). The front page (design 8.7, reworked 2026-11-15; mockup `design/ui/pradzia.html`) is that agenda as a
  timeline of sitting days: a visitor first gets "Kaip tai veikia" (the same texts as `/metodika#how-it-works`) and
  links to register or sign in, never a form; a member gets "Jūsų projektai" (the bills they follow, and only those: owner,
  2026-11-17, "followed only"; a side alone shows in its row) and their marks in the rows, islands filled
  in the browser from `GET /me/bills` (one request, never prerendered; it also says whether the caller gave the positions
  consent), so the HTML is the same for every member. The timeline is on a `.sheet` (design 8.16); the page has no
  "Organizacijos" block and the introduction no paragraph on following (owner, 2026-11-16: the footer links to
  organisations, the account page registers one, "Sekti" says what it sends), and no row of links under the agenda (owner,
  2026-11-19: the footer repeats the menu's sections); the visitor's head (owner, 2026-11-17) is the
  headline (the lead sentence as `h1`; the title stays "Lietuva 2.0") and the trial note under it with the invented-data
  notice's edges, the turning logo centred to their right from 54rem (across the invented-data notice too, which on this
  page is the head's first row, not the layout's), beside the headline on a phone. "Kas yra riba?" is one hint beside the note under the timeline, as on `/darbotvarke`. A coming bill with an open record offers "Prašau balsuoti" with "už" and "prieš" in its row (owner,
  2026-11-15), on the front page and `/darbotvarke` alike (one `TimelineItem`, owner 2026-11-19): a visitor's are
  `LandingPath.SignInLink` links to the bill's page; a member's side column is one island
  (`MyAgendaSide`, disabled before it starts) that takes, changes (the pressed button, pressed again, withdraws) the side with the bill page's
  request, asks the first-time consent across the row with the shared `<PositionConsentForm>` (also in `SidePicker`), then
  shows the bill's live numbers from `GET /bills/{n}/record`; the other islands follow through `MyAgendaState.Changed`.
  The islands (`MyAgendaSide`, `MyAgendaMark`) are keyed by the row's place (`@key`), so a day grown in place keeps
  each on its row and starts the rows added. A first side follows the bill and the mark's "Sekate" lights up; no status line
  says so (owner, 2026-11-19: "Dabar sekate šį projektą." gone, `Agenda.Followed` removed). The mark (`MyAgendaMark`) also
  says what is new on a followed bill (`NewsLine`, `#home-new-{place}`, a link to the bill; design 8.11.8, owner
  2026-11-19: "the same like start page"), from the same `/me/bills` answer. "Jūsų projektai" has no share line (owner,
  2026-11-19: `Mine.Share`, its `CopyLink` and `MyAgendaState.InvitationAsync` removed; the bill page's `ShareLine` stays). Beside the side buttons no "Niekas nenurodė, ko prašo" (`RecordFigures.HideNobody`; `MyAgendaSide.Asks`)
  and no link but the title (owner, 2026-11-19: "minimise non-essential texts"); a voted row keeps both its line and
  "Kaip balsavo Seimo nariai".
  The figures are one component (`Lt2.Web.Client/Agenda/RecordFigures`, with `SplitBar` in `Lt2.Web.Client/Components`),
  rendered by the server and by the island; with a side and a copy that says zero, the island reads the live numbers once on
  start. **A reached row names its summary's day (design 8.12.2, owner 2026-11-21):** "suvestinė planuojama {day}" /
  "suvestinė išsiųsta {day}" (`AppealUi` `Agenda.Summary.planned|sent`) in the `nowrap` part that otherwise says "riba
  pasiekta", from `AgendaRecordResponse.Summary` (`RecordSummaryResponse(Kind, Day)`, kinds `RowSummaryKinds` in
  `SharedKernel/DigestCodes.cs`). The pure rule is `Lt2.Web.Client/Agenda/RowSummaryRules` (`RowSummaryRulesTests`): open
  record: planned day, else last really sent; closed: last sent only; no trial variants, a dry run never counts. The host
  composes it in `RowSummaries` (`Lt2.Web/Services/RowSummaryServices.cs`) for `AgendaReads` and `GET /api/v1/appeals`
  (`/projektai`, `AppealList`'s `#reached-{number}` in place of "riba {N}"): one `IDigests.SummaryDaysAsync` call per list
  for the reached rows (Notifications: the planned day by the same batch as `NextForRecordAsync`, `IDigestSource.CandidatesAsync(ids)`
  from Appeals, and the last `sent` run; a constant number of queries, not one per row), cached with the agenda. `MyAgendaSide`
  keeps the page's summary on its live numbers; a threshold first reached by the member's own side asks
  `GET …/record/digests/next` once (`MyAgendaTests`). The bill's page (`ThresholdProgress`) says "Projektas jau buvo
  suvestinėje Seimo nariams – balsuoti prašo {n} …" (`Threshold.Summarised.*`, `Threshold.SummarisedClosed.*`) when
  `EnteredSummaries` and the participants are under the threshold. Tests over HTTP: `DigestTests` (planned, prepared, dry
  run stays "riba pasiekta"), `MpSendingTests` (sent; the page's line after the threshold is raised). The server keeps the front page's agenda in memory for 60 s (`FrontPageAgenda`; a test removes
  `FrontPageAgenda.CacheKey` instead of waiting). A side taken, changed or withdrawn marks the copy out of date
  (`FrontPageFreshness`, marked by `TakingSides` for every API client): the first request once the copy is 2 s old reads
  the agenda again; other changes wait for the minute. The agenda page reads the same copy (`ServerAgendaApi`; a side marks
  it out of date there too, `FrontPageFreshnessTests`); the bill's page and `GET /api/v1/agenda` stay live.
  **The lists' lengths (owner, 2026-11-18, real-sized parliament).** `AgendaReads` orders each day by
  `Lt2.Web.Client/Agenda/AgendaRules` (pure, `AgendaRulesTests`): activity (participants + published arguments, from
  `BillRecordState.Arguments`), then stage (adoption, consideration, presentation; the parser reads a combined stage as its
  furthest, `AgendaParser.StageOf`), then number, bills without a title last; the votes by Vilnius day, newest first, each
  day the same way. `AgendaRules.Day` cuts a day: the front page (`Home.razor`) shows `FrontDays` (2) planned and voted
  days of `FrontPerDay` (5), on two sheets (owner, 2026-11-21): `section#home-coming` (h2 `#h-home-coming`, "Visa
  darbotvarkė" `a#home-agenda-all` beside it) and `section#home-recent` (h2 `#h-home-recent` "Neseniai balsuota",
  "Ankstesni balsavimai →" `a#home-votes-older` beside it; `#home-no-recent` under it when none), each heading row a
  sticky `div.row.list-head` (design 8.16.5); and `/darbotvarke` `PagePerDay` (10), and only those (owner, 2026-11-18: the hidden rows are
  not in the HTML). A day grows by the shared `<ShowMore>` (owner, 2026-11-19, design 8.16.4; "Rodyti likusius" and
  `Agenda.ShowRest` gone): one day is `PlannedDay` / `VotedDay` (`Lt2.Web/Components/Agenda`, used by both pages), whose
  line `#{day}-more` (status `#{day}-shown`, "Rodoma N iš M") stands under a day with more known rows than its first page
  (`AgendaRules.Paged`) and links to `AgendaPaging.MorePlanned` / `MoreVoted`: the same page with the day grown by
  `AgendaRules.MorePerDay` (50, `ListPaging.RowStep`; `NextAsked`), `?diena=2026-11-24:60` / `?balsuota=2026-11-17:60` (pages `AgendaPaging.Agenda`,
  blocks `#day-…`/`#voted-day-…`, and `AgendaPaging.Front`, `#home-day-…`/`#home-voted-…`; the parameter repeats for every
  day grown, `AgendaPaging.Grown` carrying on only days shown beyond their first rows). `AgendaPaging.Read` takes
  `day:N` only (a bare day, a word, a number ≤ 0 dropped); `AgendaRules.ShownKnown` ignores a number not above the first
  rows and caps at the day (and `MaxAsked`). N is `AgendaDayView.ShownCount`: the known rows shown, plus the unknown bills
  once their line shows; M is the head's count; the last page has the status and no link. Enhanced navigation swaps it in
  with script. `/darbotvarke` has the same two sheets (`#agenda-coming`, `#agenda-recent`), `a#agenda-votes-older` beside
  `#h-recent` in its heading row. Neither page has a note, a hint or `#not-a-poll` under its lists, and the rows show one
  number a side (`RecordFigures`; owner, 2026-11-21). The unknown bills are
  `UnknownBills` (`details.tl-unknown`) after the last known row once every known row shows (`AgendaDayView.UnknownShown`),
  on both pages;
  rows `TimelineItem` and `VotedItem` with `Member` and `Numbers` as on the front page (owner, 2026-11-19: the same rows;
  `AgendaPage` renders them through `AuthorizeView`, carries `<NeedsScript />` for a member, and has no "Jūsų projektai"),
  heads `TimelineDate` (date in words, weekday, count via `AgendaWords`).
  The agenda answer is still built and cached whole (the order needs every row's activity); only the rendering is cut.
  The islands ask `/me/bills` about
  the shown rows only (`AgendaRules.Asked`; `MyAgendaState` splits them into requests of `MyBillsQuery.MaxNumbers`, sent side by side); "Jūsų projektai" (`MyAgendaPanel`,
  `MyAgendaState.PanelAsync`) lists the followed bills of the whole agenda and last week from `GET /me/follows`
  (`OnTheAgenda`) and asks `/me/bills` about those not shown, 30 a request (`Requests`); its `h2#h-my-agenda` is in a sticky
  `div.row.list-head` in both of its sheets (`#my-agenda`, `#my-agenda-failed`; design 8.16.5).
  **The greeting and the new member's start (design 8.28.3).** `MyStart` (`Lt2.Web.Client/Agenda`, an island) renders the
  member's `h1#home-title`: "Sveiki" as served and before the island runs, then "Sveiki prisijungę" with the start
  (`#home-start`: the full steps `.intro-steps.starter__full`, the short `.starter__short` under 54rem, "Kaip tai veikia →" to
  `/metodika#how-it-works`) while `MyAgendaState.ShowsStart` (positions consent not given, not the MP's own account; from
  the rows' `/me/bills` answer, or `GET /me` through `MyAccountState` when the agenda is empty), else "Sveiki sugrįžę". It
  follows `MyAgendaState.Changed`, so a first side takes it away without a reload. Step 4 is the same text as "Kaip tai
  veikia" (`HowItWorks.4.*` in `AppealUi`, which `HowItWorks.razor` reads for its fourth step); the steps name the controls
  by their own texts (`Agenda.Ask`, `Agenda.For`, `Agenda.Against`, `Reasons.EndorseName`). `MyAgendaPanel` is left out while
  the start shows and nothing followed is on the agenda. After "Išsaugoti ir tęsti" (8.28.5) the front page's address carries
  `#profilis-issaugotas` (`AccountScreens.ProfileSavedHome`): `MyStart` says "Profilis išsaugotas." once in a status region and
  takes the mark out of the address (`history.replaceState`). Tests: unit `MyStartTests`; E2E `StartPageTests`.
  **How a vote went (design 8.18, "The majority"; the owner asked for "Priimta", open in 12-open).** `<VoteMajorityTag>`
  (`Components/Parliament`) shows `AnalyticsRules.SeimasMajority` calculated from the ballot totals as
  `span.tag.tag--outcome` with `data-majority` (`for` filled, `against` `tag--outline`, `none` also `tag--dashed`;
  `ParliamentText` `Majority.*`: "Dauguma už", "Dauguma prieš", "Nėra daugumos"), the consensus as "Pritarta bendru
  sutarimu", nothing without numbers; used by `VotedItemResult` (front page, agenda and the shared `VoteTimeline` on
  `/seimas/balsavimai` and the bill page), the bill's closing vote and `VotePage`; `<MajorityHint>` ("Kas yra balsavusiųjų dauguma?") stands once
  beside the history of votes, on the vote's page and on the bill's page; the agenda's lists carry none (owner, 2026-11-21).
  "Projektai" (`/projektai`; `/kreipimaisi` redirects there) lists active bills, sorted by activity (participants
  plus published arguments; the default), planned date or the latest activity (owner, 2026-11-17: the page offers these
  three, `rikiuoti` = `aktyvumas`, `data`, `naujausia`; another value falls back to the default; the API's `sort` has
  `activity`, `participants`, `arguments`, `planned`, `latest`), with a search (`IBillCards.SearchAsync`, design 8.12.5: the one search of every list of bills, over `appeals.bill_card`; the votes' and the statistics' lists ask it too and filter by the numbers it returns) and the box "Planuojami per 14 dienų"
  (`input#planned-only` in the search form above "Rodyti", applied with it; owner 2026-11-21); each row has "už … · prieš …" beside its bar (`List.Counts`,
  one number a side; nothing under the list: no note, hint or `#not-a-poll`, owner 2026-11-21; the sheet has no heading,
  so the h1 and `ProjectTabs` are its sticky `div.page-head#page-head`, design 8.16.5; `p#found` is an ordinary line), and in
  the latest-activity order "Paskutinė veikla {yyyy-MM-dd HH:mm}". The last activity is one time per bill,
  `appeals.bill_record.last_activity_at`, set by `RecordActivity` (Appeals) in the same transaction as a side taken, changed or
  withdrawn, an argument published, a comment written, an organisation's statement or an MP's response made public or
  changed; never who acted. The page shows 20 and grows in place by `?rodyti=` (`Lt2.Web.Client/Appeals/BillListPaging`:
  +50, up to 200, then "Tolesni projektai" `?nuo=`); the API's
  `limit` (`BillListLimits`, 20, 1–200). The list is composed in memory (`AppealsService.ListAsync`): the bills are the
  Parliament module's, the activity the Appeals module's. An MP's votes (`/p/{slug}/balsavimai`, `MpVotePaging`) are by day,
  50 then +50 by `?rodyti=` (rows `#vote-{n}`), at most 300, then `?nuo=`; `GET /mps/{slug}/votes` takes `limit`
  (`MpVoteLimits`). Every "show more" is `<ShowMore>` (design 8.16.4, below); a press adds 50 rows or 20 texts (`ListPaging`,
  `ListPagingTests`). Every moderators' list keeps its hundred
  and carries its total (`ModerationQueueResponse.RecordsTotal` etc.); `<QueueCut>` says "Rodoma 100 iš N".
- Following a bill, an MP, an organisation or a member is private: who follows is shown to nobody (the followed party and
  staff included), never combined with sides or invitations (the person's own `/me/bills` gives them both), in the export,
  deleted with the account; there is no limit on how much a person follows (owner, 2026-11-15: `Follows:MaxPerAccount` and
  `follow.limit` are gone). Emails (planned, voted; each switchable on the account page) go once per event to those who
  followed before it; nothing older than three days, nothing about the person's side, the bill's counts or other people.
- **Follower numbers (design 8.11.2, owner 2026-11-16, built with the profiles).** "Sekėjai: n", exact from 1, is shown only
  on the thing's own page: the bill's page beside "Sekti" (`GET /bills/{number}/followers`), the MP's profile and the
  organisation's profile (`followers` in their answers) and a member's page; never in list rows, a member's card or beside
  texts, never sorted, ranked or added up, nothing at zero (`FollowerLine`, inside `FollowButton` (`Followers`) so the person's own "Sekti" moves it at once). Counted from `notify.follow` when the answer is made, through `IFollowerCounts` (SharedKernel,
  Notifications): a number only. The last three points are the designer's proposals awaiting the owner.
- **One compact follow line (owner, 2026-11-17)** under the title of a bill, an MP, an organisation and a member: the page's
  tags in a `.follow-line` row, then `FollowButton`: "Sekti" (a visitor's a sign-in link back), "Sekėjai: n", and one hint
  "Kas yra sekimas?" (`Follow.Hint`); the button and the account list's "Nebesekti" are `.btn--compact` (owner, 2026-11-18) whose answer is the kind's `Follow.What.*` (a visitor's `Follow.SignIn.*`) and
  `Follow.Private` (nobody sees who follows; what the number is), "Plačiau" to `/metodika#following`. `FollowerLine` keeps its
  own "Kas yra sekėjai?" only where it stands without the button (`WithHint`: a closed organisation, the member's own page).
- "Seimo narių atsakymai" (design 8.20.1, the fourth switch `responses`), in the person's rhythm (design 8.24): one part per
  followed bill listing the MPs whose response became public or changed stance (`appeals.mp_response.announced_at`,
  `ResponseRules.Announces`; a changed explanation with the same stance is no event) and are still public, with name,
  faction and stance and the link to `#responses`, never the explanation. Read through `IMpResponseFeed` (Appeals); each
  person is told only about answers from after they followed (`FollowRules.ResponsesFor`), and the MP's own account and
  office members never about their own MP (`IMpLinks.SpeaksForAsync`). Recorded in `bill_event` kind `response`, one per
  answer (key: `FollowRules.ResponseKey`, the person number and the moment), with the three-day rule of votes.
- **How often emails come (design 8.24, the designer's proposals in section 12 await the owner).** A member chooses "Iš karto"
  (the default), "Kartą per dieną" or "Kartą per savaitę ir artėjant balsavimui" (`notice_setting.frequency`,
  `GET|PUT /me/notices/frequency`, the island `EmailRhythm` on the follows screen `/paskyra/sekimas`, its own sheet
  `#email-frequency` after the switches; audited `notices.frequency-changed` without the choice). One place decides and sends:
  `Rhythm/NoticeDelivery` (Notifications) makes, each run, the run's emails, the "planned" dates that cannot wait, the daily
  email from `Follows:ResponsesAt` ("Rytinių laiškų laikas", 08:00) and Monday's, and gathers everything for one person into
  one email per run (`GatheredNoticesEmail`; one thing alone keeps its own email). The rules are `RhythmRules` (pure); going
  back to the choice just left within 15 minutes undoes the change (`RhythmRules.UndoWindow`). A daily or weekly email is made
  once per period (`notify.rhythm_pass`). `bill_event` rows older than 30 days go with the daily outbox clean-up
  (`IFollows.PurgeEventsAsync`, `FollowRules.EventRetention`; the designer's proposal). Comments are read up to two minutes
  before the run (`FollowRules.CommentsWindow`). An
  event belongs to the choice the person had when the job first saw it. A member email kind joins by declaring itself: an
  `IMemberNoticeKind` (record, then take per pass and mark its events) and its part (`AddNoticePart`). The kinds: planned,
  voted, statements, MPs' answers, followed members (`MemberNotices`, switch `members`) and comments under one's arguments
  (`CommentNotices`, switch `comments`); codes, account and moderation
  notices, feedback and MP mail never go through it.
- **The stop link (design 8.24.1, owner 2026-11-17; what one press stops is the designer's proposal awaiting the owner).**
  Every email of the rhythm carries `List-Unsubscribe` (`/api/v1/notices/{token}/stop`) with `List-Unsubscribe-Post`
  (`EmailLayout.StopHeaders`) and, in the footer (owner, 2026-11-17), "Keisti periodiškumą" to `AccountScreens.EmailRhythm`
  (`/paskyra/sekimas#email-frequency`, the payloads' `AccountPath`) "arba atsisakyti laiškų" to `/laiskai/nebegauti/{token}`
  (`EmailLayout.BuildNotice`); paths in `MemberMailLinks` (SharedKernel) and `NoticeStopRoutes` (contracts). The token is one
  per account, `MailKeys.MemberStopToken` (the mail key, account id, generation 0), its hash in `notify.member_mail_token`
  (withheld, deleted with the account, not exported), made by `NoticeDelivery` with the first email; the clear token travels
  in the payload's `StopToken` (`DueNotice.Write` takes a `NoticeAddress`). One press (`IFollows.StopByLinkAsync`) turns every
  email switch off, the follows stay; audited `notices.stopped-by-link` with the account as target and no actor or detail.
  The page `NoticeStopPage` (server-rendered, no script, GET changes nothing) and `NoticeStopEndpoint` (mapped outside the API
  group's filter, on `TermsExemptions.Routes`) answer the same for any token; limited under `RateLimitPolicies.MpMail` and
  the per-token limiter; no confirmation email.
- Taking a side follows the bill (design 8.11.2, owner 2026-11-15): a member's first side on a bill (no side before)
  follows it in the same request, composed by the host (`Lt2.Web/Services/TakingSides`: the Appeals module saves the
  side, then `IFollows.FollowBillAsync`), on the bill's page and in a front-page row alike. The side is never refused or
  undone because the follow failed. Changing a side adds nothing, withdrawing it keeps the follow, "Nebesekti" keeps the
  side; sides from before were not backfilled. The follow row holds nothing about the side.
- **What is new since the last visit (design 8.11.8, owner 2026-11-17).** A bill's follow keeps `last_seen_at` (from the
  follow's own time; goes with the follow and the account, in the export; nothing for bills not followed or any other
  page). The bill page's island `WhatsNew` (above the texts, nothing in the served HTML) sends one write once interactive,
  `POST /me/bills/{number}/seen` (`MyBillsReads.SeenAsync`, `own-read` limit): on a followed bill it keeps now
  (`FollowRules.Seen`) and answers the moment before with the new texts by id, on another bill nothing. What is new is
  Appeals' `NewsRules` through `IBillNews` (SharedKernel): shown texts made public or changed after the moment, never hidden
  ones, never the member's own (their organisations' and their MP's included). `NewMark` shows "Nauja" (inside the islands,
  and as its own island on each server-rendered response); "Rodyti tik naujus" (a plain checkbox) leaves only new texts and
  those with new comments, their discussions open, the responses hidden by the stylesheet from `NewMark`'s `.new-out`
  marker. "Jūsų projektai" and the follow list show the counts (`BillNewsCountsResponse`, `NewsLine` with `NumberForms`:
  "3 nauji argumentai · 5 nauji komentarai"), one read per list.
- Taking, changing or withdrawing a side, following and unfollowing (bills, MPs, organisations), "Man svarbu" and changing
  the display name share one per-account request limit, `RateLimits:Participation` (60 a minute): a guard against a runaway script, not a rule.
- Links in emails: a message carries the site path, and the template makes it a full address with
  `SiteLinks.Absolute` from `Site:Address` (design 5.6). That is deployment configuration, never a runtime setting.
- The share line (owner, 2026-11-17: "Facebook only shows the 'Prisijungti' page"): the bill's own address with the
  member's code after the `#` (`LandingPath.ShareLink(code, path)`: `/seimas/XVP-0412#kodas=…`; the plain address when the
  code is off or used up, with `Share.NoLink`); the same link whichever side. The front page's "Jūsų projektai" and an
  organisation's per-bill links (`OrganisationBillLinkResponse.Link`, its own code) are made the same way.
  `js/lt2-invitation.js` (every page, before blazor.web.js) moves a `#kodas=` into session storage `lt2.invitation-code` and out of the
  address bar (`history.replaceState`, other `#` parameters kept); `SignInFlow` fills the registration in from it (or from
  its own `#kodas=`) and forgets it after the registration or a sign-in to a finished account. The old query forms
  (`?kodas=`, `?tikslas=`) are not read (owner: "we don't need old links to work").
- Sign-in links on a bill's page (side picker, follow, report; owner 2026-11-15) carry the page to come back to
  (`LandingPath.SignInLink`: `/prisijungti#tikslas=...`), so signing in and registering both end on the bill.
- **Link previews (design 8.22.5, owner 2026-11-17).** `Components/Layout/LinkPreview` (`<HeadContent>`: `description`, Open
  Graph, `twitter:card`) placed by each public server-rendered page with `Title`, `Description` (default `Home.Lead`),
  `Type` (`article` on a bill) and `Path` (default the requested path without its query); `og:url` and `og:image` only with
  `Site:Address` (`SiteLinks.Absolute`). Text rules and the image's name are `LinkPreviewText` (pure; `Excerpt` keeps whole
  sentences up to 200 characters). The image is `wwwroot/img/link-preview.png` (1200×630, the logo with the name below it, inside the centred 630×630 square that phones crop to; made once
  in a headless browser from the logo SVG and Sofia Sans Extra Condensed). The texts are `Preview.*` in `PageText.resx`. A
  bill uses its description unless it is `ai`, never counts; a member's page only while public and never "Apie mane"; a
  blocked organisation none. `SecurityHeaders.IsPrivatePage` marks account, staff, sign-in, MP and one-time-link pages
  `noindex, nofollow`; `/img` is `Cross-Origin-Resource-Policy: cross-origin`. Tests: `LinkPreviewTests` (integration).
- The AI draft is only in a moderator's "write or correct the description" form, on any active bill, from pasted official
  text: the Claude API (owner, 2026-11-13), the model the setting `Ai:DraftModel`, on only with `Ai:Provider` = `claude`
  and a key in `deploy/secrets/ai_api_key`; otherwise nothing mentions a draft. The prompt is the public file
  `src/Modules/Lt2.Appeals/Ai/description-draft.prompt.md`. Audited without text, 20 an hour per moderator; a description
  that started from a draft says "Sukurta DI · taisė žmonės" for its whole life. The worker is configured the same way
  for the summary job (compose passes `Ai__Provider` and the key to both). Pre-screening of users' texts stays
  rule-based. Tests never call the real API.
**Analytics (design 8.26, owner 2026-11-16).** "Statistika", `/statistika` (`MenuSections.Statistics`; owner 2026-11-17: in the main menu, no tab row) (`Components/Analytics/AnalyticsPage`), public and
server-rendered from `GET /api/v1/analytics` (composed in `Lt2.Web/Services/AnalyticsServices.cs`): the four figures, the
Seimas's share "for" beside the members' frozen counts on closed bills, faction unity (`FactionUnityRule` in Parliament).
The closed bills are one dot plot (`#compare-plot`: rows `li.dotplot__row#bill-{nr}`, a label column with the
number and the title cut by CSS, a per-row SVG track on one shared axis) and "Rodyti lentelę" (`#compare-table`, rows `row-{nr}`,
title column) over the same bills (owner, 2026-11-19, back from the rows of 2026-11-18), with their own GET form in
`details#compare-filters-box` ("Filtrai (n)", `AnalyticsListState.Active`; open while a field is set) (`nuo`, `iki`, `q`,
`rezultatas`, `dauguma`, `rikiuoti`, `rodyti`: 10, then +50 up to 300; `AnalyticsList` in `Lt2.Web.Client/Analytics` reads the address, the API
reads `AnalyticsBillQuery`); `AnalyticsReads.Select` filters, orders and cuts the kept copy in memory; the figures and
factions follow only `metai`/`paskyros`. Tests: `AnalyticsTests` (integration, the filters over HTTP and on the page),
`ComparedBillListRuleTests`, `AnalyticsListTests`, `AnalyticsShareFormatTests` (unit).
"Kas Seime balsuoja taip, kaip prašote jūs" is the island `MyAgreement` from `GET /api/v1/me/analytics/agreement`: the caller's own sides
against the MPs' ballots, the only place MPs are ordered by a number; the server-side API client refuses it, so no page
holds it. Kept copies (owner, 2026-11-16) live only in memory (`AnalyticsCopies`): the public answer per filter and data
version, served as it is while one background refresh runs once it is an hour old; a member's comparison per account until
12 idle hours, sign-out, account deletion or consent withdrawal (`Forget`). Every rule is
`AnalyticsRules` (SharedKernel); one sample of 20 (`AnalyticsRules.SmallSample`, not a setting) below which no share is
shown, only counts and "maža imtis"; the Seimas's majority is that of those who voted, a consensus counting as "for".
Demonstration data (design 8.26.5, owner 2026-11-18; only where `Demo:Allowed`, never in tests): thirty invented closed
bills XVP-0701..0731 besides XVP-0707, their votes in the stand-in overlay (`tools/demo_closed_bills.py`, run by `tools/standin_overlay.py`; ballots of the overlay's 141 MPs, totals counted from them, XVP-0706 rejected with more than half "prieš"), their positions
from `--demo-appeal`'s own invented accounts (`DemoClosedBills`, `InventedMembers.SinceAsync`: the fixed identifiers, on
`pavyzdys.test`, with the consent in force; accounts made under an older consent text get the one in force when
`--demo-appeal` runs again, `InventedMembers.GivePositionConsentAsync`, unless they withdrew it); `--demo-sides-for ADDRESS|-`
(`DemoSides`) gives one named account with the consent in force (a real account consents itself, never by a script)
random sides on them and adds them to the frozen counts (`InventedRecord.GiveClosedSidesAsync`), the address never logged.
Integration: `DemoAppealTests`, `DemoSidesTests`.
**Organisations (design 8.14, 8.14.14).**
- A voice on a bill, not control. A signed-in member registers an organisation (`/organizacijos/nauja`; at most
  `Organisations:PerAccount`, 3, not counting closed ones), shown at once with nobody approving it, and is its first
  representative. Profile `/o/{slug}` (the address never changes), list `/organizacijos`; no menu item.
- Labels: "Nepatvirtinta", "Patvirtinta per el. pašto adresą @domenas" (a code sent to an address on the website's host
  or a name under it; never a public mail service or shared platform; only the domain is kept), "Patvirtino
  moderatorius". After a moderator withdraws a confirmation only a moderator confirms that organisation.
- Representatives come only by a one-time link (seven days, at most ten; its code shown beside it and typed on
  `/organizacijos/kvietimas`, owner 2026-11-17); they see each other's display names and
  nobody else does, staff included, except a moderator restricting a statement's writer. Nothing ties a
  representative's own side to the organisation. The last one cannot leave, only close it; deleting that account closes it.
- One statement per organisation per bill (`for`, `against`, `remarks`; as long as `Texts:MaxLength` allows, owner 2026-11-16; an https link; with a
  side an optional goal "Siekiame {N} dalyvių · dabar {n}"), in "Organizacijų pozicijos" after the MPs' responses and before
  the arguments, shown only with a statement or for a representative (owner, 2026-11-17: `OrganisationPositions.Shown`). Never
  counted: counts, thresholds, lists, statistics and which bills summaries hold are the same with and without them (the
  agenda and the front page name them beside a bill, never with a number); a summary names them under a bill (below).
  Edits keep public versions; frozen when the bill closes; none on a hidden record. The bill's page gives each statement
  the id `BillTextLinks.StatementElement` (`statement-{id}`), which the summaries' links use.
- Moderation after the fact: new and changed organisations and waiting, flagged, reported or unseen statements are in
  the queue; block and lift (lifting returns exactly what was shown), hide and restore a statement, publish a waiting one,
  confirm by hand, withdraw a confirmation, "Palikti"; each emailed to every representative. Report ground "Apsimeta kita
  organizacija". No automatic check of the website (owner, 2026-11-14).
- **Keep out of summaries (design 8.14.9, owner 2026-11-20).** `IOrganisationModeration.KeepOutOfSummariesAsync` /
  `ReturnToSummariesAsync` (`POST /moderation/organisations/{id}/keep-out-of-summaries`, `…/return-to-summaries`,
  `StatementRequest`, statement required; `OrganisationRules.CheckKeepOut`/`CheckKeepIn`: not closed, `moderation.wrong-state`
  otherwise; a blocked one can be kept out): columns `kept_out_of_summaries_at`, `kept_out_by_user_id`, `kept_out_statement`
  on `appeals.organisation`; recorded and audited (`moderation.organisation.keep-out` / `keep-in`), answers no report; emailed
  with the notices `kept-out-of-summaries` / `back-in-summaries` (`OrganisationNotices`). On the site nothing changes. UI:
  "Neįtraukti į suvestines" / "Vėl įtraukti į suvestines" under "Kiti veiksmai" of `OrgModerationActions` (queue), beside each
  organisation of a prepared summary on `/moderavimas/suvestines`, and a section of `HiddenOrganisationsView` with the undo
  (`HiddenOrganisationsResponse.KeptOut`); the representatives' `OrganisationManage` shows a quiet note with the statement
  (`OrganisationManagementResponse.KeptOutOfSummariesAt`, `KeptOutStatement`). Tests: `OrganisationModerationTests`,
  `DigestOrganisationTests` (unit).
- A statement still waiting when its bill closes (owner, 2026-11-15) can no longer be published (error
  `statement.waiting-bill-closed`); the queue still shows it and offers only "Palikti" (`/moderation/statements/{id}/keep`:
  it stays unpublished, leaves the queue unless reported, representatives are told) and hiding.
- Bringing members (design 8.14.6): one invitation code per organisation (Identity, kind `organisation`, no account on it; the
  representatives' part of the profile shows its link, a link per bill with a statement and one number, filled in only in
  the browser); a registration with it leaves only the level on the account (the pending account holds no such code: the
  browser sends it again at completion); blocking switches it off, lifting on, closing for good, apart from an
  administrator's switch; `Registration:OrganisationCodeUses`. Following an organisation (`follow` kind `organisation`, a
  third email switch): one email when a statement first becomes public (`first_public_at`), never its text; the number of
  followers stands on the organisation's own page (`OrganisationPage.razor`, design 8.11.2), nowhere else.

**Summaries to MPs.**
- Go to every sitting MP with an official address, joined or not. Nobody chooses recipients.
- Weekly on Monday morning in sitting weeks, and the working day before a sitting with a relevant bill. At most two a
  week per MP.
- **Reviewed a day ahead, not approved (design 8.19.2, owner 2026-11-17).** A weekly or before-vote summary is prepared
  `Digest:ReviewHours` (24) before its time on the Vilnius clock (Monday 07:00 on Sunday 07:00; `DigestRules.PreparedAt`,
  `PreparationDue`): its bills (eligible, not held) and per bill and side its two most endorsed published arguments, seen by a
  moderator or not, are fixed then (`ChoiceRules.Choose`, stored in `notify.digest_choice`); every moderator and administrator
  is emailed (`email.digest-review`, `DigestReviewTemplate`, addresses from `IStaffAddresses`, Identity). Hiding an argument
  (the normal action) brings the next of its side in from the same pool (`ChoiceRules.Replace`, marked "Įtrauktas vietoj
  paslėpto"); the list is not sorted again when it goes. At its time (`GoAsync`) every number is read then, a held or no
  longer open bill drops out (none left: `dropped`), and the snapshot and public copy are made. "Patvirtinti"
  (`POST /moderation/digests/{id}/approve`, staff passkey, no fresh confirmation) only marks it reviewed (`approved_at`); it
  still goes at its time. "Nesiųsti" declines it (`cancelled`). If nobody acts it goes as prepared. A staff account (by its
  role) sees the box `#digest-review-notice` on every page while one is prepared and neither approved nor declined:
  `MainLayout` holds the same hidden markup for everyone, `js/lt2-account.js` fills it from `GET /me`'s `digestReview` (staff
  accounts only; `ServerMeApi` leaves it out), as the MP offer notice.
- A side with more than two published arguments is headed "… (iš N)" (owner, 2026-11-20): N is the published arguments in the
  pool the two are chosen from (`Published` in `DigestService`), read when it goes and stored in `digest_item.for_reason_count` /
  `against_reason_count` (null in an older snapshot: no number). `DigestReasonCount.Shown` (SharedKernel) is the rule, used by
  `MpMailTemplates` (`Digest.ReasonsOf.*`), `DigestMapping` (`ForReasonsOf`/`AgainstReasonsOf` on `DigestItemResponse` and
  `DigestReviewItemResponse`, so the public copy and the moderators' screen show it); `DigestItemInfo.ForReasonCount` carries the raw one.
  No total of marks. Tests: `MpMailTemplateTests` (rule, headings for 0, 1, 2, 3, 14), `DigestReviewTests`, `MpSendingTests`.
- **The organisations' line (design 8.14.4, owner 2026-11-20).** After a bill's arguments: "Organizacijos, prašančios balsuoti
  už: A, B." / "… prieš: …" / "Pastabų atsiuntė: …" (`Digest.Organisations.*` in `EmailTemplates` and `AppealUi`), names only,
  each linking to `BillTextLinks.ToStatement` (`/seimas/{n}#statement-{id}`). The rule is `DigestOrganisationRules` (SharedKernel,
  pure): published now, `first_public_at` no later than the run's preparation (`digest_run.created_at`), organisation active,
  confirmed, not kept out; for, against, remarks, each by `published_at` then id (the bill page's order). The facts come from
  Appeals through `IDigestSource.OrganisationStatementsAsync` (no table read across modules); applied live on the moderators'
  screen and once more when it goes (`FreezeAsync`), so a keep-out or block during review applies. Frozen as JSON in
  `digest_item.organisations` (null in an older snapshot: no line; `[]` when none qualified), carried by
  `DigestItemInfo.Organisations` / `DigestItemResponse.Organisations` / `DigestReviewItemResponse.Organisations`. Rendered by
  `MpMailTemplates.Organisations` (HTML: under a thin rule, muted words, names as dark links; text part: the sentence, then
  each name with its full address on its own line) and by `DigestOrganisationLine` (Web.Client) on `DigestPage` and
  `DigestModeration`. Tests: `DigestOrganisationTests` (rule and email), `DigestOrganisationLineTests` (bUnit),
  `OrganisationCountTests` (integration: prepared, screen, keep-out during review, snapshot, public answer and page).
- Always both sides, always with the count for accounts older than 30 days, with the most endorsed published reasons,
  without their authors' names (email, public copy and snapshot alike). A reason longer than about 300
  characters is shortened in the email by `TextExcerpt`, with "Visas argumentas: {link}" to it on the
  public copy (`MpMailPaths.ReasonOnPublicCopy` = `DigestRoutes.ReasonAnchor`); the snapshot keeps the whole text (owner,
  2026-11-16). The email prints no other member-written text (the MP's own response as stance and date only; organisations
  by name only).
- The weekly one holds appeals on that week's agenda, or never summarised, or whose participants changed by at least
  the `Digest:NotableChange` share (default 10 %) since the last summary that held them.
- Every summary has a public copy with the same numbers. Whether an MP stopped the summaries is never shown publicly.
  A bill's page links to it as `/suvestines/{day}?projektas={number}` (`DigestRoutes.PageAt`; owner, 2026-11-17): the summaries
  holding the bill first, its item first, no fragment; a day with one summary has one title, its kind and date.
- Timing is corrected by a moderator when people flag a wrong date: correct, hold, or approve one urgent summary. The
  urgent one needs a fresh staff-passkey confirmation (design 6.2, `FreshConfirmation` in `UrgentDigestEndpoint` and
  `ServerModerationApi.UrgentAsync`); `ModerationActions` asks for the passkey on the same press when refused with
  `passkey.confirmation-required` and sends it again with the statement typed.
- **Sending (design 8.19, built 2026-11-16; owner 2026-11-17).** `Digest:Sending` `off` (default: prepared and reviewed the
  same way, then dry runs, nothing for MPs in the outbox) or `automatic` (sent at its time; an urgent one at once). Two locks keep it off whatever
  the setting says (`SendingRules.State`, `MailLocks`): no introduction letter sent yet, and invented data (`Demo:Allowed`, or any synthetic parliament data: `IMpMailAddresses.HoldsInventedDataAsync`) without
  `Email:Caught` (deployment configuration; false in code and compose, true only in the local `.env`, where Mailpit catches
  everything). Run statuses `prepared`, `dry-run`, `sent`, `cancelled`, `dropped`; a prepared run always counts, and while
  sending is on sent runs (off: dry runs too), for the ceiling, "changed notably", "covered by Monday" and "never yet in a
  summary" (`SendingRules.Counts`). A sent run marks its records summarised (`IDigestSource.MarkSummarisedAsync`, in the same transaction).
- The moderators' screen `/moderavimas/suvestines` (`DigestModeration`): prepared first, each summary's common text once
  (bills with their numbers now, the chosen arguments with "Paslėpti", each bill's "Sulaikyti siuntimą"), how many MPs, what
  happened; one line that party numbers, the MP's own response and the personal links differ per MP; no per-MP preview.
  "Patvirtinti" and "Nesiųsti" (a statement of 20 characters or more, public on the copy) under the summaries' lock, so two
  presses act once. Recipients are worked out when it goes (`SendingRules.DeliveryStatus`: suppressed, the MP's choice, the
  letter first, the ceiling); one delivery row per run, MP and recipient, never an address. The public copy says what
  happened in one line, never which MPs; a prepared or dropped run has none.
- The email (`MpMailTemplates`, through `EmailLayout.BuildMp`, the `mp` stream: `Email:Mp:FromAddress`, optional `Email:Mp:Smtp`; ceiling `Email:CeilingMpPerDay`)
  holds both sides with the 30-day counts, the MP's own published response or the link to answer, the closing lines, and
  `List-Unsubscribe` with `List-Unsubscribe-Post`. The address comes only from `IMpMailAddresses` and travels only in the outbox
  row.
- **Copies for the MP's office (design 8.19.3, owner 2026-11-17).** Each office member linked now (addresses only from
  `IMpLinks.OfficeMembersAsync`, Identity) gets a copy of every summary that goes for the MP (`DigestService.OfficeCopiesAsync`,
  recipient `office`, one delivery row per member with `office_user_id`, never an address), whatever the MP chose: by the
  member's own choice (`notify.office_copy`, the four `Frequencies`, no row = default; `GET|PUT /me/mp/office/copies`, any
  session, `MyMp` on "Dalyvavimas"), own weekly ceiling (`SendingRules.OfficeStatus`) and the suppression list. The copy's
  one-click stop is the MP's route `POST /api/v1/mp-mail/{token}/stop` with the member's own token (derived per account, MP and
  generation, its hash on the member's row), and its "Nebegauti kopijų" page the copies' page; both stop only that member.
  Ending the office link ends them and deletes the choice (`IOfficeCopiesEnd`, Notifications, called by Identity's
  `PoliticianHandlers`; an account's deletion through Notifications' `IAccountData`). Nothing public counts or names them.
- The MP's link (8.19.4): `/suvestines/nustatymai/{token}`, server-rendered forms (no script); the token is derived from the
  person number and a generation under `Security:MailHashKey` and stored hashed; "Atsiųsti naują nuorodą" moves to the next
  generation. The one-click stop `POST /api/v1/mp-mail/{token}/stop` is the only write of MP mail mapped outside the API group's
  same-origin and antiforgery filter (`OneClickStopEndpoint`; the one other is a member's stop link, `NoticeStopEndpoint`,
  design 8.24.1); `MpMailTests` proves the other token routes still need it. Token
  routes and pages are limited per address and per token (`RateLimits:MpMail`, `RateLimits:MpMailToken`) and answer the same for
  an unknown and a wrong token. Every change is confirmed to the official address. An extra recipient is confirmed from the
  official address (a page with a button, 24 hours, one use) and each copy has its own "Nebegauti kopijų" page. A linked account
  changes the same choice on "Dalyvavimas", `/paskyra/dalyvavimas` (`MyMp` in `AccountTaking.razor`,
  `GET|PUT /me/mp/mail`, passkey session).
- The introduction letter (`/administravimas/pristatymas`; public `/suvestines/pristatymas`): written and signed by a person,
  a test copy to the administrator's own address, sent with a fresh passkey confirmation to MPs who have had none; a changed
  text is a new version. An MP who had none gets it in place of a summary (`introduced`).
- The suppression list (`/administravimas/nesiunciama`): keyed hashes only, reasons `bounced` (a 5xx refusal, automatic),
  `complaint`, `staff`; the screen lists the sitting MPs on it and counts the rest.
- No address, token or MP's choice in any log line or audit detail: request paths with a link's token are logged as `***`
  (`Lt2.Hosting.SecretPaths`, the Caddyfile's `regexp` filter).

**Feedback.** A balloon on every page (and `/atsiliepimai`) lets members write to the team; an item is linked to the
account only when the member allows a reply, otherwise nothing leads back; staff reply through the outbox; not public.

**Comments, "Aptarimas" (design 8.23, built 2026-11-16).** One service in Appeals (`CommentsService`, `CommentRules`; tables
`appeals.comment`, withheld from `lt2_readonly`, and `appeals.comment_agreement`, readable) for comments under an argument, a
statement and a response, one level, oldest first, public at once, never counted or sent to MPs. The voice is the server's:
a representative under its organisation's own statement writes as the organisation, the MP's account or office under that
MP's own response as the MP (passkey session, mandate not ended), everyone else as a person; the MP's account comments
nowhere else. Pre-screening refuses an email address, a phone number or a personal code (`comment.screened`); the phone rule
(`RuleBasedPreScreening.ContainsPhoneNumber` in `PreScreening.cs`, every screened text) matches only Lithuanian numbers: `+370`/`00370`, the trunk `0`
or the old `8`, then eight digits starting 3–9 (owner, 2026-11-17), and (owner, 2026-11-17) a mobile without a prefix, eight
digits starting with 6 alone or grouped `612 34567`, `6123 4567`, `6 1234 567` (a landline without a prefix stays unflagged),
never a date, year span, sum, law number or a link's digits; an `https://`
address is a link (`CommentLinks`, `rel="nofollow ugc noopener noreferrer"`). `Comments:OnReasons|OnStatements|OnResponses`
(off: no new comments, the written ones readable), `Comments:PerPersonPerTarget` (0), `RateLimits:WriteComment` (20 an hour).
"Sutinku" is public with names, needs no positions consent, is not blocked by a writing restriction, and the first one asks
inline; it is a toggle like "Man svarbu", the word the same in both states and its icon (`Icon` `agree`) filled while agreed;
the names read "Sutinka: A, B ir dar 5", "ir dar 5" a `details.more` opening the full list (owner, 2026-11-17). The discussion follows its target (`CommentCascade`): withdrawn or merged goes (the merge confirmation says how
many), hidden hides, a closed bill or a target no longer writable is read-only (`comment.read-only`; a hidden comment cannot
be deleted or edited, `comment.hidden`). Member controls render only once the island runs, so the served HTML is the same for every
member and visitor. An open "Aptarimas" stays in its side's column (owner, 2026-11-17). The thread is `Lt2.Web.Client/Comments/CommentThread` (inside the arguments' and statements' islands, its own island
under responses), the caller's answer `CommentState` (browser only), reports target `comment`, moderators' list
`CommentModerationView`/`CommentQueueItem`, actions also on the bill's page. The author's notice is one kind of the rhythm,
`CommentNotices` (Notifications; each line links to the first new comment it reports with `BillTextLinks.ToComment`;
outbox kind `email.comment-notice` alone, its own part of the gathered email; switch
`comments`, the last on the follows screen). A person's name links to their public profile when they have one (8.25.4).
A link to a comment or an argument (design 8.23.6, the designer's proposal of 2026-11-17) is made only by `BillTextLinks`
(`ToComment`, `ToReason`; one file in SharedKernel compiled also into the contracts): `?komentaras={id}#comment-{id}` or
`?argumentas={id}#reason-{id}`. `BillPage` resolves the comment once (`GET /comments/{id}/place`, public, one 404
`comment.not-found` for hidden and unknown) and hands `Linked` to `RecordReasons`, `OrganisationPositions` and
`BillResponses` as a parameter (so an island starts in the same state): the target is shown even beyond its side's first
page (first in its side's column) or the responses' paging, its "Aptarimas" open through the linked one on the thread's own pages, 10 then +20 (`BillTextLinks.ShownThrough`,
`CommentPaging.Asked(…, linked)`), the comment marked `comment--linked` (an argument `said--linked`); anything not shown
changes nothing. The thread's API pages by `limit` and `cursor` (`CommentRules.Limit`, the first page at least ten, a later one 20 when not asked; the answer's
`NextCursor`); "Rodyti daugiau komentarų" is `<ShowMore>` (`#comments-more-{target}`, `#comments-shown-{target}`): by the
address (`?komentarai=`, `CommentPaging`) without script and before the island runs, and once it runs `CommentThread.MoreAsync`
reads the next 20 (`CommentLimits.Step`) in place and focuses the first added without scrolling (owner, 2026-11-19: the running island had let enhanced navigation
fetch the page and kept its own list, so nothing changed).

**Moderation.** Anything that reaches MPs has passed a person. Every action is audited and an argument's author gets the reason.
A report button is on every bill's description and reason, never on one's own text (owner, 2026-11-17: argument, comment in
any voice, a represented organisation's statement, one's MP's response, one's profile; `AppealRules.CheckReport`,
`report.own` where reports are written) and not for a moderator in a staff-passkey session, whose "⋯" holds only the
moderators' actions (decided once the island runs). The author of a hidden argument or comment, and the representatives of
a hidden statement, see it in its place, greyed (`said--hidden`, `comment--hidden`), with "Paslėpė moderatorius: …", from
their own never-cached answer only (`HiddenStatements`, `Hidden`, `HiddenStatement`; `AppealsService.HideStatementsAsync`;
owner, 2026-11-17). Every member whose report was open is told the decision by email (owner, 2026-11-17, DSA art. 16(5)):
`ReportNotices` (Appeals; called by `ModerationRecorder` when an action answers reports, and by the withdrawals) and Identity's
profile actions enqueue `email.report-decided` (`ReportDecidedEmail`, `ReportOutcomes`, `ReportDecidedTemplate`) with the
explanation when there is one (the "Palikti" note, "Paaiškinimas pranešusiems"), never the moderator or other reporters;
account deletions send nothing. A moderator can restrict an account from writing (reasons,
date flags, statements, responses and, as proposed, changing the display name), until a date or until lifted, with a statement emailed to the person; it never touches sides,
marks, counts or sign-in, is never shown to anyone else, and goes with the account (no address hash is kept).
