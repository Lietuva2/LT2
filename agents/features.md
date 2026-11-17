# Where each feature lives in the code

Read the part for the feature you change, after its design file ([design/README.md](../design/README.md) maps section numbers to files). The design is the rule; this file says how the code carries it out (class, route and test names) and records the owner's decisions as built.

**Stages, as the owner ordered them.**

| Stage | What |
|---|---|
| **M0 Foundation** | Solution skeleton, passwordless sign-in, security plumbing, deployment, monitoring. |
| **M1 MP profiles** | Every MP of the current term imported from Seimas open data: mandate, faction, committees, contacts, voting record, statistics. |
| **M2a Appeals without sending** | People take a side on a Seimas bill and give reasons; summaries for MPs are generated as previews only. |
| **M2a+ Reasons to come** | The agenda page (`/darbotvarke`) and the front page built on it, following bills and MPs with emails, the AI draft of a description (Claude API, off until configured), the share line (design 8.11). Its asking for an appeal and appeals opened by moderators were replaced by M2a++. |
| **M2a++ One object** | Every active bill open at once, one page per bill, no proposals, requests or approval; lists and agenda on participation; moderation after the fact (design 8.12). Then descriptions with their labels and the AI summary job (8.13). |
| **M2d Profiles** | (Owner, 2026-11-16: before comments.) The account area as four screens; public profiles by the member's choice, "Apie mane", the name's card, following a member, follower numbers on every followed thing's own page (design 8.21, 8.25, 8.11.2); the supported party on the public page by its own box and consent (8.25.9, owner 2026-11-17). |
| **Email rhythm** | (Owner, 2026-11-16.) How often a member's emails come, one delivery for every member email kind, the gathered email (design 8.24). |
| **M2c Comments** | (Owner, 2026-11-16: after M2d.) "Aptarimas" under arguments, statements and responses, "Sutinku", moderation, the argument author's notice in the rhythm; names link to public profiles (design 8.23, 8.25). |
| **M2b Sending** | Real summary emails to MPs, MP preferences, profile take-over, MP responses. Built first (owner, 2026-11-15): the link of design 8.5.1–8.5.4 (recognised at once, the take-over, the office), then the response (8.5.5–8.5.7). Then (2026-11-16) sending itself, design 8.19: `Digest:Sending`, review a day ahead (owner, 2026-11-17: no approval needed), the real email, preferences without an account, the introduction letter, the suppression list; off by default. Then (2026-11-16) design 8.20: the followers' email about MPs' answers and known browsers for sign-in. |
| **Stage 3** | Voting with delegation, AI summaries, analytics. Not designed yet; do not build towards it beyond what the design says. |

**Emails.**
- Every email is built by `EmailLayout` (Notifications/Templates) and rendered from its outbox kind by one
  `EmailRenderer<T>` registered in `NotificationsModule`; shared texts use the `Email.*` keys of `EmailTemplates.resx`.
  `Build` and `BuildMp` take the footer (`EmailLayout.Footer(l, site.Value.Address)`: the privacy notice's full address,
  design 8.22.3), so no email goes without it. In `Build`'s HTML part (owner, 2026-11-17) a line ending "text: https://…" is that
  text (its last sentence) as a link with the address under it in small type, any other address a plain link
  (`EmailLayout.LinkedHtml`); a template writes an action line with `EmailLayout.LinkLine(label, url)`; the plain part is unchanged
  and nothing is rewritten or tracked.
  Deleting an account or purging a pending one calls `IOutbox.ForgetAsync` in the same transaction.


**Navigation (design 8.21, built 2026-11-16; owner, 2026-11-17).** The menu is the logo, "Projektai", "Seimo nariai", "Statistika"
(`/statistika`, design 8.26) and "Prisijungti" or the account item "Paskyra"; one line from 360 px (`MenuTests`, E2E). "Projektai" is
a `<details>` dropdown (`#projects-menu`, closed by `js/lt2-account.js` like the account item) holding its three tabs and is current
(`aria-current` on its summary) on them and everything under them (`MenuSections`); the three tabs ("Darbotvarkė", "Projektai",
"Suvestinės") are also `<ProjectTabs>` under the heading of `/darbotvarke`, `/projektai`, `/suvestines`. The history of votes is no tab (the
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
mapped write), except `TermsExemptions.Routes` (accepting, signing out, deleting the account, the MP mail token routes, a
member's stop link `POST /notices/{token}/stop`, and
(owner, 2026-11-17) withdrawing a consent: `DELETE /me/profile/public`, `DELETE /me/consents/positions`,
`DELETE /me/party-support`; giving one again stays refused). The export is a read. The version stays 2026-11-17 until the
launch although the text changed while building (owner, 2026-11-17): nobody accepted an earlier one. The server-rendered feedback form asks the same question. Pending accounts accept with their registration;
the invented demonstration members get the consent like their others (`--demo-appeal`, repeatable).

