# 12. Still open

What is designed and not built, where the code and a rule disagree, the questions that wait for the owner, a native
speaker or the lawyer, the decisions still to take and the risks. Decisions already made are in
[12-decisions.md](12-decisions.md). Do not decide an open item in code.

## Designed, not built

- 6.2: account confirmation by the EU Digital Identity Wallet once available in Lithuania: counts of confirmed persons
  beside the totals (8.4, 8.12, 8.26), required for a delegate and an organisation's representative (transition period
  for existing ones, its length for the owner to set), the mark "Patvirtintas asmuo" on a public profile, proof of age,
  looser limits and no 30-day wait; stores only the fact, its date, age and a pairwise pseudonym.
- 5.4: the `notify.email_message` table.
- 6.4: rate limiting in Caddy (only the application's own limits exist).
- 6.5: disk encryption, TLS to the database, encrypted backups (none of them in the repository).
- 6.7: secret scanning (a repository setting for the owner), image scanning, an SBOM, signed images pulled by digest, a
  penetration test. Lock files, Dependabot, CodeQL and `security.txt` are built.
- 7.3: "appeals in progress" on an MP's profile (the profile shows only the MP's responses); the line was taken out of
  7.3, so whether it stays designed is for the owner.
- 8.1, 8.6, 8.20.1: telling participants and followers that a bill changed between stages (for example in a committee).
- 8.1: automatic closure for withdrawals, group motions and rejection wording beyond the confirmed main committee motion.
  `p2b.tais_docs` has project events. Confirm which events establish terminal bill status and how to link versions.
- 8.4, 8.19.6: a sending subdomain with SPF, DKIM and DMARC; complaint and bounce reports from an email provider.
- 9.1: a second web instance, the backup container (pgBackRest), the telemetry collector, host hardening, CDN and DDoS
  protection, a rehearsed recovery. 9.2: separate staging and production. 9.3: an image per commit in a registry, an
  approved deploy, expand-then-contract migrations. 9.4: an external uptime check, a telemetry backend, dashboards,
  alerts, runbooks, restore drills.
- 10: a load test.
- 8.11.6: summaries of official positions; a weekly email about followed MPs (perhaps covered by the weekly rhythm,
  8.24).
- 8.13, 8.11.6: a reader of bills' official texts (`IBillTextSource` has only the "unavailable" source), so the AI
  summary job runs and makes nothing; the AI provider is off until configured.

## Where the code does not do what a rule says

For the owner to decide in each case: fix the code or change the rule.

- 8.1: participants were to get one closing email at the final vote; there is no such email, and they hear through
  the follow email about the vote (a first side follows the bill, 8.11.2).
- 6.4: Caddy was to rate-limit per IP address; the Caddyfile has no rate limiting, only the ASP.NET policies.
- 8.4: in an MP's summary, bills led by the MP's committee come first; the group is always empty, because lead
  committees are not imported.
- 8.4 (deliverability): only permanent SMTP 5xx refusals about the recipient's mailbox go to the suppression list; complaints and later bounces are not handled (plain SMTP reports none, 8.19.6).

## Awaiting the owner

**1 Scope.** The concept website on the `docs` branch still describes an earlier scope (voting first, free topics);
it is to be updated after discussion in chat.

**5.6 Settings.** The 50 of `Digest:ThresholdMinimum` is a placeholder (the settings screen says so).

**6.2 Sign-in.** Built as proposed: a writing restriction also blocks changing the display name (or it could leave the
name alone); the change is audited without the names and no earlier names are kept; registrations invited by an MP's
office are left out of the invitation codes' counts.

**6.7 Supply chain.** The address for security reports (`LT2_SECURITY_CONTACT`, a `mailto:` or https address): until it is
set the site serves no `/.well-known/security.txt`. Secret scanning (with push protection) is a repository setting for
the owner to switch on; Dependabot and CodeQL start once the workflow files are pushed to GitHub.

**6.8 Rules held throughout.** The agenda watcher keeps its items when a parse comes back empty, until the owner
decides otherwise. One-time codes are limited per address, not per code.

**7.1, 7.5 The Seimas import against the live service.** Run the complete live contract suite (`LT2_SEIMAS_LIVE=1`)
to check the remaining assumptions and record its XML. The checks of 7.5 that need the live stack are 20 profiles
against lrs.lt, 30 votes whose ballots sum to their totals, and Lighthouse 90 or more.

**8.18 The official outcome in vote lists.** The full proceedings supply the motion and its official outcome together
in `aprasas`. Choose the labels and where they are shown before replacing a vote row's ballot-majority chip.

**8.2 Editing until someone reacts.** Built as proposed:
- Every edit of an argument drops the moderator's "seen", flagged or not, and the edited argument is listed in the
  queue as not yet seen. If no: only a flagged edit drops it. (The reason first given, that only seen arguments go to
  MPs, does not hold: a summary holds published arguments, seen or not, 8.3.)
- An unflagged edit of an argument still waiting for a moderator keeps it waiting.
- No public mark such as "Redaguota" beside an edited argument or comment. If no: "Redaguota {data}" beside it.
- Only a comment's own writer edits a comment in an organisation's or an MP's name (deleting it is open to every
  representative, or to the MP's account and office); a comment kind switched off takes no edits.
- The writing box's hint "Sąrašą rašykite eilutėmis, pradėtomis „-“ arba „1.“." (also for a native speaker).

**8.3 Hiding a record.** The sides and arguments of a hidden record are kept, so a mistaken hide can be undone. Or:
hiding deletes them.

**8.3 The reporter's email.** Built as proposed:
- A text withdrawn or deleted by its author while a report on it is open sends the reporter "Turinį atsiėmė pats
  autorius"; a comment removed with the text it stood under sends "Jis nebeskelbiamas: pašalintas kartu su tekstu, po
  kuriuo buvo parašytas." If no: nothing in those cases.
