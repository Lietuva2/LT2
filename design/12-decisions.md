# 12. Decisions made

What the owner decided or accepted, by date. What is still open is in [12-open.md](12-open.md).

**Confirmed by the owner on 2026-11-12**

- Code on `master`; documents stay on `docs` (section 4).
- Minimum age 18, self-declared (6.2).
- Registration by email only at the start; phone verification is a later decision (6.2).
- Each appeal has its own public threshold, set by the initiator, never below the administrator's minimum. Objections
  reach moderators by email, not through the product; a moderator's number is final for the initiator (8.4).
  **[Superseded on 2026-11-14 by 8.12.2: one threshold for all bills with a moderator's override; objections still reach
  moderators by email.]**
- Timing of a summary is changed by a moderator, on flags from people (8.4).
- An account is pending until its address is proven; the display name and the age declaration are asked after the
  code, and the invitation use is counted then (6.2).
- A sign-in code belongs to the address and can be typed into any browser. Ten codes a day per address from browsers
  that have not signed in before; known devices are built before registration opens (6.2). [built: 8.20.2]
- A staff role works only with a staff passkey, activated by a code the administrator passes on directly. No
  authenticator app (6.2).
- "Balsavo kaip frakcija" counts only votes the MP took part in; an absence shows in the participation figure only, so
  the same absence is not marked twice (7.3).
- The mixed group of MPs is not a faction and has no position; its members, and MPs outside any faction at the time of
  a vote, have no "Balsavo kaip frakcija" figure for those votes (7.3).

**Confirmed by the owner on 2026-11-13**

- The sides taken on a hidden (rejected) appeal are kept, for now; to be looked at again (8.3).
- A dry-run summary does not lock the initiator's threshold; only the first summary really sent does (8.4). **[The
  initiator's threshold went with 8.12; a dry run still does not count as a summary sent.]**
- Summaries carry no authors' names beside the reasons: the email, the public copy and the stored snapshot (8.4, 8.7).
- "Changed notably" for the weekly summary is a runtime setting, `Digest:NotableChange`, default 10 % (8.4, 5.6).
- A reason may be written for either side, whatever the author's own; the author's side is only the preselected
  choice, and a person without a side may write too (8.2). Writing a reason needs no positions consent: the author's
  own deliberate publication, public with name and side and withdrawable (8.2); a lawyer reads this with the consent
  texts once, before going live (below).
- Opening an appeal asks only for the neutral description and the threshold: no side, no first reason, no positions
  consent. The initiator writes reasons and takes a side on the published appeal like anyone else, or never (8.1).
  Earlier first reasons stay as ordinary reasons; nothing marks a reason as the initiator's. **[Superseded on 2026-11-14
  by 8.12: nobody opens an appeal.]**
- Reasons and "Man svarbu" marks per person per bill are runtime settings, both 0 (no limit) at the start; a limit
  switched on later counts only shown or waiting reasons and removes nothing (8.2, 5.6).
- An author can withdraw their own reason while the appeal is open, and not once it is closed (8.2, 8.8).
- Administrators set and lift writing restrictions like moderators (8.3, 6.3).
- Feedback from members: a balloon on every page and `/atsiliepimai`; linked to the account only if the member allows a
  reply; staff list, note and reply; one notice a day to the team; kept 12 months (8.10).
- Personal invitation codes: one per member, unlimited by default (`Registration:PersonalCodeUses`), replaceable,
  counted at completed registration on the code and per day; members see only their count; administrators see numbers
  per day, per code and per level, never who invited whom, and can switch codes off and on (6.2).
- A moderator can restrict an account from writing, until a date or until lifted, with a statement emailed to the
  person; it is not public, touches no side, mark or count, and goes with the account; no hash of the address is kept.
  A full lock that stops sign-in is left for later (8.3).
- Several proposals per bill until one is published: one waiting or returned proposal per person per bill, no cap per
  bill; the opening form says how many wait (a number only), which closes the earlier open question whether the page
  says an appeal waits; a moderator publishes one (guidance: the earliest that meets the rules), the others close and
  their authors are emailed without names (8.1).
- The initiator may propose a change to the description of an open appeal; it shows only once a moderator approves it,
  and the history page lists each approved change. Telling participants about it is M2b (8.1).
- Moderators can restore a hidden appeal or reason, with a statement, audited, the author told (8.3). Not decided by
  the owner yet: restoring while only proposals wait counts as publishing and closes them (8.3, proposed).
  **[Superseded on 2026-11-14 by 8.12.4: no proposals, no initiator's description changes; restoring a hidden record
  brings it back, one record per bill.]**
- Moderation actions are separate controls, each with its own fields and one sentence on what it does (8.3).
- Dates typed into a form are a text field in the form yyyy-MM-dd; dates shown as numbers are yyyy-MM-dd (with HH:mm,
  Vilnius time); dates written in words in sentences stay as they are (`AGENTS.md`).

**Direction agreed by the owner on 2026-11-13, details proposed (8.11)**