**Accounts.** Registration is by email only, by invitation during the closed trial (an MP office member's invitation counts
as one, owner 2026-11-17, design 8.5.3), with a self-declared age of 18 or older. An account is pending until its address is proven; the display name, the age declaration and the terms of use are
asked after the code, never before. One account per normalised address. Each account has a `verification_level` (`email` now); phone
verification is a later decision. Every member has one personal invitation code (replaceable; uses unlimited unless
`Registration:PersonalCodeUses` is set) and sees only how many registered with it; administrators see numbers per day,
code and level, never who invited whom, and can switch codes off. Invitations are never combined with sides.
A member changes the display name on `/paskyra` (`PUT /me/display-name`, `DisplayNameHandlers`; the designer's proposal
awaiting the owner): the registration's rule (60 characters, empty allowed), refused under a writing restriction (asked of
Appeals through `IWritingRestrictions`), as often as the member likes (owner, 2026-11-16; `RateLimits:Participation` guards
against a runaway script), audited without the names, no earlier names kept. Names are read live everywhere, so a
change shows beside older arguments too.

**The account screens (design 8.21; the owner, 2026-11-16: "split into several forms", replacing the one page in four
parts; under `/paskyra`, the owner, 2026-11-16: "the url sveiki reads strange, change it"; `/sveiki` is gone, no redirect).** Four screens, each its own page with its own forms, joined by `<AccountTabs>` (`Lt2.Web.Client/Components`, the
`#account-parts` row of plain links, the current one `aria-current`, no script), with the same title and the terms note:
"Profilis" `/paskyra` (level and roles, the display name `MyDisplayName`, "Apie mane" and "Viešas profilis" `MyProfile`, party
support `MyPartySupport`), "Sekimas ir laiškai" `/paskyra/sekimas` (`FollowList`, `#sekami`; `EmailRhythm`, `#laisku-daznis`), "Dalyvavimas" `/paskyra/dalyvavimas`
(`MyMp`, `MyOrganisations`, `MyInvitationCode` `#my-invitation`, the staff part), "Saugumas ir duomenys" `/paskyra/saugumas`
(passkeys, sessions, known browsers `#zinomos-narsykles`, the positions consent, export and deletion `#my-data`, signing out).
The addresses are `AccountScreens` (`Lt2.SharedKernel/AccountScreens.cs`, also compiled into Lt2.Web.Client): links in emails, legal texts and pages are built from it, never
written by hand. A visitor sent to sign in from one comes back to it (`LandingPath` accepts `/paskyra…`). Without a page to
return to, signing in, a staff session and accepting the terms land on the front page `/` (owner, 2026-11-17); a
registration just completed lands on "Profilis", the MP's take-over on "Dalyvavimas". Every screen is
`no-store` and carries `<NeedsScript />`; `IslandsBeforeStartTests` walks all four.