- Publishing a waiting argument, confirming a description, restoring and "Palikti" all read "paliktas"; withdrawing an
  organisation's confirmation reads "Atšaukėme jos patvirtinimą". The link is given only while the text stands.
- Reports closed because the reporter's or the author's account is deleted, or an organisation closes, send nothing.

**8.4 The party group minimum and its consent** (also for the lawyer). The party support consent (`party-support`,
version `2026-11-13`) is given against the form's text, which states the value of `Digest:PartyGroupMinimum` as it
holds then; a later lower value changes what that consent described.

**8.4 The readable title and the description in summaries to MPs.** Summaries and their public copy carry the
official title and no description (8.13.1). Whether they may carry the readable title or the AI description is to be
discussed with MPs first.

**8.4 The entered agenda.** A sitting the Seimas cancels keeps its entered rows: an empty list is refused, and a
moderator's correction moves a bill but cannot take it off the agenda. If needed: "Posėdis atšauktas" on the agenda's
entry page, clearing the day with a statement. The open data lists the upcoming sittings and their times
(`p2b.ad_seimo_posedziai`, rows without a start time; `p2b.ad_sp_darbotvarke` gives the planned time, with no items
until the agenda is approved): the entry could refuse a day without a planned sitting. Not built. How often
`p2b.tais_docs` is made anew during the day is not known (seen at 15:00 on 2026-11-25); the hourly read assumes it may be.

**8.11.1 The agenda.** The watcher's rows (when it runs) can name a bill the site does not know yet; they come last in
their day, folded into "Dar N projektų, kurių atviruosiuose Seimo duomenyse dar nėra". The lrs.lt agenda page names
each such bill's title: the watcher could create the bill, as an entered agenda does (8.4). Not built. If no: as now.

**8.11.2 Follower numbers.** Built as proposed: the number only on the thing's own page (a bill's, an MP's, an
organisation's, a member's profile), never in list rows; no MP, member or organisation sorted, ranked or added up by
it and no "most followed" list of them, until delegation is designed; lists of bills may order by it (owner,
2026-11-25, 8.11.2); nothing shown at zero; a hint "Kas yra sekėjai?" beside the number saying these are self-selected
Lietuva 2.0 users who asked for emails, not support and not a representative poll, with the methodology's part on
following saying it at length; a bill's activity (8.12.5) counts its followers. Alternatives: the number in list rows; "Sekėjai: 0"; the methodology only,
without the hint.

**8.11.5, 8.22.5 The share link and link previews.** Built as proposed; "no" names what changes:
1. A link's code is taken on any page by the site's own script (`js/lt2-invitation.js`, before Blazor starts) into the
   session key `lt2.invitation-code` and removed from the address bar; the sign-in page then opens the registration with it. If
   no: only the bill's page takes it (an island, so only once WebAssembly runs).
2. The kept code opens the sign-in page on the registration form and is forgotten once the person signs in to a
   finished account. If no: the sign-in form first, the code filled in only when "Registruotis" is pressed.
3. A member whose code is off or used up shares the bill's plain address, with "Jūsų kvietimo kodas šiuo metu neveikia,
   todėl nuoroda atveria projektą be jo." If no: no link. "Jūsų projektai" shows the share link only with a usable code.
4. A member's preview is the name and a generic line, never "Apie mane"; an organisation's is its own description while
   active and none while blocked. If no: "Apie mane" as the text.
5. One image for every page, the logo and the name; the front page has its own with the slogan (owner, 2026-11-26). If no: an image made per bill (needs a renderer on the server).
6. Pages of one's own account, staff, sign-in and one-time links are `noindex, nofollow` by a path list
   (`SecurityHeaders.IsPrivatePage`); the image is served `Cross-Origin-Resource-Policy: cross-origin`.
7. Without `Site:Address` (a development machine) `og:url` and `og:image` are left out rather than taken from the
   request's host.

**8.11.8 What is new since the last visit.** Built as proposed; "no" names what changes:
1. A visit is the page's island running in the browser; a page read without script keeps nothing. If no: a server-side
   record on GET, which is ruled out (12-decisions, 8.11.8).
2. A bill's count goes away only when its page is opened, not when the list is seen.
3. The visit's write shares the limit of a page's own answers (`own-read`) rather than `Participation`, so reading pages
   never uses up the allowance for taking sides.

**8.14 Organisations.** Talks with a few organisations on both sides of one real bill. 8.14.2: must representatives use
a passkey once organisations are named to MPs? 8.14.14: the details there are built as proposed.

**8.16.2 Version histories.** Built as proposed; "no" names what changes:
1. The text in force opens its history with its own changes ("dabartinė versija"; a response "dabartinė versija,
   parašyta {data}"; the letter "Dabartinė versija, išsiųsta {data}"), so the last edit shows. If no: only the earlier
   versions.
2. Texts are compared within their layout, line by line first (one line for one line, or lines sharing two fifths of
   their words, word by word; other lines removed or inserted whole). If no: one comparison over the whole text.
3. A space between two changes joins them into one removal and one insertion.
4. The description history puts "aprašymo nebuvo" or "aprašymas pašalintas" beside the date; a removal shows the
   removed text struck through, and whole under "Rodyti tekstą prieš pakeitimą".
5. A version whose text is unchanged (only the side, goal or subject changed) is shown whole, with no marks. If no: a
   line such as "Tekstas nepakeistas".
6. The cap: about two million comparison steps per pair of versions (`TextDiff.MaxCells`); past it the changed part is
   shown replaced whole.

**8.16.3 Bubbles.** Built so, awaiting the owner's look: the marks ("Seimo narys arba jo biuras", "Patvirtinta",
"Organizacija"), an MP's faction and the dates go under the bubble rather than beside the name, so nothing wraps onto
the line above the bubble on a phone.