- The site must be worth visiting before appeals exist: an agenda page and a front page built on it; following a bill
  or an MP with emails; asking for an appeal in one click; appeals opened by moderators, also used to seed the site
  before inviting; an AI draft of the description for the moderator, from pasted official text; a share line tied to
  the threshold. No AI-written reasons. **[Asking for an appeal and appeals opened by moderators were superseded on
  2026-11-14 by 8.12.]**
- **AI provider for the description draft [decided by the owner on 2026-11-13]:** the Claude API (Anthropic, first-party),
  for the draft only; the model a setting (`Ai:DraftModel`, default `claude-sonnet-5-5`); only the bill's number and title
  and the pasted official text are sent. Pre-screening of users' texts stays rule-based: those texts can hold personal
  data and this API has no EU-only processing (inference global or US-only, storage in the US); AI pre-screening would
  need an EU-region route chosen first.
- To confirm on the built result: the menu item "Darbotvarkė"; the wording of every new text. **[A third point, that
  follower numbers are not shown at all, was replaced by the owner on 2026-11-16: every thing that can be followed
  shows how many follow it, and who follows stays private (8.11.2).]**

**Decided by the owner on 2026-11-14**

- Organisations get a voice on a bill, not control of it (8.14): a verified profile, one statement per bill, a line
  in the summary to MPs, their own goal and invitation link; no threshold, timing, description or data about people.
  Later that day: anyone may register an organisation, nobody approves it beforehand, moderators may block it or keep
  it out of summaries, "remarks" is offered, the goal counts its own side, and it appears on the site before it appears
  in summaries. Confirmation is by email domain or by a moderator; moderators look at the website themselves, with no
  automatic check of the name. Built now, on the site only, in two stages (8.14.14).
- Emails carry full addresses: `Site:Address`, deployment configuration (5.6).
- A moderator who writes a bill's description also saves it; no second moderator (8.11.3, 8.12.3).
- A bill's AI summary may be shown on the bill's page without a person reading it first, labelled as not reviewed by
  people (8.13). It needs the bills' explanatory notes (e-seimas, closed for maintenance until about 2026-11-16) and
  the provider's key; until both exist only the moderator's form makes descriptions. Built on 2026-11-14 with no reader
  of the notes (8.13): the label, reports, the moderator's actions and the job are in place.
- After seeing the summaries built (8.13, the last block): the summary job gets its own public prompt; the
  "Moderavimas" panel on the bill's page shows the reports about the description; a moderator may record the official
  text's address for a description they write or correct.
- The bill and the appeal are one object: every active bill is open, nobody starts or approves an appeal, moderation
  acts afterwards (8.12). Confirmed on the built result on 2026-11-14: one threshold for all bills
  with a moderator's override, that hiding closes taking part on a bill, and that a bill once sent stays eligible. The
  list is "Projektai" at `/projektai` (it was "Kreipimaisi").
- Arguments are moderated on the bill's page too, with the queue's actions, from a moderators-only answer (8.12.3).
- `/projektai` can be sorted by activity (the default), participants, arguments or planned date; activity also picks the front page's six cards (8.12.5). Confirmed: activity is a plain sum of participants and published arguments.

**Decided by the owner on 2026-11-16**

Built on 2026-11-16:

- One length for every text members write for others to read: the runtime setting `Texts:MaxLength` (2 000, 0 = no
  limit, a technical ceiling of 20 000) for arguments, organisations' statements and MPs' responses, checked where each
  is written (5.6, 8.2), and for comments and "Apie mane", except texts a moderator reviews anyway;
  long ones are shown with "Rodyti daugiau" (8.2: an argument beyond about 300 characters, a statement or a response
  beyond about 1 500).
- Reasons in the email to MPs are shortened like the page, with a link "Visas argumentas" to the reason on the public
  copy; the snapshot keeps the whole text (8.4, 8.19.3). (It was an open question while reasons were capped at 500.)
- On a bill's page "Data neteisinga?" sits under the planned date, with the hint "Iš kur ši data?" and the agenda link
  beside it, and "Stadijos Seime" heads the side column (8.4, 8.12.1).
- The front page's head lines up (a larger logo beside the title and the lead, the trial note under both); the paragraph
  on following and the "Organizacijos" block are gone from it, and following is explained at `/metodika#sekimas`
  (8.7, 8.16).
- A staff account's session becomes a staff session without signing out, by its staff passkey on `/prisijungti/raktas`
  (6.2, 8.21).
- The account page `/paskyra` is in four parts: "Profilis", "Sekimas ir laiškai", "Dalyvavimas", "Saugumas ir duomenys"
  (8.21). [Replaced later the same day, below: four screens.]
- A member may change their display name, with no limit on how often; the account keeps no time of
  the change (6.2).
- The terms and the privacy notice are kept correct as drafts by us as the site changes, and the lawyer reads them once,
  before going live, not per change (8.22).
- The site counts its own use: visitors by IP address and browser, members by account, now, by day, month and year,
  with pages and referring sites; no cookie, no script, no third party, no banner; salts kept until the period ends;
  approximate numbers are enough (8.27). Members are counted by year as well, though the year's sketch lets anyone
  holding the whole database list the accounts active that year until it ends (8.27.2).

