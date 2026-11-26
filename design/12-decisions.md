# 12. Decisions made

The owner's decisions that hold today, by section of the design, each with the date it was made or last changed. The
sections say how each is built; what is still open is in [12-open.md](12-open.md).

## 4. Solution layout

- A feature's parts are named alike across the solution, not kept in one folder (section 4); `AGENTS.md` says so
  (2026-11-19).

- Product code on `master`; the concept website and the discovery notes stay on `docs` (2026-11-12).

## 6. Security and data protection

- Administrators set and lift writing restrictions as moderators do (6.3, 2026-11-13).
- The positions consent covers both a person's side on bills and the arguments they mark "Man svarbu"; its text is
  version 2026-11-18. A consent to an earlier text is not the one in force: the person is asked again before a new side or
  mark, and nothing of theirs is deleted (6.5, 2026-11-18).
- The account page shows the positions consent and its withdrawal for any consent not withdrawn, whatever text it was
  given to ("show any live consent"); a consent to an older text has one line more, with the text in force to read and
  accept there (6.5, 2026-11-19).
- A public profile switched on under an earlier consent text stays on (the earlier consent covered it); the person is asked
  once, on the account page where the switch is, by one line and a way to read the text in force and accept it; switching the
  profile off stays one step; no email, no banner elsewhere (8.25.1, 2026-11-19, "go ahead with the designer's recommendations").

## 6.2 Sign-in and accounts

- Minimum age 18, self-declared (2026-11-12).
- Registration by email only (2026-11-12).
- Accounts are confirmed by email now; the EU Digital Identity Wallet is added once Lithuania has one, optional and
  gating nothing a member does today. Its uses: counts of confirmed persons beside the totals (8.4, 8.12, 8.26), a
  requirement for roles that act for others (a delegate, an organisation's representative, with a transition period for
  existing representatives), the mark "Patvirtintas asmuo" on a public profile, proof of age instead of the declaration,
  looser limits, and no 30-day wait for a confirmed person's positions. It stores only the fact, its date, the age proof
  and a pairwise pseudonymous identifier (or its keyed hash) under a unique index; never a name, a personal code or a
  document number (6.2, 2026-11-19).
- An account is pending until its address is proven; the display name and the age declaration are asked after the code,
  and an invitation's use is counted then (2026-11-12).
- A sign-in code belongs to the address and can be typed into any browser; ten codes a day per address from browsers that
  have not signed in before, with known browsers (8.20.2) (2026-11-12).
- A staff role works only with a staff passkey, activated by a code the administrator passes on directly; no
  authenticator app (2026-11-12).
- A staff account's session becomes a staff session without signing out, by its staff passkey on `/prisijungti/raktas`
  (8.21, 2026-11-16).
- Personal invitation codes: one per member, unlimited by default (`Registration:PersonalCodeUses`), replaceable, counted
  at completed registration on the code and per day; members see only their own count; administrators see numbers per
  day, per code and per level, never who invited whom, and can switch codes off and on (2026-11-13).
- A member may change their display name as often as they like; the account keeps no time of the change (2026-11-16).
- Display names are not unique; only the email address is, one account per normalised address (2026-11-19).
- A member can change the account's email address; an account has one address, no second one (6.2, 2026-11-19).
- Changing the email address, as designed (6.2, 2026-11-19: "Yes, build it"); "no" would have changed:
  1. The change starts only in a passkey session or after a code to the address in use, so a stolen session cannot move
     the account. If no: any signed-in session.
  2. The account's other sessions end when the address changes. If no: they stay.
  3. The old address gets a one-time link for seven days that puts it back and ends every session. If no: the email says
     to write to the moderators, and an administrator's tool puts the address back.
  4. An address held only by a pending account is not taken: the change proves it and the pending account goes. If no:
     it is taken until the pending account is deleted after seven days.
  5. The email to the old address shows the new one in part (`r***@pavyzdys.test`). If no: it shows none.
- A passkey session stays one: no renewal or security-stamp check turns it into a code session or changes what it was
  signed in as (2026-11-17).