**8.19.2, 8.19.3 Reviewing and sending summaries.** Built as proposed:
- A summary with no bill left at its time is `dropped`: nothing goes, nothing is public, it counts as made for its week.
- An argument withdrawn or merged during review is replaced like a hidden one.
- A declined summary's public copy shows the bills and arguments as they were when it was declined, with the statement.
- A summary found after its time (the worker was down) is prepared and goes at once, on its sending day only.
- The review box is not shown on the summaries screen itself; its counts take their Lithuanian plural forms.
- `Digest:ReviewHours` takes 1 to 72 hours; the statements line of a future organisations part (8.14.9) follows the
  rule of arguments (public when prepared).
- An office member sets their own frequency in any session; one one-click stop route is shared.

**8.22 Terms and storage.** The facts marked "[bus įrašyta]" in the terms and the privacy notice: the controller and
its contacts, the provider's details, the email, server and backup providers, the inspectorate's contacts, where other
requests go. The terms' `Terms.Summaries.1` ("argumentai, kuriuos daugiausia žmonių pažymėjo „Man svarbu“") is
slightly off, since only counted marks choose the arguments; changing it is a new terms version (8.22.2), not made (also
for the lawyer).

**8.23 Comments.**
- 8.23.3: arguments with a link wait for a moderator, because they can reach MPs, while comments with a link do not.
  Or: leave all links through as in comments.
- 8.23.6, 8.23.14 point 8: built as proposed, a link to a comment or an argument is `?komentaras={id}` or
  `?argumentas={id}` with the element's own fragment, resolved by the server (the target shown even behind "show all"
  or paging, its discussion open, the linked one marked; one 404 for hidden and unknown). Or: the fragment alone with a
  script that opens the thread (no help without script).
- 8.23.10 and the eight points of 8.23.14: built as proposed. Among them: the paging parameter `komentarai`; a
  `comments` count on each argument, statement and response in the public answers; no comment as the MP once the
  mandate has ended; the argument author's notice, told in the email rhythm as a kind of its own; the moderation
  notices on the existing kinds; moderating a comment whatever its target's state; the "Moderavimas" panel listing the
  bill's reported comments.

**8.24 Email rhythm.** Built as proposed; "no" names what changes:
1. The daily and weekly emails go at the first run of the follow job from `Follows:ResponsesAt` (08:00, so 08:07),
   the setting labelled "Rytinių laiškų laikas". If no: a setting of its own for each, or another hour.
2. The weekly email goes every Monday, whether or not the Seimas sits that week. If no: only in sitting weeks, or a
   setting for the day.
3. A gathered email holds what the job saw before that morning's hour; an event seen at 08:07 waits a day. If no: the
   cut is the run itself.
4. "Cannot wait" means a planned date on or before the day of the person's next gathered email. If no: only a date
   strictly before that day.
5. A planned date that moves again within that window is sent early again; the one-a-day limit is for "Iš karto" only.
   If no: at most one early email per bill per day.
6. One email per person per run, an early "planned" line included. If no: an early line is always an email of its own.
7. An email with one thing keeps that thing's own email; two or more make the gathered email ("Lietuva 2.0: naujienos
   apie tai, ką sekate"; daily "dienos naujienos", weekly "savaitės naujienos"). If no: every email of a run or a day is
   the gathered one.
8. After a change of the choice, what was waiting comes in the email it was waiting for; going back within 15 minutes
   undoes the change. What can still be lost: after two changes before the first choice's email, what waited under the
   first; after an undo, what waited under the choice before the undone one's predecessor; an item a gathered pass took
   for others during a mis-click then undone. If no: what waits moves into the new choice's first email, with a
   per-person mark of the carry-over.
9. MPs' answers are recorded per answer, with the three-day rule of votes, and told within a run under "Iš karto"; a
   missed morning is sent late at the next run. If no: the record per bill and day stays.
10. Changing the choice is audited without the choice itself. If no: not audited, like the switches.
11. At most one daily email a Vilnius day and one weekly email a week (`notify.rhythm_pass`); moving
    `Follows:ResponsesAt` later the same day sends nothing more that day. If no: the email follows the hour at each run.
12. A switch is read when each email is made: a "planned" date not sent early because its switch was off is not told
    in the gathered email either. If no: the early pass marks the date per person.
13. The follow job's records are kept 30 days, a planned date only until it is past. If no: another number of days.
14. 8.24.1: one press of a stop link turns off every notice switch of "Sekimas ir laiškai"; the follows stay.

**8.25 Public profiles.**
- 8.25.1: "Apie mane" only with a public profile, one switch and one explanation (built so). Or: "Apie mane" shown in a
  card also for members without a page.
- 8.25.2: the "Sutinku" a person gave is not listed on the profile (built so). Or: listed.
- 8.25.2: the page is `no-cache` for everyone, so switching off is at once; the 410 and 404 answers carry
  `X-Robots-Tag: noindex` (built so). Or: ordinary caching.
- 8.25.3: a personal code is refused in "Apie mane" (`about.personal-code`, built so). Or: allowed like the person's own
  contacts.
- 8.25.5: following a member sends a line when the member publishes an argument or a comment in their own voice, never
  the text, by the follower's rhythm, with its own switch (built so). Or: arguments only. No follower number in the card.
- 8.25.6: "Viešas profilis" needs a display name, which cannot be emptied while the profile is public
  (`profile.name-required`, built so). Or: a page without a name, titled "Narys".
- 8.25.9: changing the supported party withdraws the consent to show it, as removing it does (built so).

**8.26 Analytics.**
- The list of closed bills on `/statistika`, built so and awaiting the owner's look: four figures follow only the
  period and the accounts, not the list's filters; the filters are a second form; the label column at 38 % / 12rem (on
  a phone about 14 letters of a title show); the axis names only 0, 50 and 100 % on a phone; the table opens at once
  when none of the bills shown can stand in the plot; on a narrow screen the table's rows are blocks.