Decided for comments, the email rhythm and public profiles (8.23-8.25; all three built 2026-11-16):

- **Comments (8.23; replaces "comments dropped" of 1.3).** Members comment in public under an argument, an
  organisation's statement and an MP's response: one implementation for the three kinds, built together, MPs'
  responses included (against the earlier advice to wait for the talks with offices). A runtime switch per kind (three,
  in place of the one first offered). No moderation beforehand; nothing in the moderators' list unless reported; every
  comment can be reported and a reported one reaches the list; a moderator moderates a comment on the bill's page too.
- Later the same day, on the draft of 8.23: `Comments:OnResponses` starts on like the other two, tried during the trial
  and switched off when going live if needed (decided again before real MPs are on the site, below); pre-screening
  refuses a comment only for an email address, a phone number or a personal code, and a link is allowed (shown as a
  link, 8.23.3); "Sutinku" beside every comment, a button that only shows support (8.23.10).
- Also that day, as directions: every member chooses how often their emails come (8.24); a member may say publicly who
  they are (8.25).
- Then, on the open points: no link cards ("the link is enough for now"; 8.23.3 keeps the reasons); "Iš karto" means
  within minutes, at the follow job's runs, with one email per person per run gathering everything of that run, which
  also changes 8.20.1's timing for MPs' answers; the weekly choice is "Kartą per savaitę ir artėjant balsavimui", and a
  "planned" event whose sitting comes before the person's next gathered email is sent at once, alone, for the daily
  choice too; comments and "Apie mane" come under the one cap above; "Apie mane" has no length of its own and allows
  links.
- And last: "Iš karto" is the default rhythm (8.24); a person's own email address and phone number are allowed in "Apie
  mane", someone else's left to reports (8.25.3); "Sutinku" is public with the names of those who agreed, a deliberate
  publication needing no positions consent (8.23.10; this replaced the proposal that the first "Sutinku" ask that
  consent); the direction of public profiles: hovering a name opens a card, pressing it goes to the person's page, and
  members may follow ordinary persons (8.25); and a person is followed only with their explicit consent (8.25.1).
- Then, on the profile (8.25): "A public profile would get indexed and allowed to be followed automatically. Also
  let's show the number of followers, why not?" One switch, "Viešas profilis", is the consent (kind
  `public-profile`) and covers the page, its indexing, being followed and the shown number; the separate "Leidžiu
  mane sekti" is gone; the page is indexed (no `noindex`); the number of followers shows on the profile page, who
  follows stays private, the person included.
- **Follower numbers for everything that can be followed (8.11.2).** "If we're planning on voice delegation later on,
  that will probably break this rule anyway. So let's have the following to be like a predecessor of delegating." A
  bill, an MP, an organisation and a member with a public profile each show how many follow it; who follows stays
  private from everyone, the followed party and staff included, with no list of followers and nothing about one
  follower in any answer, log or admin screen; follows stay out of any combination with sides and invitations. It
  replaces the rule that no follower count is shown, and with it the member's number as its one exception. Following is
  written down as the predecessor of delegation (1.1, 11). The numbers of bills, MPs, organisations and members
  were built with the public profiles, 2026-11-16 (11, "Follower counts"). Two limits are the
  designer's proposals (below): counts, not rankings; and a follow never becomes a delegation by itself.

**Decided by the owner later on 2026-11-16: public profiles now, and the account as screens**

- "Yes, i missed public profiles too, implement those": public profiles (8.25) are built now, before comments (M2d
  before M2c, section 11); this replaces the designer's proposal "its own milestone after comments". The follower numbers
  of bills, MPs and organisations are built with them (8.11.2). The designer's proposals of 8.25 and 8.11.2 are built as
  proposed and still await the owner (below and in the table).
- "The menu could not just scroll, but it would be split into several forms? Starting with name, about me and probably a
  party": the account area is four screens, `/paskyra` ("Profilis": the name, "Apie mane", "Viešas profilis", party
  support), `/paskyra/sekimas`, `/paskyra/dalyvavimas`, `/paskyra/saugumas`, with the row of parts as navigation between them
  (8.21). The party shown publicly was then the designer's proposal of 8.25.9; the owner decided it on 2026-11-17 (below).
- "The url sveiki reads strange, change it": the account's screens are `/paskyra`, `/paskyra/sekimas`,
  `/paskyra/dalyvavimas`, `/paskyra/saugumas`; `/sveiki` goes with no redirect (8.21).
- A member's public address carries the name: `/nariai/{name-slug}-{id}`, the id deciding, any other name part
  redirecting permanently to the current address (8.25.2). The owner suggested the name alone; the id stays (names are
  neither unique nor stable), and the owner did not object.

**Decided by the owner on 2026-11-17: the party on the public profile**

- "Add party to public profile": 8.25.9 as proposed: the supported party, by its own box "Rodyti viešame profilyje" and
  consent `party-public`, only while the profile is public; withdrawn at once with the box, the party or the profile and not
  given back by switching the profile on again; nothing for the MP's account; the "at least 20" rule unchanged (made a
  setting later the same day, below).