- "Patvirtinti prieigos raktu" in place wherever an action needs a passkey session (speaking for an MP, a linked
  account's passkey) or a staff session: any passkey of the signed-in account, the staff passkey for staff; another
  account's passkey is refused (8.5.4, 2026-11-17).
- A sign-in never silently switches the account: signing in where another account is signed in ends that session and the
  page says "Prisijungėte kaip kitas narys: {vardas}." (2026-11-17).

## 7. MP profiles

- The profile header links only the current faction and committees. All current memberships are listed under
  "Pareigos Seime" (7.3, 2026-11-24).
- The profile's recent votes show recorded choices by the MP. "Visi balsavimai" leads to the full mandate history
  (7.3, 2026-11-24).
- "Balsavo kaip frakcija" counts only votes the MP took part in; an absence shows in the participation figure only
  (7.3, 2026-11-12).
- The mixed group is not a faction and has no position; its members, and MPs outside any faction at the time of a vote,
  have no "Balsavo kaip frakcija" figure for those votes (7.3, 2026-11-12).
- The scheduled Seimas import stays one recurring job with its freshness rules; beside it, manual jobs an administrator
  starts on the jobs dashboard, never on a schedule, force the MPs, the sittings, the votes or everything (MPs before
  votes); the scheduled job triggered when not due says so in the log and on its dashboard page (7.2, 2026-11-18).
- A bill's number and title are one link in an MP's list of bills and in the follow list, as on a member's page
  (2026-11-17).
- The bills registered or changed in TAIS are imported periodically from `p2b.tais_docs`, the latest data in a readable
  form (7.2, 2026-11-25).
- A moderator corrects a vote's classification (7.2, 2026-11-20: "build it"). The designer's choices as built: a moderator
  or an administrator in a staff-passkey session, on the vote's page under "Moderavimas", with a required statement,
  audited (`vote.reclassified`, `vote.classification-reset`), undone by "Grąžinti taisyklių klasifikaciją"; only the kind
  and a stage result's stage are corrected (the existing codes), never the totals, ballots or wording; the correction is
  kept apart from the rules' result and wins on every read and every re-parse; a vote corrected into the adoption stage
  closes the record at the closing job's next run, and a record's closing vote is never corrected out of it (refused,
  `vote-classification.closing-vote`), since a record closed by the vote is never reopened; the vote's page says
  "Klasifikaciją pataisė moderatoriai" beside the stage, never who; nothing enters the moderators' queue.

## 8.1 Life of an appeal

- A bill rejected by the Seimas or withdrawn by its initiators before its adoption stage closes with the reason, date
  and official source. The counts freeze and followers are told. A moderator can close the record with a statement or
  undo a wrong close. Use the official data when it confirms a decision (8.1 step 6, 2026-11-19).

## 8.2 Rules

- An argument may be written for either side, whatever the author's own; the author's side is only the preselected
  choice, and a person without a side may write too. Writing one needs no positions consent: it is the author's own
  deliberate publication, public with name and side, and withdrawable (2026-11-13).
- Arguments and "Man svarbu" marks per person per bill are runtime settings, 0 (no limit) at the start; a limit switched
  on later counts only shown or waiting arguments and removes nothing (5.6, 2026-11-13).
- An author can withdraw their own argument while the bill is open, not once it is closed (8.8, 2026-11-13).
- One length for every text members write for others to read: the runtime setting `Texts:MaxLength` (2 000, 0 = no limit,
  a technical ceiling of 20 000) for arguments, organisations' statements, MPs' responses, comments and "Apie mane",
  except texts a moderator reviews anyway; checked where each is written; long ones shown with "Rodyti daugiau"
  (5.6, 2026-11-16).
- "Man svarbu" may be given on an argument of either side, with or without a side of one's own, never on one's own
  argument (a merge drops such a mark), with the positions consent, within `Appeals:EndorsementsPerPerson`. A mark counts
  only while its giver asks for the argument's side; the figure, the order and the summaries use only counted marks;
  taking, changing or withdrawing a side deletes no mark; marks stay private (2026-11-18; own argument 2026-11-17).
- A mark moves nothing on the page until it is read again, and the pressed button keeps the focus (2026-11-17).
- Pressing the side one has takes it back; there is no separate "Atsiimti" for sides (it stays for one's own argument and
  for "Sutinku") (8.7, 8.23.10, 2026-11-18). No line under a pressed side button explaining this; onboarding is to cover
  it (2026-11-19).

## 8.3 Moderation

- A moderator can restrict an account from writing, until a date or until lifted, with a statement emailed to the person;
  it is not public, touches no side, mark or count, and goes with the account; no hash of the address is kept. A full
  lock that stops sign-in was left for later (2026-11-13) and is built (2026-11-19, below).
- The administrator's end of an MP's link has its screen: the first section of "Paskyros", a list of the MPs whose profile is
  taken over with "Baigti ryšį" opening the statement field in place (8.5.2, 2026-11-19).
- A full lock that stops signing in is an administrator's action, never a moderator's, with a required statement and a fresh
  passkey confirmation (2026-11-19, "go ahead with the designer's recommendations"): sessions end, no sign-in works and the
  answers reveal nothing, the public profile is not shown and the follows send nothing, texts stay as they are; the person
  gets one email with the statement and how to write to the team, erasure being asked for by writing (a locked person cannot
  sign in to delete); unlocking likewise; a staff account with an active role and the administrator's own are not locked.
- Moderators can restore a hidden record or argument, with a statement, audited, the author told (2026-11-13).
- The sides taken on a hidden record are kept, to be looked at again (2026-11-13).
- A moderator's actions are one filled button for what the item needs most in its state and "Kiti veiksmai" opening the
  rest; a bill's grouped under "Aprašymas", "Seime" and "Suvestinės", "Paslėpti" set apart at the end; the same on every
  moderation screen (8.16.1, 2026-11-17).
- Each moderator's explanation is labelled by who reads it: the author by email ("Paaiškinimas autoriui"), everyone on a page
  ("Viešas paaiškinimas", with the page named under it), or staff only ("Paaiškinimas moderatoriams", "Mato tik moderatoriai
  ir administratoriai."); where nobody receives it as its author the refusal is "Parašykite paaiškinimą." (8.3, 2026-11-22).

## 8.4 Summaries to MPs

- The upcoming agenda is entered by staff a day at a time from the e-seimas agenda documents, through a browser agent,
  until the Seimas gives programmatic access to TAIS; the watcher stays as built and will replace the entry then. An
  entered day creates the bills the site does not know (8.4, 2026-11-25).
- The timing of a summary is changed by a moderator, on flags from people (2026-11-12). "Data neteisinga?" takes an
  optional "Tikroji data (jei žinote)", shown to the moderators (2026-11-17).
- A dry-run summary does not count as a summary sent (2026-11-13).
- Summaries carry no authors' names beside the arguments: the email, the public copy and the stored snapshot (8.7,
  2026-11-13).
- "Changed notably" for the weekly summary is the runtime setting `Digest:NotableChange`, default 10 % (5.6, 2026-11-13).
- Arguments in the email to MPs are shortened as on the page, with a link "Visas argumentas" to the public copy; the
  snapshot keeps the whole text (8.19.3, 2026-11-16).
- The smallest group of a side's participants supporting the MP's party for that number to be shown is the runtime
  setting `Digest:PartyGroupMinimum`, from 0 (no floor; an empty group is never shown) to 1 000, default 10; shown on
  `/metodika` and stated by the party form; the privacy notice points to the methodology page. A change applies to
  summaries made after it. The owner's reason: taking part is private and no list ties a side to a person, so a small
  group number reveals little from within the system (5.6, 6.3, 8.2, 2026-11-17). It does not change the analytics
  sample of 20 (8.26), organisations (no group numbers) or follower numbers (exact from 1).
- Each bill in a summary shows, beside the two most endorsed published arguments of a side, how many published arguments
  that side has ("Dažniausi argumentai už (iš 14):"), only when it has more than two. No total of "Man svarbu" marks:
  summing marks counts the same people more than once (8.4, 8.19.3, 2026-11-20).

## 8.5 MPs on the platform

- The MP's own account takes no part, and the forms it would be refused are not shown to it (8.5.4, 2026-11-17).
- `Registration:OfficialAddressesWithoutInvitation` is on for the trial site, as on the local stack; off by default
  elsewhere (8.5.1, 2026-11-19).

## 8.7 The front page and the lists of the agenda

- The front page's head: a larger logo beside the title and the lead, the trial note under both; no paragraph on
  following and no "Organizacijos" block; following is explained at `/metodika#following` (8.16, 2026-11-16).
- The front page shows the next two sitting days and the last two voting days, five rows of each (8.11.1, 2026-11-18).
  No row of links ("Visi projektai", "Suvestinės Seimo nariams", "Seimo nariai") under its agenda (2026-11-19).
- The front page's planned days and voted days are two sheets, "Artimiausiu metu Seime" and "Neseniai balsuota" (both
  h2), each with its long form's link beside the heading ("Visa darbotvarkė →", "Ankstesni balsavimai →"; on
  `/darbotvarke` "Ankstesni balsavimai →" beside the recent votes' heading too). A list's heading row stays at the top of
  the screen while its sheet scrolls past, at every width; from 54rem a day's date stays just under it. Lists of bills
  (the front page, `/darbotvarke`, `/projektai`, an MP's bills) show one number a side, without the count for accounts
  older than 30 days in brackets, and nothing under them: no note on the brackets, no hints ("Kas yra riba?", the
  majority) and no "not a representative poll" notice. That notice stays on the bill's page, the summaries (page and
  email), the statistics page and the methodology, and the counts for accounts older than 30 days on the bill's page,
  the summaries, the moderators' summary screen and the statistics (8.7, 8.11.1, 8.12.5, 8.16.5; owner, 2026-11-21).