- 8.26.6: should a periodic job make the public analytics copy?

**8.28 Getting started.** Built as designed; "no" names what changes:
1. Step 2 of the start says that pressing the pressed side again takes it back ("Paspaudę dar kartą, atsiimsite."). It
   is read before the first side, not after: the start goes when the side is taken. If no: a hint beside "Prašau
   balsuoti", for everyone.
2. The profile's three withdrawals stay requests of their own beside the one `PUT /me/profile` (`DELETE
   /me/profile/public`, `/me/party-support`, `/me/party-support/public`), because an account that has not accepted the
   terms may withdraw but not save (8.22.2). If no: only the one endpoint, exempt from the terms when a request only
   withdraws.
3. A part of the profile's form that did not change is not checked, refused or audited: a member under a writing
   restriction can still save a party or a box. If no: the whole form is refused under a restriction.
4. The visit that follows the registration, and the notice "Profilis išsaugotas.", are each carried by a mark after `#`
   in the address, not stored. If no: the notice is left out.

## Awaiting a native speaker

- 8.13.1: "Trumpas pavadinimas", its hint, "Oficialus pavadinimas: …", and the readable-title rules in both prompts.
- 7.2: a moderator's correction of a vote's classification: the public hint "Klasifikaciją pataisė moderatoriai" and its
  answer, the methodology's paragraph (`ParliamentText` `Vote.Corrected`, `Vote.Corrected.Answer`,
  `Method.Classification.2`); the moderators' part on the vote's page (`AppealUi` `VoteMod.*`: "Klasifikacija: {0}.",
  "Nustatė taisyklės pagal balsavimo formuluotę.", "Pataisė moderatorius {0}: {1}", "Pataisyti klasifikaciją",
  "Grąžinti taisyklių klasifikaciją", the form's sentences and labels, the confirmations); the refusals
  (`ProblemText` `vote.not-found`, `vote-classification.*`; `ValidationMessages` `VoteClassification.*`).
- 8.14.4: the organisations' line under a bill in a summary, "Organizacijos, prašančios balsuoti už: A, B.", "Organizacijos,
  prašančios balsuoti prieš: …", "Pastabų atsiuntė: …" (`EmailTemplates` and `AppealUi` `Digest.Organisations.*`); the
  methodology's row "Organizacijos" and its value and the rewritten sentences on statements and impersonation
  (`AppealText` `Method.Digests.Organisations*`, `Method.Organisations.2`, `.3`).
- 8.14.9: keeping an organisation out of summaries: "Neįtraukti į suvestines", "Vėl įtraukti į suvestines", their sentences
  and confirmations, the tag "Neįtraukiama į suvestines", the hidden list's "Organizacijos, neįtraukiamos į suvestines"
  with its lead, the summaries screen's hint and confirmation (`AppealUi` `OrgMod.Action.keep-out|keep-in`,
  `OrgMod.Sentence.*`, `OrgMod.Done.*`, `OrgMod.KeptOut`, `OrgMod.Hidden.KeptOut*`, `DigMod.KeepOutHint`, `DigMod.KeptOut`),
  the representatives' note (`Org.Manage.KeptOut`), the moderation rules' sentence (`AppealText` `Rules.Organisations.2`)
  and the two emails ("Lietuva 2.0: organizacija „{0}“ neįtraukiama į
  suvestines Seimo nariams" and "…vėl įtraukiama…", `EmailTemplates` `Organisation.kept-out-of-summaries.*`,
  `Organisation.back-in-summaries.*`).
- 8.19.4: the welcome to an extra recipient, "Seimo narys patvirtino, kad šiuo adresu siųstume jo suvestinių kopijas. Seimo
  narys: {0}." (`EmailTemplates` `MpMail.extra-welcome.Body`), its stop link in the footer.
- 8.19.3, 8.4: the summary's HTML-only link and figure labels (`EmailTemplates` `Digest.ForLabel` "Prašo balsuoti už", `Digest.AgainstLabel` "Prašo balsuoti prieš", `Digest.Older` "iš jų {0} – paskyros senesnės nei 30 d.", `Digest.AnswerButton` "Atsakyti visiems", `Digest.WholeReasonLabel` "Skaityti visą", `Digest.PublicCopyLabel` "Ši suvestinė viešai"); the text part no longer has "Visi skaičiai ir argumentai" beside another link to the same bill.
- 8.19.3, 8.19.5: the takeover's lead line and button in every MP email, "Prisijungę prie Lietuva 2.0 šiuo adresu, galite perimti savo Seimo nario profilį ir atsakyti visiems iš karto." and "Perimti profilį ir atsakyti visiems iš karto" (`EmailTemplates` `Digest.TakeoverLead`, `Digest.TakeoverButton`).
- 8.4, 8.19.3: the heading of a side with more than two published arguments, "Dažniausi argumentai už (iš {0}):" / "prieš (iš {0}):"
  (`EmailTemplates` `Digest.ReasonsOf.*`), and the same count beside a side on the public copy ("(iš {0})", `AppealText` `Digests.OfSide`) and on the moderators' screen (`AppealUi` `DigMod.OfSide`).
- 8.19.3: the settings screen's group "Laiškų siuntimo ribos" and the three daily ceilings of the email streams (`AppealUi` `Admin.Group.email`, `Admin.Setting.Email:Ceiling*PerDay`, `Admin.Help.Email:Ceiling*PerDay`, `Admin.Unit.Email:Ceiling*PerDay`).
- 6.5, 8.4, 8.25.1: the refusals when a consent text in force is not the one the form showed, "Galioja kitas sutikimo
  tekstas. …" (`ProblemText` `consent.outdated`, `party.consent-outdated`, `profile.consent-outdated`).