- "I can now 'Man svarbu' on my own argument": an argument's author cannot mark it (8.2); a merge drops a mark that would
  land on its giver's own argument.
- "The view is scrolled away after clicking 'Man svarbu'": a mark moves nothing on the page until it is read again, and the
  pressed button keeps the focus (8.2).

**Decided by the owner on 2026-11-17: party group numbers as a setting**

- "Make that setting, with 0 also possible": the smallest group of a side's participants who support the MP's party for
  that number to be shown in a summary is the runtime setting `Digest:PartyGroupMinimum` (5.6, 6.3, 8.2, 8.4), in place of
  the fixed 20. The owner's reason: taking part is private, nobody can see who took a side at all, and no list ties a side
  to a person inside the system, so a small group number reveals little from within the system. 0 = no floor: any group
  of one or more is shown (an empty group never is, so 0 and 1 act the same). Default 10, the designer's recommendation,
  to which the owner did not object. The lawyer's one reading before launch (the table below, "Legal reading of the
  consent texts") may set the value. Audited and on the settings screen like every setting, from 0 to 1 000; shown on
  `/metodika` ("Grupių skaičiai") as it holds now, and stated by the party form's text; the privacy notice points to the
  methodology page rather than naming a number. A change applies to summaries made after it; one already made keeps its
  numbers. The texts that say it (`UiText` `Party.Text` and `Party.TextAny`, `AppealText` `Method.Digests.GroupsValue`
  and `Method.Digests.GroupsAny`, `LegalText` `Privacy.Data.Fact7.Value` and `Privacy.Positions.2`, the settings screen's
  `Admin.*.Digest:PartyGroupMinimum`) go to the native reader and the lawyer with the others. [For the owner and the
  lawyer: the party support consent (`party-support`, version 2026-11-13) is given against the form's text, which states
  the value as it holds then; a later lower value changes what that consent described.]
- Not changed by it: the analytics page's fixed sample of 20 (8.26), organisations (8.14: no group numbers at all), the
  follower numbers (8.11.2: exact from 1), the "Nesiųsti" statement's 20 characters (8.19.2).

**Decided by the owner on 2026-11-16: the analytics page (8.26)**

- The page is built without delegation: the figures, the Seimas beside the members on closed bills, faction unity and
  the member's own comparison; showing how many MPs took over their profile and setting factions side by side are
  allowed (8.26.1).
- "Kas Seime balsuoja taip, kaip prašote jūs" lists MPs in order of agreement for the member who asks, seen by nobody else: the one
  exception to "no ranking or comparison of MPs by any number" (7.3, 8.26.4). Its heading, renamed after the review: "Kas
  Seime balsuoja taip, kaip prašote jūs" (members do not vote).
- The Seimas's majority is the majority of those who voted; a consensus counts as "for" (8.26.2).
- The page is the fourth tab under "Projektai", "Analitika" (8.21). [Owner, 2026-11-17: "Statistika" in the main menu, `/statistika`.]
- One fixed sample of 20 for every number on the page, not a setting; below it, counts and "maža imtis" and no share
  (8.26.2), faction unity included (after the review of 2026-11-16).
- **Kept copies:** "I think we may cache this quite long, say per-session, with the personal sections like "who votes like
  you" built asynchronously the first time the page is opened, and then saved for the whole session." Built as 8.26.6:
  the public numbers kept for an hour (one copy for everyone per filter), the member's comparison made the first time
  their page asks and kept in server memory for the member until they sign out or stop using it.

**Proposed by the designer on 2026-11-16**

Read by the owner without objection, so written as the rule (the alternative in brackets where there was one):

- Comments (8.23): the part is "Aptarimas (n)", an item a *komentaras*, one level with no replies to replies; the
  discussion follows its target (withdrawn goes, hidden hides, read-only on a closed bill); hiding with a statement as
  for arguments; an argument's author gets at most one email a day, with a switch; off stops new comments and keeps the
  written ones readable; a representative under their own organisation's statement speaks as the organisation; under an
  MP's response the MP's account or an office member comments only as the MP, in a passkey session, marked "Seimo narys
  arba jo biuras".
- Comments oldest first, never by popularity (most agreed first); hiding a comment removes its "Sutinku" with the names
  (kept for a restore); a hidden comment shows nothing in its place ("Komentaras paslėptas" with the statement); a
  ceiling per person per text, `Comments:PerPersonPerTarget`, 0 by default (no setting); a merged argument's comments are
  deleted (moved to the argument it was merged into); no email about comments to organisations or MPs (the same notice
  to representatives); a writing restriction blocks changing "Apie mane" (it does not); the rest of 8.24's shape (the
  choices, what is covered and never delayed, the three-day rule read for gathered emails); and the rest of "Sutinku"
  (one per person per comment, not on one's own, "Atsiimti" while open, the inline sentence on first use, the MP's
  account not marking, representatives and office members marking as persons, the participation limit, not blocked by a
  writing restriction, readable by the read-only role as `reason` is).