**Public profiles (design 8.25; the owner, 2026-11-16: "implement those", before comments).**
- Only by the member's switch "Viešas profilis" on "Profilis" (`PUT /me/profile`), which is the consent `public-profile`
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
- The page (`Lt2.Web/Components/Members/MemberPage`, composed by the host from Identity, Notifications and Appeals) shows the
  name, "Apie mane", the follower number and the member's texts (`IMemberTexts`, Appeals): published arguments and the
  comments they wrote in their own voice that the bill's page shows, on bills whose record is not hidden, newest first, 10 and "Rodyti daugiau" by `?rodyti=` (no script). Never sides, "Man svarbu", follows, invitations,
  organisations, anything written as an organisation or MP, "Sutinku" (the designer's proposal), counts of the person's
  texts, or who follows.
- **The party on the page (design 8.25.9, owner 2026-11-17).** The party already on "Profilis" (`identity.party_support`,
  `MyPartySupport`), never a free field, shown as "Palaikoma partija: …" on `/nariai/…` only while the profile is public and
  the box "Rodyti viešame profilyje" is on: its own versioned consent `party-public` (`PartyPublicConsent`, SharedKernel; the
  form's `PartyPublicLimits.ConsentVersion`), `PUT|DELETE /me/party-support/public` (`PartySupportHandlers.ShowAsync|HideAsync`;
  the DELETE is on `TermsExemptions.Routes`), read for the page only through `IPublicProfiles.ShownPartyAsync`
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
  (`mpOffer`), shown on `/paskyra/dalyvavimas` by `MyMp`, as one line on "Profilis" by `MyMpLine` and (owner, 2026-11-17) as the
  notice `#mp-offer-notice` under the header of every page but the take-over page and "Dalyvavimas" (`MainLayout`, hidden markup
  the same for every member, shown by `js/lt2-account.js` from one `GET /me` per page load), in the browser only; the page the
  server renders holds nothing of it (`ServerMeApi` leaves `mpOffer` and `mp` out). Nothing is linked or public before the take-over.
- `Registration:OfficialAddressesWithoutInvitation` (off by default, the designer's proposal awaiting the owner): both
  invitation gates ask `OfficialAddressGate`. The local `.env` switches it on through the migrator.
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
  (`POST /admin/mps/{slug}/link/end`, statement, fresh confirmation; no screen yet) and deleting the account end the
  link and the office with it. An office member gets copies of the MP's summaries by their own choice (owner, 2026-11-17; see
  "Copies for the MP's office" under Summaries to MPs); any end of the office link ends them. Every change is audited and told to the MP's account's own address (`AccountNotices.Mp*`),
  never naming an office member.
- The MP's own account takes no part (decided by the owner, 2026-11-17): no side, argument, "Man svarbu", party
  support or organisation (`mp.takes-no-part`, `MpAccounts` in SharedKernel); it follows like anyone; office members are not
  restricted. Its forms are not shown to it (design 8.5.4): the islands ask `MyAccountState.IsMpAccountAsync` (the browser's
  `GET /me`, `mp.part` = `mp`, never while prerendering, so the served HTML is the same for every member) and the front
  page's rows `GET /me/bills`'s `mpAccount`; `SidePicker` shows one line to the responses instead, `RecordReasons` the
  number without "Man svarbu" and no "Rašyti argumentą", `MyAgendaSide` the numbers only; `MyPartySupport` (unless a party
  is still set), `MyProfile`, `MyOrganisations`'s "Registruoti" and `/organizacijos/nauja`'s form go; `CommentThread` already
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
  once; every change keeps the replaced text as a public version (`revision`, kind `response`, without a person); a withdrawn
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
  profile's `MpAnswers` (newest first; no part without responses); both show 10 and "Rodyti daugiau" by `?atsakymai=` landing
  on `#atsakymas-{n}` (`ResponsePaging`). Each item is `MpResponseItem` with the one mark "Seimo narys arba jo biuras". The
  linked account's form is the island `MyMpResponse`, filled in the browser only (on an open bill nobody answered yet it
  stands alone with `Section`, bringing the part's sheet and heading for a linked account); reports are `ReportResponse` islands.
- `/seimo-narys/projektai` (8.5.7, `MpBills`, linked from `MyMp`; `GET /me/mp/bills`): the bills in summaries now in the
  summaries' order (`Lt2.Notifications.DigestLists.InSummariesNow`), with the MP's own response or "Atsakyti"; then (owner,
  2026-11-17) every other bill the MP answered, any state, newest first, without the counts ("Dabar suvestinėje nėra",
  "Atsakyti" only while open); a code session may read it; nothing about other MPs. A first response follows the bill for
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
- One position per person per bill, private, changeable until the bill closes at the final Seimas vote.
- Reasons: plain text, public with the author's display name, for either side, whatever the
  author's own (no side needed; the writing box preselects the author's side if any). Nothing public may tell whether
  a reason is on its author's side. The author can withdraw their own reason while the bill is open. Writing a reason
  needs no positions consent: it is the author's own deliberate publication, public with name and chosen side.
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
- "Man svarbu" endorses a reason on your own side, never one you wrote (owner, 2026-11-17: `AppealRules.CheckEndorsement`,
  `endorsement.own-reason`; the island disables it on one's own argument from the never-cached answer; a merge drops a mark
  that would land on its giver's own argument). A mark moves nothing on the page: `RecordReasons` keeps the shown order
  after the person's own action (`StableOrder`, the order by marks returns on the next read) and gives the pressed button
  the focus back without scrolling. It is a toggle whose button stays neutral, only the heart (`Icon` `heart`, `.icon__fill`)
  filling in the brand's green while marked (owner, 2026-11-17); a visitor's is a `LandingPath.SignInLink` back to the bill
  of the same look. One hint beside the arguments' heading, "Kas yra „Man svarbu“?" (`Reasons.EndorseWhat`, owner
  2026-11-17), says when it can be marked (a side first, one's own side, not one's own argument), "Plačiau" to
  `/metodika#argumentai`; the same served markup for everyone, gone for the MP's own account once the island runs.