- 6.2: the texts of changing the email address ("El. pašto adresas", "Keisti adresą", "Kodą išsiuntėme adresu …",
  "Ne jūs? Grąžinti ankstesnį adresą", the rest of the part and of the page that puts the old address back: `UiText`
  `EmailPart.*`, `EmailRestore.*`), its emails (`EmailTemplates` `EmailChange.*`: the code to the new address, to an
  address that has an account, to the old address, when the old address was put back) and the code to the address in
  use (`EmailProofCode.*`), and the refusals `email.same`, `email.proof-required`, `email.restore-invalid`.
- 6.2: the line under the sign-in and registration form, "Paskyros patvirtinamos el. paštu. Kai Lietuvoje bus ES skaitmeninės
  tapatybės piniginė, ją taip pat bus galima naudoti." (`UiText` `SignIn.Verification`; no date, on purpose).
- 6.2, 6.4: the bot check: its status line "Tikrinama, ar jūs ne robotas…" (`UiText` `BotCheck.Working`), its refusal
  "Nepavyko patikrinti, ar jūs ne robotas. Bandykite dar kartą." (`ProblemText` `bot-check.failed`), "Patikros atsakymas
  per ilgas." (`ValidationMessages` `BotCheck.TooLong`) and the three settings' names, help and units (`Admin.Setting`,
  `Admin.Help`, `Admin.Unit` `BotCheck:*`).
- 6.2: the urgent summary's passkey messages "Skubi suvestinė nepatvirtinta: ši naršyklė negali patvirtinti prieigos
  raktu. …" and "Skubi suvestinė nepatvirtinta, nes prieigos raktu nepatvirtinote. …" (`AppealUi` `Mod.Passkey.*`).
- 6.5: the positions consent text (`UiText` `PositionConsent.Text`); for a consent to an older text, the account page's
  line "Sutikote su ankstesne šio sutikimo teksto versija. Perskaitykite galiojančią ir patvirtinkite.", "Skaityti
  galiojantį tekstą", "Sutikimas su galiojančiu tekstu išsaugotas." (`UiText` `PositionConsent.Outdated`, `.Read`,
  `.Accepted`) and the form's "Galiojantis sutikimo tekstas" and "Sutinku" (`AppealUi` `Consent.Title.account`,
  `Consent.Submit.account`).
- 8.2: the notes "Bus skaičiuojama, kai prašysite balsuoti už." and "Neskaičiuojama: jūs prašote balsuoti prieš."; the
  writing box's hint "Sąrašą rašykite eilutėmis, pradėtomis „-“ arba „1.“." (also for the owner).
- 8.1, 8.3, 8.6, 8.12.1: closing a rejected or withdrawn bill: the actions "Uždaryti: projektas atmestas ar atsiimtas" and
  "Atšaukti uždarymą", the form's "Kas nutiko", "Seimas projektą atmetė", "Iniciatoriai projektą atsiėmė", "Sprendimo
  data", "Šaltinis lrs.lt" and its hint, the sentences and confirmations (`AppealUi` `Mod.Action.close|reopen`,
  `Mod.Sentence.appeal.close|reopen`, `Mod.Close.*`, `Mod.Done.close|reopen`); the page's "Seimas projektą atmetė {0}.",
  "Iniciatoriai projektą atsiėmė {0}.", "Projektas atsiimtas", "Šaltinis", "Skaičiai užfiksuoti {0} ir nebesikeičia."
  (`AppealText` `Closed.*`); the follow email's subjects, lines and the gathered part "Seimas nebesvarsto:"
  (`EmailTemplates` `Follow.Closed.*`, `Gathered.Closed.*`); the refusals (`ProblemText` `closing.*`,
  `closing-url.invalid`; `ValidationMessages` `Closing.*`); the methodology's "kol dalyvauti dėl projekto galima"
  (`AppealText` `Method.Privacy.1`, also for the lawyer).
- 8.3: the lock from an item and the list of locked accounts: "Užrakinti autoriaus paskyrą" and "Paskyra užrakinta; žmogui išsiųstas laiškas."
  (`AppealUi` `Lock.*`, `Mod.Action.lock`, `Comments.Mod.Action.lock`, `OrgMod.Action.lock`, `ProfileMod.Action.lock`), "Užrakintos paskyros",
  its lead, "Užrakinta {0}", "Atrakinti", the unlock sentence and "Užrakinimas pagal el. pašto adresą" with "Jei paskyros savininkas parašė
  komandai iš savo adreso." (`AppealUi` `Acc.Locked.*`, `Acc.Lock.Title`, `Acc.Lock.Lead`), and the validation line "Nurodykite, kieno paskyrą
  užrakinti." (`ValidationMessages` `Lock.Target`).
- 8.3: the full lock: the "Paskyros" screen (`AppealUi` `Acc.*`, `PageText` `Nav.Accounts`), the two emails ("Lietuva 2.0: paskyra
  užrakinta" and "…atrakinta" with their bodies and the line on writing to the team and on erasure, `EmailTemplates`
  `Notice.account-locked.*`, `Notice.account-unlocked.*`) and the refusals (`ProblemText` `account.lock-self`,
  `account.lock-staff`, `account.locked`, `account.not-locked`).
- 8.3: the moderators' explanation labelled by who reads it: "Paaiškinimas autoriui (privalomas)", "Viešas paaiškinimas
  (privalomas)" with "Rodomas projekto puslapyje.", "Rodomas atsakymo vietoje." and "Rodomas organizacijos puslapyje. Jį gaus
  ir organizacijos atstovai el. paštu.", "Paaiškinimas moderatoriams (privalomas)" with "Mato tik moderatoriai ir
  administratoriai." (`AppealUi` `Mod.Statement.*`); the refusal "Parašykite paaiškinimą." where no author
  receives it (`ValidationMessages` `Statement.RequiredPlain`).