- A public profile exists only for a member who switches "Viešas profilis" on (8.25.1): read by the owner three
  times without objection, and the owner speaks of a public profile as something a member has (a profile for every
  member, which would need every existing member told first and a way to refuse before anything is gathered).

Built, awaiting the owner's word:

- Changing the display name (6.2): a writing restriction blocks it (or leaves it alone); audited without the names; no
  earlier names kept.
- Comments, filled in while building (8.23.14): the paging parameter `komentarai`; a `comments` count on each argument,
  statement and response in the public answers; no comment as the MP once the mandate has ended; the argument author's notice
  recorded per argument and run without a person, told in the email rhythm (8.24) as a kind of its own; the
  moderation notices on the existing kinds; moderating a comment whatever its target's state; the "Moderavimas" panel listing
  the bill's reported comments.

**Decided by the owner on 2026-11-17** ("All good, but lithuanian number is now 06.., not 86..")

- Withdrawing a consent works behind changed terms: switching the public profile off and withdrawing the positions and
  the party support consents (8.22.2); nothing else is exempt.
- A restricted member may clear "Apie mane" and switch the profile off; writing or changing the text stays refused (8.25.3).
- The phone rule matches only Lithuanian numbers, with the trunk prefix `0` in use and the old `8` (8.23.3).
- The terms stay at version 2026-11-17 until the launch although their text changed (8.22.2).

**Decided by the owner on 2026-11-17: the bill's page, after a look on a desktop** ("build them as recommended")

- The width: what the bill is about, the sides and a closed bill's result stay beside the side column; MPs' responses,
  organisations' statements and the arguments run at the page's full width below it (8.12.1). An opened "Aptarimas" stays
  in its side's column and nothing else moves; the word "Aptarimas" shows at desktop widths, closed and open alike (8.16.1,
  8.23.6; this settles the open choice of 8.16.1).
- The order: MPs' responses, then organisations' statements, each only when there is something to show (a linked MP's
  account and a representative still get their forms), then the arguments, always (8.5.6, 8.14.3, 8.14.14, 8.12.1).
- "Pranešti", "Atsiimti", "Moderuoti" and a confirmation open directly under the action row, above an open discussion
  (8.16.1).
- The header: "Projektai / Įstatymo projektas XVP-0412" with no label line of its own; one compact line with the stage
  tags, "Sekti", "Sekėjai: n" and the hint "Kas yra sekimas?", which takes the sentence that stood beside the button; the
  same line on an MP's, an organisation's and a member's page (8.11.2). "Data neteisinga?" in "Stadijos Seime" (8.4).
- "Man svarbu" and "Sutinku" are toggles of one look: the button neutral, the icon filled in the brand's green while
  marked; "Sutinku" keeps its word in both states; a visitor's "Man svarbu" is a sign-in link back to the bill (8.16.1,
  8.23.10). "Sutinka: A, B ir dar 5", where "ir dar 5" opens the full list (8.23.10).
- The author's name above an argument's text, as above statements, responses and comments (8.16.1).
- "Data neteisinga?" takes an optional "Tikroji data (jei žinote)", `yyyy-MM-dd`, shown to the moderators (8.4).
- Back to a bill's page keeps the reader's place (8.15): found in a real browser on the local stack, after following a
  link from the bill's page (a stage's vote) and pressing Back. Blazor's enhanced navigation patches the page back into the
  one just left; the browser's scroll anchoring followed a node the patch moved far down the page, and an "Aptarimas" opened
  on the earlier visit was opened again during the patch, so the page landed near its bottom (y 252 → 3 728 of 4 628 in the
  measurement). Built: the disclosures' memory (`lt2-disclosures.js`) is of the page being read only, emptied whenever the
  path changes; a small script (`lt2-return.js`) keeps in memory where the reader stood per address and holds that place
  on Back or Forward while the page is patched (scroll anchoring off until Blazor reports the load), the reader's own
  scrolling winning. Nothing is stored in the browser (8.22.1). Measured again: the place stays (252 of 4 142), the
  discussion stays closed.

**Decided by the owner on 2026-11-17: a moderator's actions as one button and a menu** ("this doesn't look good; a
single button with the dropdown options maybe", of the bill's "Moderavimas" panel)

- Replaces 8.3's "each action is its own control": one filled button for what the item needs most in its state, and
  "Kiti veiksmai" opening the rest in the browser's popover; a bill's under "Aprašymas", "Seime" and "Suvestinės",
  "Paslėpti" set apart at the end; the same on every moderation screen (8.3, "The screen"; 8.16.1). The designer's
  choices: with both a report and an unread AI summary, "Palikti" stays the filled button (it also closes date flags) and
  "Patvirtinti aprašymą" heads "Aprašymas"; with no filled button the trigger reads "Veiksmai"; a blocked organisation has
  no filled button.

**Decided by the owner on 2026-11-17: passkey sessions and the MP's own account** (approved to build)

- A passkey session stays one: the security stamp's check every 30 minutes no longer turns an MP's, an office member's or a
  staff member's passkey session into a code session; a renewal never changes what a session was signed in as (6.2).