- A day's rows go by activity (participants, published arguments and, since 2026-11-25, followers), then stage
  (priėmimas, svarstymas, pateikimas; a combined stage as its furthest), then number; bills the open data does not know
  yet last, folded into one line. A member's own followed bills are not moved up for them ("Jūsų projektai" is their
  place) (8.11.1, 2026-11-18).
- A longer day grows by the one "show more" ("Rodoma N iš M · Rodyti daugiau", by the address), as every list does (8.16.4); M is the day's count; the unknown bills come as the last line once every known
  row shows (8.11.1, 2026-11-19).
- Every voted row says how the vote went in a neutral chip: the majority of those who voted ("Dauguma už", "Dauguma
  prieš", "Nėra daugumos"; a consensus "Pritarta bendru sutarimu"), never "Priimta" read from the numbers, which cannot
  see qualified majorities or the quorum (8.18, 2026-11-18).
- The agenda's rows are the front page's, one component: a coming bill's row offers "Prašau balsuoti" "už" and "prieš";
  a visitor's are sign-in links back to the bill's page; a member's parts are filled in the browser, so the served page
  holds nothing of anyone's own. "Jūsų projektai" and the greeting stay on the front page only (8.11.1, 2026-11-19).
- Fewer texts in the rows: the title is the way to the bill's details; no "Nurodyti, ko prašote →", "Ko prašo,
  argumentai →", "Argumentai ir pozicijos", or "Niekas nenurodė, ko prašo" beside the side buttons (a row without
  buttons keeps it) (8.11.1, 2026-11-19).