- 8.3, 8.13: the description's public statement: "Rodomas keitimų istorijoje. Jį el. paštu gaus ir apie aprašymą
  pranešę nariai." and "Viešas paaiškinimas (nebūtinas)" (`AppealUi` `Mod.Statement.Public.Hint.description`,
  `Mod.Statement.Public.Optional`); the sentences "…o ankstesnis liks keitimų istorijoje." and "…Patvirtinimas bus
  matyti keitimų istorijoje." (`Mod.Sentence.appeal.describe|confirm`); the history's "Paaiškinimas", "nenurodytas" and
  "aprašymas patvirtintas be pakeitimų" (`AppealText` `History.*`); the draft's "Juodraštis įrašytas į pavadinimą,
  aprašymą ir viešą paaiškinimą. …", "Palikite tuščią, ir DI pats perskaitys projekto tekstą ir aiškinamąjį raštą
  e. seime." (`AppealUi` `ModOpen.DraftReady`, `ModOpen.SourceHintReader`) and "Projekto dokumentų e. seime perskaityti
  nepavyko. Įklijuokite oficialų tekstą." (`ProblemText` `draft.source-unavailable`); the agents' box "Tik DI agentams:
  pažymėkite, jei šį aprašymą parengėte ir siunčiate jūs, DI agentas, o ne žmogus. …" (`AppealUi` `Mod.WrittenByAi`).
- 8.5.2: the MPs' section of "Paskyros": "Seimo narių profiliai", its lead, "Perimtas {0}", "Baigti ryšį", the sentence and
  "Ryšys baigtas. Seimo narys: {0}." (`AppealUi` `Acc.Mps.*`).
- 8.7, 8.12.1: "Paspaudę dar kartą, atsiimsite." (`AppealUi` `Side.PressAgain`), the pressed button's title.
- 8.10: the feedback texts.
- 8.12.2: the reached threshold, "Riba pasiekta – …" (`AppealUi` `Threshold.Reached.*`, `Threshold.ReachedClosed.*`)
  and "Suvestinė Seimo nariams planuojama …" (`Threshold.Next`), also for the owner's look; the threshold hint's answer
  "Riba – dalyvių skaičius …".
- 8.12.2: "Projektas jau buvo suvestinėje Seimo nariams – balsuoti prašo …" (`AppealUi` `Threshold.Summarised.*`,
  `Threshold.SummarisedClosed.*`) and the rows' "suvestinė planuojama …" and "suvestinė išsiųsta …" (`AppealUi`
  `Agenda.Summary.*`, `AppealText` `List.Summary.*`, `List.Reached`).
- 8.15: texts applied in the `.resx` files with no native reading recorded: the registration form's lead and its "Kaip
  tai veikia" items; the shortened lines of the bill's page (`Share.Lead`, `Side.Private`, `Reasons.SimilarText`,
  `Org.Section`, `Org.Form`, `Org.Mine`); the organisation's profile and code texts; the account's texts
  (`Sessions.Text`, `Data.Text`, `Data.DeleteWarning`, `Follows.Lead`); the sign-in hints (`SignIn.PasskeyHint`,
  `Invitation.Hint`, `Register.Fine`); `List.Lead`, `List.Counts`, `Agenda.Lead`, `Agenda.Source`; the feedback texts
  (`NotChat`, `MayReplyHint`, `IssuesMore`); the moderators' leads (`Mod.Lead`, `Mod.UnderThreshold`, `Mod.DateFlags`,
  `Mod.Surge`); the methodology's (`Method.Dates`, `Method.Privacy`, `Method.Organisations.Private`,
  `Method.Digests.PublicCopyValue`, `Method.Limits`, `Method.Lead`, `Method.Limits.Note`); `Digests.Items`.
- 8.16.2: the version histories' texts.
- 8.18: the vote's majority words "Dauguma už", "Dauguma prieš", "Nėra daugumos" and the hint "Kas yra balsavusiųjų
  dauguma?" (`ParliamentText`, `AnalyticsText`).
- 8.21: the footer heading "Naršyti" (`PageText` `Footer.Browse`). If no: "Puslapiai", "Skyriai" or "Lietuva 2.0".
- 8.22.5, 8.11.5: the link-preview and share-link texts.
- 8.23.1: the terms "Aptarimas" and "komentaras".
- 8.4, 8.24, 8.19.4 (looking at every email in Mailpit, 2026-11-20): wording moved, none added. The gathered email's
  parts (`Gathered.*.Line`) no longer end a line with a bare address: the address goes on its own indented line under
  the label that the single email uses ("Projektas: …", "Balsavimo rezultatai: …", "Šaltinis: …"), so a followed
  member's argument or comment is also linked as "Projektas" (`Gathered.Member.Line.*`). The feedback notice reads
  "Naujų atsiliepimų: {0}. Juos rasite (reikia darbuotojo prieigos rakto): {1}" (`Feedback.Notice.Body`; the address was
  inside the sentence). The MP's change, extra-recipient and new-link emails put the sentence that follows the link on a
  new line (`MpMail.NotYou`, `MpMail.extra-confirm.Body`). Existing wording the look turned up, not changed, against the
  rule that nothing is worded as new: "nuo šiol" in `MpMail.extra-confirmed.Body` and
  `Organisation.confirmation-withdrawn.Body`, "dabar" in `Notice.mp-linked.Body`.
- 8.24.1: the stop page's texts ("Nebegauti laiškų", its sentence, button and confirmation).
- 8.25.1: the public-profile consent text (`UiText` `PublicProfile.Consent`); for a profile on under an earlier text, the
  account page's line "Sutikote su ankstesne šio teksto versija. Perskaitykite galiojančią ir patvirtinkite.", "Skaityti
  galiojantį tekstą", "Galiojantis sutikimo tekstas", "Perskaičiau šį tekstą ir sutinku", "Sutinku" and "Sutikimas su
  galiojančiu tekstu išsaugotas." (`UiText` `PublicProfile.Consent{Outdated,Read,Title,Check,Accept,Accepted}`), and the refusal
  `profile.not-public`. The consent text's last sentence ("Išsaugodami įjungtą profilį sutinkate su tuo.") reads oddly beside
  "Sutinku"; for the reader to word.