- "Patvirtinti prieigos raktu" in place wherever an action needs a passkey session (speaking for an MP, a linked account's
  passkey) or a staff session (the staff screens and the account page): any passkey of the signed-in account, the staff
  passkey for staff; another account's passkey refused (6.2, 8.5.4).
- A sign-in never silently switches the account: a sign-in in a browser signed in to another account ends that session and
  the page says "Prisijungėte kaip kitas narys: {vardas}." (6.2).
- The MP's own account takes no part (8.5.4: until then the designer's proposal), and the forms it would be refused are
  not shown to it (8.5.4).
- A bill's number and title are one link in the MP's list of bills and in the follow list, as on a member's page.

**Decided by the owner on 2026-11-17: summaries reviewed a day ahead, no approval (8.19.2)**

- A weekly or before-vote summary is prepared `Digest:ReviewHours` (24) before it goes: its bills and arguments are fixed
  then; its numbers are read when it goes. The arguments are the most endorsed published ones, seen by a moderator or not
  (8.3).
- Hiding an argument during review brings the next of its side in, marked "Įtrauktas vietoj paslėpto"; a bill held during
  review drops out.
- No approval is needed: if nobody acts it goes at its time. "Patvirtinti" marks it reviewed (it still goes at its time;
  no fresh passkey confirmation); "Nesiųsti" declines it with a public statement. The `approval` mode, "Siųsti",
  `Digest:ApprovalHours`, `waiting` and `expired` are gone; `Digest:Sending` is `off` or `automatic`.
- Moderators and administrators are told by email at preparation and by a box on every page until it is approved,
  declined or gone. The screen shows each summary's common text once; no per-MP preview.

**Decided by the owner on 2026-11-17: copies of the summaries for an MP's office (8.19.3)**

- Every office member of a linked MP gets a copy of each summary that goes for that MP, at their own account's address, in
  the copy's format, with their own frequency (the MP's four choices; default on), independent of the MP's; their own
  delivery row and weekly ceiling; the suppression list applies; sending off or a lock sends none. Ending the office link
  ends the copies and deletes the choice. Never public.
- The designer's choices: the member changes the choice on "Dalyvavimas" in any session (their own mail, not speaking for
  the MP); the copy's one-click stop goes to the MP's one-click route with the member's own token (so one route stays the
  only write outside the same-origin check) and its "Nebegauti kopijų" line to the copies' page; the MP's own account has no
  copies (it gets the summary); the stop sends no confirmation email; a dry run makes no office rows.

**Decided by the owner on 2026-11-17: `/projektai` by the latest activity (8.12.5)**

- A third order, "Naujausia veikla", by one time per bill: the last side, argument, comment, organisation's statement or
  MP's response; bills without any last; each row then shows "Paskutinė veikla …". Nothing per person is kept. The
  designer's choices (8.12.5): the link reads "pagal naujausią veiklą"; the same side again, a restored argument and a
  statement or response on a bill without a record are no activity.

**Proposed by the designer while building the stop link of members' emails (8.24.1), 2026-11-17; accepted by the owner on 2026-11-17**

Built as written; "no" names what changes.

1. One press of "Nebegauti šių laiškų" (the footer's "atsisakyti laiškų" since 2026-11-17; or a mail program's "Unsubscribe") turns every email switch off, whichever email it
   came from: planned, voted, statements, MPs' answers, followed members, comments. The follows, the follower numbers and
   the choice of how often stay; switches turned on again on `/paskyra/sekimas` undo it. If no: the press turns off only
   the kinds the email in hand held (a gathered email: all of its parts), with a token per account and kind.
2. No confirmation email after a stop, and nothing audited when every switch was already off. If no: one short email
   saying the emails stopped, with the link to turn them on again (a confirmation into the list just left).
3. The page names nobody and answers the same for any token, valid or not; the link does not expire and has no "new link"
   (generation 0 for every account). If no: an expiry, or a way to replace the link from the account page.