- No "Dabar sekate šį projektą." after a first side in a row: the mark's "Sekate" lights up anyway (2026-11-19).
- Every moderators' list says "Rodoma 100 iš N" when cut (8.3, 2026-11-18).
- The lists' numbers (days shown, rows a day, page sizes) are the design's, not settings (2026-11-18).

## 8.10 Feedback

- Feedback from members: a balloon on every page and `/atsiliepimai`; linked to the account only if the member allows a
  reply; staff list, note and reply; one notice a day to the team; kept 12 months (2026-11-13).

## 8.11 Reasons to come

- The site is worth visiting before anyone takes part: an agenda page and a front page built on it, following a bill or
  an MP with emails, an AI draft of the description for the moderator from pasted official text, a share line. No
  AI-written arguments (2026-11-13).
- The AI provider for the description draft is the Claude API (Anthropic, first-party), for the draft only, the model a
  setting; only the bill's number and title and the pasted official text are sent. Pre-screening of users' texts stays
  rule-based: those texts can hold personal data and the API has no EU-only processing (8.11.4, 2026-11-13).
- A moderator who writes a bill's description also saves it; no second moderator (8.11.3, 8.12.3, 2026-11-14).
- Every thing that can be followed (a bill, an MP, an organisation, a member with a public profile) shows how many follow
  it; who follows stays private from everyone, the followed party and staff included, and follows stay out of any
  combination with sides and invitations. Following is the predecessor of delegation (1.1): "let's have the following
  to be like a predecessor of delegating" (8.11.2, 2026-11-16).
- The page header's follow line: the stage tags, then one compact line with "Sekti", "Sekėjai: n" and the hint "Kas yra
  sekimas?"; the same compact follow control on a bill's, an MP's, an organisation's and a member's page and for
  "Nebesekti" in the account's list (8.11.2, 8.16.3, 2026-11-18).