- 8.25.9: the party box's sentence (`UiText` `Party.PublicText`), the page's line "Palaikoma partija: …", `Party.Text`.
- 8.28: the lines' and the steps' texts, "Išsaugoti ir tęsti", "Profilis išsaugotas." (8.28.2, 8.28.3, 8.28.5); "Kur ką
  rasti"'s group names "Meniu" and "Puslapio apačioje" and the line under "Projektai" ("Keturi puslapiai apie tai, ką
  svarsto Seimas:", from the mock-up); the form's "Nieko neišsaugota: pataisykite pažymėtus laukus." and `party.none`,
  "Pirmiausia pasirinkite, kurią partiją palaikote." (`UiText`, `ProblemText`).
- Appendix A: the summary email's texts, above all "Iš nurodžiusių, kad palaiko {party}:" with the party's name not
  declined.

## Awaiting the lawyer

The terms, the privacy notice and the consent texts are kept correct as drafts as the site changes; the lawyer reads
them all once, before going live.

- **6.5 Consent texts.** The positions consent texts (`UiText` `PositionConsent.Text`, `LegalText` `Privacy.Positions.3`)
  read together with the rule that writing an argument needs no consent (8.2): an argument is the author's own
  deliberate publication, public with name and chosen side, and withdrawable. The consent covers "Man svarbu" marks as
  well as sides: is a private mark on an argument, given on either side, special-category data as a side is (the design
  treats it so)? Is a permit from the State Data Protection Inspectorate needed?
- **8.4 Summaries.** Before the first real send: are the summaries citizens' appeals, not marketing, and what does the
  notice say? The party support consent states the party group minimum as it holds when given; a later lower value
  changes what it described (under "Awaiting the owner", 8.4).
- **8.14 Organisations.** Is passing a statement to MPs lobbying under the Law on Lobbying Activities, and who declares
  what? The DPIA covers representatives, confirmation by email and the invitation counter.
- **8.22 Terms and privacy notice**, one reading of all of them. What a member publishes or allows (comments, "Sutinku"
  under the name, the public profile with "Apie mane", being followed): the terms `Terms.Purpose.2`, `Terms.Publish.2`,
  `Terms.Publish.5`, `Terms.Publish.6`, `Terms.Moderation.1`, `Terms.Moderation.2`, `Terms.Leave.1`; the privacy notice
  `Privacy.Data.Fact2.Value`, `Privacy.Data.Fact5.Value`, `Privacy.Data.Fact7.Value`, `Privacy.Data.Fact12.Value`,
  `Privacy.Rights.Fact3.Value`, `Privacy.Rights.Fact5.Value`. A change to a `Terms.*` text is a new terms version
  (8.22.2); `Terms.Summaries.1` is slightly off (under "Awaiting the owner", 8.22).
- **8.22 Legal bases.** The legal basis of the technical logs. The legal basis of the usage statistics (8.27: IP
  address and browser hashed with a salt kept until the day, month or year ends; counts only; `Privacy.Basis.Fact2.Value`
  is "[bus įrašyta]"), and whether e-privacy Art. 5(3) applies (EDPB Guidelines 2/2023 bring tracking by IP address
  under 5(3) when the address goes to a third party to track across sites; ours is first-party, on one site, and keeps
  nothing beyond the period). If 5(3) applies, the counting stops or asks consent; never a banner by default.
- **8.11.2 Follower numbers.** How many follow a bill, an MP, an organisation or a member is public, who follows is not:
  `Privacy.Data.Fact6.Value`, checked with `Terms.Purpose.2` (following as what an account is for), `Terms.Leave.1`
  (deleting the account lowers the shared numbers, follower numbers among them), `Privacy.Rights.Fact5.Value`
  (unfollowing), and the methodology's `AppealText` `Method.Privacy.1` and `Method.Privacy.Follow` (`/metodika#following`).
- **8.23 Comments.** Comments carry no side and need no consent, as arguments (8.23.1). The methodology's "Kas matoma
  viešai, o kas ne" (`AppealText` `Method.Privacy` and its part) and the positions consent texts say that "Man svarbu"
  stays private (it tells the side) while "Sutinku" on a comment is public under the name and tied to no side. 8.23.10:
  is GDPR Art. 9(2)(e) the ground for a public "Sutinku" (opinion data the person makes public themself), and does the
  inline sentence suffice?
- **8.25 Public profiles.** A profile gathers one person's published opinions in one place, by their choice, and "Apie
  mane" can state a political opinion or membership. 8.25.1: the "Viešas profilis" consent text (kind `public-profile`,
  `UiText` `PublicProfile.Consent`, its own version) says the page is indexed, that others may follow and that the number
  of followers is shown; the privacy notice says a search engine's copy outlives switching the profile off. 8.25.5: the
  sentence "Atskiro sekėjo užblokuoti negalima…" is not in the form. 8.25.6: one reading of the profile, consent and
  terms texts. 8.25.9: the party shown, `UiText` `Party.PublicText` and `Party.Text`, `Privacy.Data.Fact7.Value`,
  `Privacy.Data.Fact12.Value`, `Privacy.Rights.Fact3.Value`, `Terms.Publish.6`, `Method.Privacy.Follow`.

## Decisions needed