4. The texts: the email line "Nebegauti šių laiškų" [replaced by the owner on 2026-11-17: the footer's "atsisakyti
   laiškų", below]; the page "Nebegauti laiškų", "Paspaudę mygtuką nebegausite laiškų
   apie tai, ką sekate.", the button "Nebegauti laiškų", and after it "Laiškų apie tai, ką sekate, nebesiųsime. Vėl
   įjungti juos galite savo paskyroje." A native speaker reads them before they are shown externally.

**Decided by the owner on 2026-11-17: the footer of members' notice emails and plain links (8.24.1, 8.11.2)**

- Every email of the rhythm ends with its reason (one sentence, no link), "Keisti periodiškumą arba atsisakyti laiškų"
  (the first part to `/paskyra/sekimas#laisku-daznis`, the second to the stop page) and the privacy line, in the footer;
  the body's line "…galite pakeisti savo paskyroje: …" and the line "Nebegauti šių laiškų: …" are gone. The plain part reads
  "Keisti periodiškumą: https://… · Atsisakyti laiškų: https://…". The `List-Unsubscribe` headers are unchanged.
- In the HTML part of a member's email a line "text: https://…" is the text as a link, bold in the site's green as before; the
  address is no longer printed under it. Every link in the footer (the privacy notice's included, and the MP footer's) is the footer's grey and underlined.

**Decided by the owner on 2026-11-17: what is new on followed bills (8.11.8); the designer's choices (the owner accepted 2, 3, 4, 6, 7 and 8 on 2026-11-17; 1, 5 and 9 await the owner)**

Each is built as written; "no" names what changes.

1. A visit is the page's island running in the browser; a page read without script keeps nothing. If no: a server-side
   record on GET, which the owner ruled out.
2. A statement or a response edited after the visit is new again; an argument restored by a moderator, or a response
   withdrawn, is not. If no: only a first publication counts.
3. The member's own texts include their organisations' statements and comments (written by a co-representative too) and
   those of the MP they speak for (the MP's account and the office). If no: only what the account itself wrote.
4. The counts stand only in "Jūsų projektai" and the follow list, not in the agenda's or `/projektai`'s rows. If no: also
   there, for followed bills.
5. A bill's count goes away only when its page is opened, not when the list is seen.
6. Under "Rodyti tik naujus" a side column with nothing new is left empty, the representatives' own forms for their
   organisation's statement step aside, and the responses' part goes when none stands. If no: the parts stay with a line
   "Naujų nėra".
7. Only the responses shown on the page (the first ten, or as many as asked) are marked; one beyond them is counted in the
   lists but needs "Rodyti daugiau".
8. Each part of the count carries its own "nauj-": "3 nauji argumentai · 5 nauji komentarai" (the owner's example read "3
   nauji argumentai · 5 komentarai"); statements read "organizacijos pozicija" (not "pozicija", which is a member's side),
   responses "atsakymas". If no: the adjective once, or other nouns.
9. The visit's write shares the limit of a page's own answers (`own-read`) rather than `Participation`, so reading pages
   never uses up the allowance for taking sides.

**Decided by the owner on 2026-11-18: the version histories show what changed (8.16.2)**

- Every public version history (a bill's description, an organisation's statements, an MP's responses, the introduction
  letter) lists the newest first and shows each version against the one before it, word by word, the oldest whole; inserted words in `<ins>`,
  removed in `<del>`, neutral marks with hidden "pridėta:" and "pašalinta:"; the text before each change whole under "Rodyti tekstą prieš pakeitimą" (it was the text after it, "Rodyti visą tekstą", until the owner saw it).
  An organisation's names and websites stay "A → B", also newest first. One pure function (`TextDiff`), no new library, its work capped.

**Decided by the owner on 2026-11-18: the lists' lengths with a real-sized parliament (8.7, 8.11.1, 8.12.5, 8.18, 8.3)**

- Resolves "Found with the stand-in's real-sized parliament", 2 (the front page's "Balsuota" length). The front page shows
  the next two sitting days and the last two voting days, five rows of each, with "Visi N projektų →" to the day on the
  agenda page and "Dar N balsavimų →" to the day on the history of votes; at most twenty rows. [Both links replaced the
  same day by "Rodyti likusius" in place, below.]
- A day's rows go by activity (participants plus published arguments), then stage (priėmimas, svarstymas, pateikimas; a
  combined stage as its furthest), then number; bills the open data does not know yet last, folded into one line.
  Followers never rank anything; a member's followed bills are not moved up in the server's page ("Jūsų projektai" is
  their place, and finds them on the whole agenda).
- The agenda page: every day a block with its count, ten rows, then "Rodyti visus N" opening the rest without script;
  the recently voted list by day the same way. It reads the front page's minute-old copy. [Superseded the same day, see
  "the rest of a day on request" below: the hidden rows are no longer in the page.]
- `/projektai` and an MP's votes grow in place by the address (`?rodyti=`), as the history of votes does; the arguments'
  "show all" works without script (`?argumentai=visi`); every moderators' list says "Rodoma 100 iš N" when cut.
- The numbers (2 days, 5 a day, 10 a day, 20 a page and so on) are the design's, not settings.

**Decided by the owner on 2026-11-18: the Seimas import by hand (7.2)**

- The scheduled import stays exactly as it was: one recurring job, `parliament-import`, asked every 15 minutes and
  importing when due, with its freshness rules per request.
- Added beside it, four manual jobs an administrator starts with "Trigger now" on the jobs dashboard, never on a schedule:
  `parliament-members`, `parliament-sittings`, `parliament-votes` and `parliament-everything` (all three, MPs before
  votes). Each forces its part of the same walk, ignoring that part's freshness rules; nothing else about the import changes.
- The scheduled job triggered when it is not due is not silent: a log line at information level and a parameter on the
  job's dashboard page say so, with the last start.

**Decided by the owner on 2026-11-18: the rest of a day on request, and how a vote went (8.7, 8.11.1, 8.18)**

- The agenda page holds only what it shows: ten rows a day, then "Rodyti likusius" (no number), a link to the same page
  with that day whole (`?diena=` a planned day, `?balsuota=` a voted one), landing on the first row added; the bills the
  open data does not know yet come with the rest, as the day's last line. Without script the server renders the day
  whole; with script the site's enhanced navigation fetches the page and swaps it in, without a reload. The day head
  keeps its count ("40 projektų"); "Rodyti visus N" is gone.
- The front page the same way: five rows a day, "Rodyti likusius" opens the rest of a planned or a voted day in place
  (`/?diena=`, `/?balsuota=`); "Visi N projektų →" and "Dar N balsavimų →" are gone. One link to the whole agenda stays
  by the heading ("Visa darbotvarkė"), and "Ankstesni balsavimai →" to the history of votes under the last voted day.
- Every voted row says how the vote went, in a neutral chip beside the numbers. Asked as "Priimta" / "Nepriimta"; the
  open data has no official outcome and 8.26.2 says "balsavusiųjų dauguma", never "priimta", so it is built as the
  majority of those who voted ("Dauguma už", "Dauguma prieš", "Nėra daugumos"; a consensus "Pritarta bendru sutarimu")
  with one hint beside the list. The owner chose these words (2026-11-18: "Keep Dauguma už"): no "Priimta" /
  "Pritarta" read from the numbers, which cannot see qualified majorities, MPs registered but not voting, or the quorum.
  If the live contract run once the service reopens finds an official outcome (a result field, or a result text in the
  proceedings), that is a new question for the owner.
- "Dar niekas nenurodė, ko prašo" reads "Niekas nenurodė, ko prašo".

**Decided by the owner on 2026-11-18: the list of closed bills on `/statistika` (8.26.1 item 2, 8.26.2, 8.26.6)**

- The comparison of sides lists every closed bill of the period with a side, one row each with its title linking to the
  bill, a thin track with the Seimas's and the members' shares and the line at 50 %, and the exact numbers written under
  it (no hover needed). The consensus case and the sample of 20 stay as they were.
- Filters for that list: days of the closing vote ("nuo", "iki", typed `yyyy-MM-dd`, a malformed one ignored with a note),
  the search by title or number, the result (majorities agreed or differed), the Seimas's majority (už, prieš, nėra
  daugumos), and the order (newest closing vote, largest difference, most positions). No stage filter: the comparison
  uses only the closing vote. The accounts filter stays and combines.
- Ten rows, then "Rodyti daugiau" by the address (`?rodyti=`), as the other long lists.
- Built as proposed, awaiting the owner's look (2026-11-18): the four figures and faction unity follow only the period and
  the accounts counted, never the list's filters; the list's filters sit in a second form above the list, the period's
  form at the top; "Rodyti lentelę" is removed, because every row carries what its table row did; the filters work over
  the kept copy in memory, so they add no copies and no counting.
**Asked by the owner on 2026-11-18: one green, the sides' pair, the threshold reached, straight bubbles (8.12.2, 8.16.3)**

- The "už" side is the links' green and "prieš" a red of the same lightness and strength, wherever a side is a colour
  (buttons, switches, figures, bars, the seat map, the squares). This replaces the moss of the prototype, chosen so that
  "for" would not read as Lietuva 2.0 taking a side.
- The threshold's bar is the same green, lighter; once the threshold is reached the bar gives way to words: "Riba
  pasiekta – balsuoti prašo n dalyvių." and the next summary's planned day, or the last one's, or that it is not known
  yet. The owner's draft said "yyyy-MM-dd bus siunčiama suvestinė Seimo nariams"; built as "Suvestinė Seimo nariams
  planuojama yyyy-MM-dd." (with the trial's "nesiunčiama" while sending is off), because the day is a plan the
  moderators and the agenda can change. Awaiting the owner's look and a native reader.
- The bubbles are straight boxes with a small radius and a tail pointing up to the name; the menu's chosen item is
  underlined in the same green; an outlined side button's hover fill has its outline's shape.

**Decided by the owner on 2026-11-18 (later the same day): straight buttons, one pair of side colours (8.16.3)**

- The corner cut leaves every button, filled or outlined (the designer's recommendation of the open question "which
  elements carry the cut corner"); it stays on the page's main sheet and the portraits. The front page's step numbers
  lose it too.
- The dark theme uses brighter versions of the light theme's side colours, `#217740` and `#b4433f`, with white words, as
  the light theme does: they meet 3:1 against the dark sheet and ground, so no shape in a side's colour has an edge. (The
  first build of this decision kept the light pair, `#1b6034` and `#85322f`, in the dark theme with a light 1px edge
  round every shape under 3:1; the owner chose the brighter colours instead the same day.) The threshold's progress is
  the same `#3a8e54` in both themes; the dark menu keeps its own link green, since the light one is under 3:1 on the
  dark ground.
- A bubble's tail always points at its author's name: the line right above every bubble is the name. A stance stands
  above the name as a small line; the marks ("Seimo narys arba jo biuras", "Patvirtinta", "Organizacija"), an MP's
  faction and the dates go under the bubble (the designer's choice between "beside the name when it fits" and "under
  the bubble": under, so nothing wraps onto the line above the bubble on a phone). A bubble with no name above it has
  no tail. Awaiting the owner's look.