- What is new on followed bills (8.11.8): no server-side record of a visit on a page read; a statement or response edited
  after the visit is new again, a restored argument or a withdrawn response is not; the member's own texts include their
  organisations' statements and comments and those of the MP they speak for; the counts stand only in "Jūsų projektai",
  the follow list and the member's marks in the timeline's rows; under "Rodyti tik naujus" an empty side column is left
  empty; only the responses shown are marked; each part carries its own "nauj-" ("3 nauji argumentai · 5 nauji
  komentarai"), statements reading "organizacijos pozicija" (2026-11-17; the timeline's rows 2026-11-19).
- "Jūsų projektai" has no share line; the bill's page keeps it (8.11.5, 2026-11-19).

## 8.12 One object: every bill is open

- No record has an initiator: the column `bill_record.initiator_user_id` and the description source `initiator` are gone
  (8.12.4, 8.13, 2026-11-19).

- The bill and the appeal are one object: every active bill is open on its own page, nobody starts or approves an appeal,
  and moderation acts afterwards. The list is "Projektai" at `/projektai` (2026-11-14).
- One threshold for all bills, with a moderator's override; objections reach moderators by email, not through the
  product (8.12.2, 2026-11-14).
- Hiding a record closes taking part on the bill; a bill once sent stays eligible (2026-11-14).
- A reached bill's list rows (the front page, the agenda, `/projektai`) name the summary's day instead of "riba
  pasiekta": "suvestinė planuojama {yyyy-MM-dd}" when one that would hold it is planned, else "suvestinė išsiųsta
  {yyyy-MM-dd}" for the last one really sent, else "riba pasiekta"; the rows show everything as it would be live, with no
  trial variants (no "bandomoji", a dry run is not "išsiųsta"). The bill's page names a bill reached only through an
  earlier summary sent, its participants now under the threshold: "Projektas jau buvo suvestinėje Seimo nariams –
  balsuoti prašo {n} dalyvių." (8.12.2, 2026-11-21).
- Arguments are moderated on the bill's page too, with the queue's actions (8.12.3, 2026-11-14).
- `/projektai` orders by activity (the default; a plain sum of participants, published arguments and, since
  2026-11-25, followers, which also picks the front page's cards), by planned date, or by the latest activity: the last side, argument, comment, organisation's
  statement or MP's response, each row then showing "Paskutinė veikla …"; nothing per person is kept; the same side
  again, a restored argument and a statement or response on a bill without a record are no activity (8.12.5,
  2026-11-14 and 2026-11-17).
- The bill's page (8.12.1): what the bill is about, the sides and a closed bill's result beside the side column; MPs'
  responses, then organisations' statements (each only when there is something to show, or a form for who may write),
  then the arguments, always, at the page's full width below it (8.5.6, 8.14.3, 2026-11-17).
- The header reads "Projektai / Įstatymo projektas XVP-0412" with no label line of its own; it stands 16px above the
  page's first part on a bill's page, an MP's profile and an organisation's page (2026-11-17, 2026-11-18).
- "Stadijos Seime" heads the side column; under the stages and their planned date one small line with "Iš kur ši data?"
  and the agenda link, then "Data neteisinga?" (8.4, 2026-11-18).
- The e-seimas link stands once, in "Apie ką projektas"; a page without that part keeps "Projektas e-seime" under the
  stages (2026-11-18).
- On a narrow screen the side column, the moderators' panels with it, comes after everything else, and it is last in the
  markup too; "Tai nėra reprezentatyvi apklausa …" stands in "Ko prašo" under the counts and side buttons on every width
  (2026-11-18).
- The side buttons do not look greyed out while the page's script starts: full colour, a busy state, still not pressable;
  on hover only the fill darkens (8.7, 2026-11-18).
- The arguments a page at a time: each side's first two, then more under each side by the one "show more"; a linked
  argument and the one just published stand first in their side (8.16.4, 2026-11-19).
- Back to a bill's page keeps the reader's place, with nothing stored in the browser (8.15, 8.22.1, 2026-11-17).

## 8.13 The description and the AI summary

- A bill's AI summary may be shown on its page without a person reading it first, labelled as not reviewed by people
  (2026-11-14).
- The summary job has its own public prompt; the bill page's "Moderavimas" panel shows the reports about the
  description; a moderator may record the official text's address for a description they write or correct (2026-11-14).
- A bill may carry a readable title, made and moderated with its description (8.13.1). Lists show it as the link with
  the official title in fine print under it, the tags keeping their line above in every list (the number beside the
  title was tried and dropped: it fits only where the number is the row's one tag); an `ai` title is shown on the site too;
  summaries to MPs keep the official title. Searches of bills match it, not the description (2026-11-24). Off the site
  (link previews; members' emails name the bill by it) the readable title and the description go whatever their source, an `ai` one
  included, the bill's page and a vote's page alike, except while a report about the description is open; summaries to
  MPs stay as they are until it is discussed with the MPs (2026-11-26).
- The description form fills "Oficialaus teksto adresas" with the bill's e-seimas address on a bill without a
  description; every link to lrs.lt opens in a new tab. Moderators find the bills that most want a description in
  "Projektai be aprašymo" (8.3): only bills with a participant, an argument or a follower, the most attention first.
  Organisations do not write descriptions (8.14.7): their view goes in their statement (2026-11-25).
- The moderators' queue is one page of tabs, each list loaded when opened, the tab in the address: work tabs with a
  neutral count chip only when something waits, then, set apart, "Be aprašymo", "Paslėpti" and "Apribojimai" with
  muted counts; the page never opens on those. Each list keeps its cut at a hundred, no paging (8.3, 2026-11-25).
- A moderator's statement on a description (writing, correcting, removing, and a confirmation's optional one) is public:
  shown under its entry in "Aprašymo keitimų istorija", where it says what was read and what was left out on purpose. It
  is the one field, relabelled "Viešas paaiškinimas", not a second one beside a staff note. A change of the address
  alone and a confirmation each get an entry of their own (8.3, 8.13, 2026-11-25).
- The AI fills that statement too, in the draft and the summary job, and names the document its description rests on:
  the explanatory note when it alone is enough, otherwise the bill's text, whose address becomes "Oficialaus teksto
  adresas". Built as if a reader of the official documents existed, tested with a stand-in; without one, pasted text
  keeps working as before (8.11.4, 8.13, 2026-11-25).
- An AI agent writing a description in a moderator's form ticks a box hidden from sight for agents only; the text is
  then `ai`, "Sukurta DI · žmonių neperžiūrėta", until a person confirms or corrects it (8.13, 2026-11-25).
- A description may be up to 3 000 characters, not 1 500 (8.13, 2026-11-25).
- Follower numbers may order lists of bills and be counted into their order; the rule against ranking by followers
  stays for MPs, members and organisations, at whom it was aimed. A bill's row still shows no follower number (8.11.2,
  2026-11-25).
- Every search of bills runs in PostgreSQL: a word is found by its stem (`lithuanian` configuration) or inside the
  text with case and Lithuanian letters ignored; the Appeals module keeps its own copy of the bill catalogue for it
  (`appeals.bill_card`), refreshed from the Parliament module after every import; the search returns the matching
  bills, and the lists keep their own order (2026-11-24).

## 8.14 Organisations

- Organisations get a voice on a bill, not control of it: a profile, one statement per bill, a line in the summary to
  MPs, their own goal and invitation link; no threshold, timing, description or data about people (2026-11-14).
- Anyone may register an organisation; there is no list of who qualifies and nobody approves it beforehand (8.14.2,
  2026-11-14).
- Moderators cannot refuse a registration; they can block an organisation or keep it out of summaries (8.14.9,
  2026-11-14).
- The stance "remarks without a side" is offered (8.14.3, 2026-11-14).
- The goal counts the organisation's own side (8.14.5, 2026-11-14).
- Organisations appear on the site first; the line in the summaries to MPs comes later, by its own decision (8.14.4,
  2026-11-14). That decision: build the line in summaries and "keep out of summaries", as 8.14.4 and 8.14.9 design them;
  the automatic check of an organisation's website is not built, since moderators look at the website themselves
  (8.14.4, 8.14.9, 2026-11-20).
- Whether a person may delegate to an organisation is a later question; nothing here assumes or prevents it (2026-11-14).
- Confirmation is by email domain, or by a moderator for organisations without one; moderators look at the website
  themselves, with no automatic check of the name (8.14.2, 8.14.9, 2026-11-14).
- On a day with few registrations an administrator could match a new account to an organisation's code by its
  registration date; the owner judged the gap too small to change (8.14.6, 2026-11-14).

## 8.15 Explanatory text and wording

- Dates typed into a form are a text field in the form `yyyy-MM-dd`; dates shown as numbers are `yyyy-MM-dd` (with
  `HH:mm`, Vilnius time); dates written in words in sentences stay as they are (2026-11-13).
- Every typed date field has a small calendar button beside it that opens the browser's own date picker and writes the
  picked day back into the text field as `yyyy-MM-dd`; typing stays as it was, the field itself is never `type="date"`,
  and there is no calendar of our own and no library; the placeholder shows the form and no hint line stands under the field (2026-11-23).

## 8.16 Surfaces

- "Pranešti", "Atsiimti", "Moderuoti" and a confirmation open directly under the action row, above an open discussion;
  the author's name stands above an argument's text, as above statements, responses and comments (8.16.1, 2026-11-17).
- "Man svarbu" and "Sutinku" are toggles of one look: a neutral button, the icon filled in the brand's green while
  marked; a visitor's "Man svarbu" is a sign-in link back to the bill (8.16.1, 8.23.10, 2026-11-17).
- Every public version history (a bill's description, organisations' statements, MPs' responses, the introduction
  letter) lists the newest first and shows each version against the one before it, word by word, with the text before
  each change whole under "Rodyti tekstą prieš pakeitimą"; an organisation's names and websites stay "A → B" (8.16.2,
  2026-11-18).
- "Už" is the links' green and "prieš" a red of the same lightness and strength, wherever a side is a colour; the
  threshold's bar is the same green, lighter; the menu's chosen item is underlined in it (8.16.3, 2026-11-18).
- No corner cut on any button, filled or outlined, nor on the front page's step numbers; it stays on the page's main
  sheet and the portraits (8.16.3, 2026-11-18).
- The dark theme uses brighter versions of the side colours, with white words, meeting 3:1 against the dark sheet and
  ground (8.16.3, 2026-11-18).
- The bubbles are straight boxes with a small radius and a tail pointing up to the author's name, the line right above
  every bubble; a bubble with no name above it has no tail (8.16.3, 2026-11-18).
- One "show more" under every long list: "Rodoma N iš M" where the total is known and a text link to the grown page, on
  one line; without script the link, in an island the next page in place, the focus on the first item added (8.16.4,
  2026-11-19).
- The first page of every list stays small and each press brings a larger page: a list of rows adds 50 (the agenda's
  and the front page's days, `/projektai`, the history of votes, a vote's ballots, an MP's votes, a member's texts,
  `/statistika`'s bills), a list of members' long texts adds 20 (arguments, comments, MPs' responses); the caps on a
  page stay (8.16.4, 2026-11-19).
- Every public list's heading row stays at the top of the screen while its sheet scrolls past ("Jūsų projektai", the
  MPs' list, a vote's ballots, a profile's lists and the rest). On a list page whose list's sheet has no heading of its
  own (`/projektai`, the history of votes, an MP's votes and bills, `/organizacijos`) the page's head stays instead:
  the title and the tab row under it; the lead and the filters scroll away, and the count line is an ordinary line
  (8.16.5; owner, 2026-11-21). The statistics' comparison keeps its heading, shortened to "„Už“ dalis: Seimas ir Lietuva
  2.0 naudotojai", in view the same way, on the ground above its filters and sheet (8.16.5, 8.26; owner, 2026-11-21).
- `/projektai`'s "Planuojami per 14 dienų" is a box in the search form above "Rodyti", applied with it, not a link on a
  line of its own; no date range filter, since planned dates reach about two weeks ahead (8.12.5; owner, 2026-11-21).

## 8.19 Sending the summaries

- A weekly or before-vote summary is prepared `Digest:ReviewHours` (24) before it goes: its bills and arguments are fixed
  then, its numbers read when it goes; the arguments are the most endorsed published ones, seen by a moderator or not
  (8.19.2, 2026-11-17).
- Hiding an argument during review brings the next of its side in, marked "Įtrauktas vietoj paslėpto"; a bill held
  during review drops out (8.19.2, 2026-11-17).
- No approval is needed: if nobody acts the summary goes at its time; "Patvirtinti" marks it reviewed and "Nesiųsti"
  declines it with a public statement; `Digest:Sending` is `off` or `automatic`. Moderators and administrators are told by
  email at preparation and by a box on every page until it is approved, declined or gone (8.19.2, 2026-11-17).
- Every office member of a linked MP gets a copy of each summary that goes for that MP, at their own address, with their
  own frequency (default on), delivery row and weekly ceiling; the suppression list applies; ending the office link ends
  the copies; never public. The MP's own account has no copies (8.19.3, 2026-11-17).

## 8.20 MPs' answers and known browsers

- The followers' email about MPs' answers follows the member's rhythm: with "Iš karto", within minutes, at the follow
  job's runs (8.20.1, 8.24, 2026-11-16).

## 8.21 Navigation

- The account area is four screens, `/paskyra` ("Profilis": the name, "Apie mane", "Viešas profilis", party support),
  `/paskyra/sekimas`, `/paskyra/dalyvavimas` and `/paskyra/saugumas`, with the row of parts as navigation; `/sveiki` is
  gone, with no redirect (2026-11-16).
- "Statistika" is in the main menu, at `/statistika` (8.26, 2026-11-17).
- The history of votes is the tab "Balsavimai" of "Projektai": "Darbotvarkė", "Projektai", "Balsavimai", "Suvestinės";
  the history has the tab row under its heading (8.18, 2026-11-19).
- The footer, on every page, repeats the menu's sections in the menu's order and words, as plain links (8.7,
  2026-11-19).

## 8.22 Terms and storage

- The terms and the privacy notice are kept correct as drafts by us as the site changes; the lawyer reads them once,
  before going live, not per change (2026-11-16).
- The terms stay at version 2026-11-17 until the launch although their text changes (8.22.2, 2026-11-17).
- Withdrawing a consent works behind changed terms: switching the public profile off and withdrawing the positions and
  the party support consents; nothing else is exempt (8.22.2, 2026-11-17).

## 8.23 Comments

- Members comment in public under an argument, an organisation's statement and an MP's response: one implementation for
  the three kinds, with a runtime switch per kind, each on at the start; `Comments:OnResponses` may be switched off when
  going live, decided again before real MPs are on the site (2026-11-16).
- No moderation beforehand: nothing in the moderators' list unless reported; every comment can be reported; a moderator
  moderates a comment on the bill's page too (2026-11-16).
- Pre-screening refuses a comment only for an email address, a Lithuanian phone number (trunk prefix `0` or the old `8`)
  or a personal code; a link is allowed and shown as a link, with no link cards (8.23.3, 2026-11-16; phone 2026-11-17).
- The part is "Aptarimas (n)", an item a *komentaras*, one level with no replies to replies, oldest first, never by
  popularity; the discussion follows its target (withdrawn goes, hidden hides, read-only on a closed bill); hiding with a
  statement as for arguments; switched off, no new comments and the written ones stay readable; a merged argument's
  comments are deleted; no ceiling per person per text by default (`Comments:PerPersonPerTarget` 0) (2026-11-16).
- An argument's author gets at most one email a day about comments, with a switch; no email about comments to
  organisations or MPs (2026-11-16).
- A representative under their own organisation's statement speaks as the organisation; under an MP's response the MP's
  account or an office member comments only as the MP, in a passkey session, marked "Seimo narys arba jo biuras"
  (2026-11-16).
- "Sutinku" beside every comment only shows support; it is public with the names of those who agreed, a deliberate
  publication needing no positions consent; one per person per comment, not on one's own, "Atsiimti" while open; it keeps
  its word in both states, and "Sutinka: A, B ir dar 5" opens the full list (8.23.10, 2026-11-16; look 2026-11-17).
- An opened "Aptarimas" stays in its side's column and nothing else moves; the word "Aptarimas" shows at desktop widths,
  closed and open alike (8.16.1, 8.23.6, 2026-11-17).

- A comment in an organisation's name stays when its writer's account is deleted, unless the deletion closes the
  organisation (its last representative): closing withdraws its statements and their comments with them (8.23.7,
  2026-11-19).

## 8.24 Email rhythm

- Every member chooses how often their emails come; "Iš karto" is the default and means within minutes, one email per
  person per run gathering everything of that run; the weekly choice is "Kartą per savaitę ir artėjant balsavimui"; a
  planned event whose sitting comes before the person's next gathered email is sent at once, alone, for the daily choice
  too (2026-11-16).
- One press of the stop link turns every email switch off, whichever email it came from; the follows, the follower
  numbers and the chosen rhythm stay, and switches turned on again undo it. No confirmation email after a stop, nothing
  audited when every switch was already off. The page names nobody and answers the same for any token; the link does not
  expire and has no "new link" (8.24.1, 2026-11-17).
- Every email of the rhythm ends with its reason (one sentence, no link), "Keisti periodiškumą arba atsisakyti laiškų"
  and the privacy line, in the footer; the `List-Unsubscribe` headers are unchanged (8.24.1, 2026-11-17).
- In a member's email a line "text: https://…" is the text as a link, the address not printed under it; every link in
  the footer, the MP footer's included, is the footer's grey and underlined (8.11.2, 2026-11-17).

## 8.25 Public profiles

- A member may say publicly who they are; a public profile exists only for a member who switches "Viešas profilis" on
  (8.25.1, 2026-11-16).
- The one switch "Viešas profilis" is the consent (`public-profile`) and covers the page, its indexing, being followed and
  the shown number of followers; who follows stays private, the person included (2026-11-16).
- Hovering a name opens a card, pressing it goes to the person's page; members may follow ordinary persons, only with
  their consent (the switch above) (2026-11-16).
- A member's public address carries the name: `/nariai/{name-slug}-{id}`, the id deciding, any other name part
  redirecting permanently to the current address (8.25.2, 2026-11-16).
- "Apie mane" has no length of its own (the one cap of 8.2) and allows links and a person's own email address and phone
  number; someone else's are left to reports (8.25.3, 2026-11-16).
- A writing restriction blocks writing or changing "Apie mane"; a restricted member may still clear it and switch the
  profile off (8.25.3, 2026-11-17).
- The supported party is shown on the public profile by its own box "Rodyti viešame profilyje" and consent
  `party-public`, only while the profile is public; withdrawn at once with the box, the party or the profile, and not
  given back by switching the profile on again; nothing for an MP's account (8.25.9, 2026-11-17).
- Members have no uploaded picture (a face beside political texts, moderation load, storage and parsing risk, drift
  towards a social network); a member with a public profile has a round badge with their initials beside the name that
  leads to the profile, in one of eight neutral colours chosen from the profile's public id (8.25.4, 2026-11-19).

## 8.26 Analytics

- The page is built without delegation: the figures, the Seimas beside the members on closed bills, faction unity and
  the member's own comparison; showing how many MPs took over their profile and setting factions side by side are
  allowed (8.26.1, 2026-11-16).
- "Kas Seime balsuoja taip, kaip prašote jūs" lists MPs in order of agreement for the member who asks, seen by nobody
  else: the one exception to "no ranking or comparison of MPs by any number" (7.3, 8.26.4, 2026-11-16).
- The Seimas's majority is the majority of those who voted; a consensus counts as "for" (8.26.2, 2026-11-16).
- One fixed sample of 20 for every number on the page, not a setting; below it counts, "maža imtis" and no share, faction
  unity included (8.26.2, 2026-11-16).
- Kept copies: the public numbers for an hour, one copy for everyone per filter; the member's comparison made the first
  time their page asks and kept in server memory until they sign out or stop using it (8.26.6, 2026-11-16).
- The closed bills of the period are compared in one dot plot drawn by the server, with "Rodyti lentelę"; every bill is
  named by number and title, as a link; the plot fits the screen's width and never scrolls; the table also shows the
  difference and the "Daugumos sutapo" / "Daugumos skyrėsi" chip (8.26.1, 2026-11-19).
- Filters for that list: days of the closing vote ("nuo", "iki"), the search by title or number, the result, the
  Seimas's majority, and the order (newest closing vote, largest difference, most positions); no stage filter; the
  accounts filter combines with them; the filters' form is closed at first and open whenever a field is set
  (2026-11-18, 2026-11-19).
- The trial shows invented members' activity on the real Seimas data: sides, arguments, comments, organisations, sides
  on bills the Seimas finished and past summaries as dry runs, never a description or an MP's response, only where every
  email is caught, never on a bill a real account took part on, with a notice on every page (8.26.5, 2026-11-26).

## 8.27 Usage statistics

- The site counts its own use: visitors by IP address and browser, members by account, by day, month and year, with
  pages and referring sites; no cookie, no script, no third party, no banner; salts kept until the period ends;
  approximate numbers are enough. Members are counted by year as well, though the year's sketch lets anyone holding the
  whole database list the accounts active that year until it ends (8.27.2, 2026-11-16).

## 8.28 Getting started

- No guided tour: the menu says what its pages are, the front page tells a member who has not taken a side yet how to
  start, and the methodology page lists where everything is ("Kur ką rasti"); nothing is stored to show or hide any of
  it (8.28.1, 8.28.2, 8.28.4, 2026-11-19).
- The start shows its full steps on a wide screen and one line a step on a phone, the short form under 54rem (8.28.3,
  2026-11-19).
- The start does not say that a person's side is shown to nobody: "it's everywhere anyway" (8.28.3, 2026-11-19).
- A new member's front page is titled "Sveiki prisijungę"; the served title is "Sveiki", completed in the browser
  (8.28.3, 2026-11-19).
- The start has a fourth step, that MPs learn of it through the summaries: "that is the whole purpose for doing
  anything"; the short step 4 leaves the trial's sentence to the note at the page's foot (8.28.3, 2026-11-19).
- A member who withdraws the positions consent sees the start again; while the start shows and the member follows
  nothing on the agenda, "Jūsų projektai" is left out (8.28.3, 2026-11-19).
- A registration just completed still lands on "Profilis", "to fill it out" (8.28.5, 2026-11-19).
- "Profilis" is one form with one "Išsaugoti", one request to one endpoint: "if anything is refused, the form shows
  validation and stays open, unsaved". On the visit that follows the registration the button reads "Išsaugoti ir tęsti"
  and opens the front page, which says once that the profile was saved (8.28.5, 2026-11-19).

## 9. Hosting

- Email goes by plain SMTP, for now `smtp.hostinger.com`, configurable; sign-in codes (with every account-security
  email), members' and the team's notices, and MP mail are three streams, each on its own address and mailbox, with a
  daily ceiling a runtime setting, so a flood of notices can never stop sign-in codes; the provider's own limit never
  puts a recipient on the suppression list (8.19.3, 8.19.6, 9.6, 2026-11-19).