| Decision | Options | Needed |
|---|---|---|
| Telemetry backend | A hosted free tier with EU storage, or self-hosted. | Before going live |
| CDN and DDoS protection | Bunny (EU) or Cloudflare. | Before the first public campaign |
| MP photos | Use the Seimas photos if their terms allow; the profile shows initials. | Any time |
| AI provider for pre-screening | Pre-screening of users' texts by AI needs an EU-region route first: the first-party Claude API has no EU-only processing. Pre-screening is rule-based. | Before any AI pre-screening |
| Comments under MPs' responses (8.23.5, 8.5.6) | Keep `Comments:OnResponses` on, as on the trial; or switch it off. Decided with what the trial showed and after talks with MPs' offices. | Before real MPs are on the site (with the introduction letter, 8.19.5) |
| A follow and a future delegation (1.1) | **Proposed:** a follow never becomes a delegation by itself; delegation is its own explicit act, and existing follows are not converted (people follow those they disagree with, to watch them). A constraint on delegation's design. | When delegation is designed |
| Legal reading of the terms, privacy notice, consent texts and usage statistics | One reading of all of them; the questions are under "Awaiting the lawyer". | Before going live |

## Risks

| Risk | Mitigation |
|---|---|
| MPs' mail system filters the summaries, or MPs see them as spam | Introduction letter, low and capped frequency, both sides in every email, one-click stop, no tracking, discussions with offices before the first send. |
| A campaign registers many accounts to inflate one side. With email-only registration this costs nothing, so it is the main weakness of the numbers | Registration by invitation (`Registration:RequireInvitation`), one account per normalised address, disposable domains refused, the 30-day count beside a bill's numbers on its page, in the summaries and the statistics, surge note, moderator hold; the wallet level (6.2) is the planned upgrade. The numbers stay a signal, and say so. |
| The upcoming-agenda page changes layout or is late | Watcher alerts, moderator-entered dates, "Planuojama …" wording, no extra emails when dates move. |
| A campaign's volume can exceed a shared mailbox's daily cap (plain SMTP, 9.6: about 1 000 or 3 000 emails a day per mailbox, bulk mail unwelcome) | Three streams with their own mailboxes, so notices cannot stop sign-in codes (8.19.3); a daily ceiling per stream set under the cap, with the excess waiting in the outbox; codes sent first; a provider's refusal for its limit pauses the stream and suppresses nobody (8.19.6). Before a public campaign, sign-in codes move to a transactional provider. |
| Open-data service outages | Raw responses stored, retries, pages show the last import time, imports resume. |
| Someone who knows a person's address uses up its daily codes | A known browser (8.20.2) keeps its own five codes an hour and ten a day, counted separately, and a code only it can use; a passkey is not affected. What stays possible: keeping a person from signing in by code from a browser they have never signed in with, and keeping a new address from finishing its registration (a pending account has no known browser). Nothing leaks either way. |
| A leak of positions | Minimised data, no position in logs or admin screens, encrypted backups, impact assessment, penetration test, incident runbook. |
| Moderation load for a volunteer team | One record per bill and moderation after the fact (nothing waits for approval but flagged or reported arguments, 8.12.3); plain-text members' texts whose length is one setting (`Texts:MaxLength`, 2 000 characters for arguments, statements and responses; lowering it needs no deploy, 5.6, 8.2); rule-based pre-screening; hourly rate limits per account; limits per person per bill ready to switch on (off, 8.2). Comments: never in the moderators' list unless reported; pre-screening refuses a comment with personal data instead of holding it; no replies to replies; "Sutinku" carries no text; an hourly limit of their own and a ceiling per person per text ready to switch on; a switch per kind that stops new comments at once. What stays: insults, spam and spam links in comments (links are allowed, 8.23.3) reach a moderator only by a report. |
| MPs see "Aptarimas" under their answers as a place of abuse: a pile-on under an answer, the expectation that the MP answers every comment, offices less willing to answer at all (8.23.2) | The switch for responses (`Comments:OnResponses`, decided again before real MPs are on the site); the MP's account and office comment only as the MP under its own response, never pressed to; no email to the MP about comments; nothing counts or lists comments an MP left unanswered; reports and moderation on the page; writing restrictions. |
| One person's arguments and comments gathered into a picture of their political views (8.25) | A public profile only by the member's choice, said in the form before saving, indexing included; the address retired (410) when the profile goes off; no page, id or link for anyone else; nothing on a profile about sides, "Man svarbu", what the person follows, counts of their texts or rankings. What stays: a member who chose a profile can be collected by anyone who visits it. |
| A followed member watched and pressed (8.25.5) | The profile switch is the consent to being followed and ends every follow at once; only the number of followers shows, nobody (the person included) is told who follows; harassment in comments answered by reports, hiding and the writing restriction. What stays: one particular follower cannot be blocked without showing followers. |
| The product drifts from "people tell MPs" towards a social network (8.25, 8.11.2) | A number of followers on every thing that can be followed, members included, stands only on the thing's own page and no person or organisation is ranked, sorted or listed by it (bills may be ordered by it); who follows is invisible to all but the follower; summaries carry no follower number. What stays: a visible count invites collecting followers, which is what a social network rewards. |
| MPs' follower numbers read as a popularity score or a scorecard (7.1, 8.11.2) | No ordering (no list sorted by it, no "most followed", no number in list rows), no comparison (no total per faction or party, nothing set beside another MP's number), and the note that these are self-selected users who asked for emails, not supporters and not a representative poll. What stays: anyone can open profiles one by one and build the table elsewhere; the site does not make it. |
| A withdrawal or a rejection outside the confirmed motion stays open until a moderator records it (8.1 step 6). | Verify other terminal events and their bill links before using them for automatic closure. Analytics covers bills closed by an adoption-stage vote (8.26.2). |
| A member's comparison kept in server memory (8.26.6) reveals their sides to anyone who can read the web process's memory | Kept only in memory, never in a table, a log or the browser, bounded to 10 000 members and dropped after 12 idle hours, at sign-out, account deletion and withdrawal of the positions consent; the same exposure as the sides themselves in the database. |
| Running costs outlive enthusiasm (the old platform's fate) | One VPS, no paid infrastructure beyond email and backup storage. |