- **The bill's page (design 8.12.1, owner 2026-11-17).** The header: "Projektai / Įstatymo projektas XVP-0412" (no label
  line), the title, one `.follow-line` (the stage tags, "Planuojama …", then `FollowButton`). Beside the side column: what
  it is about, the sides, a closed bill's result; the side column: "Stadijos Seime" with `DateFlagForm` under the stages, the
  moderators' panels, the summaries. Then at the page's full width: `BillResponses`, `OrganisationPositions` (each only
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
  links to register or sign in, never a form; a member gets "Jūsų projektai" (the bills they follow, and only those: owner,
  2026-11-17, "followed only"; a side alone shows in its row) and their marks in the rows, islands filled
  in the browser from `GET /me/bills` (one request, never prerendered; it also says whether the caller gave the positions
  consent), so the HTML is the same for every member. The timeline is on a `.sheet` (design 8.16); the page has no
  "Organizacijos" block and the introduction no paragraph on following (owner, 2026-11-16: the footer links to
  organisations, the account page registers one, "Sekti" says what it sends); the visitor's head (owner, 2026-11-17) is the
  headline (the lead sentence as `h1`; the title stays "Lietuva 2.0") and the trial note under it with the invented-data
  notice's edges, the turning logo centred to their right from 54rem (across the invented-data notice too, which on this
  page is the head's first row, not the layout's), beside the headline on a phone. "Kas yra riba?" is one hint beside the note under the timeline, as on `/darbotvarke`. A coming bill with an open record offers "Prašau balsuoti" with "už" and "prieš" in its row (owner,
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
  plus published arguments; the default), planned date or the latest activity (owner, 2026-11-17: the page offers these
  three, `rikiuoti` = `aktyvumas`, `data`, `naujausia`; another value falls back to the default; the API's `sort` has
  `activity`, `participants`, `arguments`, `planned`, `latest`), with a search and the link "Planuojami per 14 dienų"
  (applies at once, keeps the search; `aria-current` while on); each row has "už … (…) · prieš … (…)" beside its bar, and in
  the latest-activity order "Paskutinė veikla {yyyy-MM-dd HH:mm}". The last activity is one time per bill,
  `appeals.bill_record.last_activity_at`, set by `RecordActivity` (Appeals) in the same transaction as a side taken, changed or
  withdrawn, an argument published, a comment written, an organisation's statement or an MP's response made public or
  changed; never who acted.
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
  "Kas yra sekimas?" (`Follow.Hint`) whose answer is the kind's `Follow.What.*` (a visitor's `Follow.SignIn.*`) and
  `Follow.Private` (nobody sees who follows; what the number is), "Plačiau" to `/metodika#sekimas`. `FollowerLine` keeps its
  own "Kas yra sekėjai?" only where it stands without the button (`WithHint`: a closed organisation, the member's own page).
- "Seimo narių atsakymai" (design 8.20.1, the fourth switch `responses`), in the person's rhythm (design 8.24): one part per
  followed bill listing the MPs whose response became public or changed stance (`appeals.mp_response.announced_at`,
  `ResponseRules.Announces`; a changed explanation with the same stance is no event) and are still public, with name,
  faction and stance and the link to `#atsakymai`, never the explanation. Read through `IMpResponseFeed` (Appeals); each
  person is told only about answers from after they followed (`FollowRules.ResponsesFor`), and the MP's own account and
  office members never about their own MP (`IMpLinks.SpeaksForAsync`). Recorded in `bill_event` kind `response`, one per
  answer (key: `FollowRules.ResponseKey`, the person number and the moment), with the three-day rule of votes.
- **How often emails come (design 8.24, the designer's proposals in section 12 await the owner).** A member chooses "Iš karto"
  (the default), "Kartą per dieną" or "Kartą per savaitę ir artėjant balsavimui" (`notice_setting.frequency`,
  `GET|PUT /me/notices/frequency`, the island `EmailRhythm` on the follows screen `/paskyra/sekimas`, its own sheet
  `#laisku-daznis` after the switches; audited `notices.frequency-changed` without the choice). One place decides and sends:
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
  (`EmailLayout.StopHeaders`) and, under the account line, "Nebegauti šių laiškų: …/laiskai/nebegauti/{token}"
  (`EmailLayout.WithStopLine`); paths in `MemberMailLinks` (SharedKernel) and `NoticeStopRoutes` (contracts). The token is one
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
**Analytics (design 8.26, owner 2026-11-16).** "Statistika", `/statistika` (`MenuSections.Statistics`; owner 2026-11-17: in the main menu, no tab row) (`Components/Analytics/AnalyticsPage`), public and
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
- Representatives come only by a one-time link (seven days, at most ten; its code shown beside it and typed on
  `/organizacijos/kvietimas`, owner 2026-11-17); they see each other's display names and
  nobody else does, staff included, except a moderator restricting a statement's writer. Nothing ties a
  representative's own side to the organisation. The last one cannot leave, only close it; deleting that account closes it.
- One statement per organisation per bill (`for`, `against`, `remarks`; as long as `Texts:MaxLength` allows, owner 2026-11-16; an https link; with a
  side an optional goal "Siekiame {N} dalyvių · dabar {n}"), in "Organizacijų pozicijos" after the MPs' responses and before
  the arguments, shown only with a statement or for a representative (owner, 2026-11-17: `OrganisationPositions.Shown`). Never
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
- Always both sides, always with the count for accounts older than 30 days, with the most endorsed published reasons,
  without their authors' names (email, public copy and snapshot alike). A reason longer than about 300
  characters is shortened in the email by `TextExcerpt`, with "Visas argumentas: {link}" to it on the
  public copy (`MpMailPaths.ReasonOnPublicCopy` = `DigestRoutes.ReasonAnchor`); the snapshot keeps the whole text (owner,
  2026-11-16). The email prints no other member-written text (the MP's own response as stance and date only).
- The weekly one holds appeals on that week's agenda, or never summarised, or whose participants changed by at least
  the `Digest:NotableChange` share (default 10 %) since the last summary that held them.
- Every summary has a public copy with the same numbers. Whether an MP stopped the summaries is never shown publicly.
  A bill's page links to it as `/suvestines/{day}?projektas={number}` (`DigestRoutes.PageAt`; owner, 2026-11-17): the summaries
  holding the bill first, its item first, no fragment; a day with one summary has one title, its kind and date.
- Timing is corrected by a moderator when people flag a wrong date: correct, hold, or approve one urgent summary.
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
- The email (`MpMailTemplates`, through `EmailLayout.BuildMp`, the MP stream: `Email:Mp:FromAddress`, optional `Email:Mp:Smtp`)
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
  changes the same choice under "Seimo narys" on `/paskyra` (`MyMp`, `GET|PUT /me/mp/mail`, passkey session).
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
(`PreScreening.ContainsPhoneNumber`, every screened text) matches only Lithuanian numbers: `+370`/`00370`, the trunk `0`
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
(`ToComment`, `ToReason`; one file in SharedKernel compiled also into the contracts): `?komentaras={id}#komentaras-{id}` or
`?argumentas={id}#reason-{id}`. `BillPage` resolves the comment once (`GET /comments/{id}/place`, public, one 404
`comment.not-found` for hidden and unknown) and hands `Linked` to `RecordReasons`, `OrganisationPositions` and
`BillResponses` as a parameter (so an island starts in the same state): the target is shown even behind "show all" or the
responses' paging, its "Aptarimas" open through the linked one rounded up to the next ten (`BillTextLinks.ShownThrough`,
`CommentPaging.Asked(…, linked)`), the comment marked `comment--linked` (an argument `said--linked`); anything not shown
changes nothing.

**Moderation.** Anything that reaches MPs has passed a person. Every action is audited and an argument's author gets the reason.
A report button is on every bill's description and reason, never on one's own text (owner, 2026-11-17: argument, comment in
any voice, a represented organisation's statement, one's MP's response, one's profile; `AppealRules.CheckReport`,
`report.own` where reports are written) and not for a moderator in a staff-passkey session, whose "⋯" holds only the
moderators' actions (decided once the island runs). A moderator can restrict an account from writing (reasons,
date flags, statements, responses and, as proposed, changing the display name), until a date or until lifted, with a statement emailed to the person; it never touches sides,
marks, counts or sign-in, is never shown to anyone else, and goes with the account (no address hash is kept).
