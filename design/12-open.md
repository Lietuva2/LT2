# 12. Still open: proposals, decisions needed, risks

What the owner decided is in [12-decisions.md](12-decisions.md). Do not decide an open item in code.

**Found with the stand-in's real-sized parliament (141 MPs, about 45 bills a sitting day), 2026-11-18, awaiting the owner**

Nothing is built for these; the local stack and the trial show them as they are.

1. About four in ten upcoming agenda rows are bills to be presented, which the open data does not know before their first
   sitting. Since 2026-11-18 (owner) they come last in their day, folded into one line "Dar N projektų, kurių
   atviruosiuose Seimo duomenyse dar nėra" that opens to their numbers and the agenda source (on the agenda page at the
   end of a day shown whole, or of a day whose known bills all show); the front page counts
   them in the day and lists them only in a day opened whole (design 8.11.1, 8.7). Still open: the lrs.lt page names each bill's title in its row;
   the watcher could keep it for such rows until the open data has the bill, and they would then rank as other bills.
   If no: as now.
2. [Decided by the owner on 2026-11-18, 12-decisions: the front page shows two voting days of five votes each, with
   links to the rest.]

**The outcome of a vote: [Decided by the owner on 2026-11-18, 12-decisions: keep the majority words (choice 1).]**

The background, kept for the live contract run: built in the words of 8.26.2 (8.18, "The majority"): a chip "Dauguma už" / "Dauguma prieš" / "Nėra daugumos"
(the majority of those who voted; a consensus "Pritarta bendru sutarimu") and one hint "Kas yra balsavusiųjų dauguma?"
beside each list. The open data carries no official outcome: the vote results service gives the totals, those who voted
and the official note (`ASSUMPTIONS.md` R1–R20), and the proceedings service the vote's wording (E7), which is the
question ("Pritarti projektui po pateikimo"), not its result; only a consensus carries its result ("Pritarta bendru
sutarimu", E9). No live response has been recorded yet (`Recorded/` holds only its README), so a field nobody has seen
may still exist: the live contract run once the service reopens is the place to look. The owner's choice:

1. **Keep the majority words** (as built). If yes: nothing changes.
2. **Say "Priimta" / "Nepriimta" (adoption) and "Pritarta" / "Nepritarta" (other votes) read from the numbers**, by the
   same rule, with the hint saying it is read from the numbers. What the numbers cannot tell, so such a word can be wrong:
   - **Qualified majorities.** A constitutional law needs more than half of all MPs to adopt and three fifths to amend; an
     amendment of the Constitution two thirds, in two votes; a law the President returned more than half of all MPs
     (three fifths for a constitutional law); some resolutions their own. The data does not say which vote needs which.
   - **Who takes part.** The Seimas counts the MPs registered for the vote; one who registered and did not vote counts
     against. The totals are those who voted, and the registration is not imported.
   - **The quorum.** A sitting decides only when more than half of the MPs take part; a vote with fewer is not checked.
   8.26.2 would then say the same words or keep "balsavusiųjų dauguma" on the analytics page.
3. **Use an official outcome** if the live run finds one (a result field, or a result text in the proceedings), with 1
   until then.

The words are proposals for a native read. Not shown either way: "Jūsų projektai" and the followers' "voted" email say
the numbers only; an MP's votes show the MP's choice; the bill's stages say "už : prieš".

**Found while rewording the positions consent (6.5, 12-decisions 2026-11-18), awaiting the owner**

A consent given to an older text of the positions consent (2026-11-13) is not the consent in force (`CurrentPositionConsent`).
The design says the text is versioned but not what a person on an older text has meanwhile. As built: their sides and marks
stay stored and counted and show as theirs; taking a side back and removing a mark work; a new side or mark opens the form in
place. But the account page shows "Sutikimas saugoti pozicijas ir žymas" with "Atšaukti sutikimą ir ištrinti pozicijas bei
žymas" only for the consent in force, so until the person consents again the page offers no way to withdraw the older
consent and delete what it covered (the API, `DELETE /me/consents/positions`, withdraws every version). Nothing is built for
this. Options: show the part (and its withdrawal) for any consent not withdrawn, with a line asking to read the new text;
or ask for the new text on the account page itself; or, before the launch only, leave it (nobody but the owner and the
invented accounts has a consent). The owner's own trial account is in this state after this change until it consents again.

**Proposed by the designer while building the version histories' changes (8.16.2), 2026-11-18, awaiting the owner**

Each is built as written; "no" names what changes. The Lithuanian texts are proposals for a native read.

1. The text in force opens its history with its own changes ("dabartinė versija", for a response "dabartinė versija,
   parašyta {data}", for the letter "Dabartinė versija, išsiųsta {data}"), so the last edit shows; the summary still reads
   "Ankstesnės versijos (n)". If no: only the earlier versions, and the last edit is seen by comparing by eye.
2. Texts are compared within their layout, line by line first: one line for one line, or lines sharing two fifths of
   their words, word by word; other lines removed or inserted whole where they stood. If no: one comparison over the whole
   text, which can mark half a list item against another.
3. A space between two changes joins them ("mažina tarifą" → "didina mokestį" is one removal and one insertion, not
   word against word).
4. The description history (newest first, as every history: owner, 2026-11-18) puts "aprašymo nebuvo" or "aprašymas pašalintas" beside
   the date; "Buvo" and "Tapo" are gone. A removal shows the removed text struck through, and whole under "Rodyti tekstą prieš pakeitimą".
5. A version whose text is unchanged (only the side, goal or subject changed) is shown whole, with no marks and no
   "Rodyti tekstą prieš pakeitimą". If no: a line such as "Tekstas nepakeistas".
6. The cap: about two million comparison steps per pair of versions; past it the changed part (between what is equal at
   the start and the end) is shown replaced whole.

**Proposed by the designer while building the share link on the bill's address and link previews (8.11.5, 8.22.5),
2026-11-17, awaiting the owner**

Each is built as written; "no" names what changes. The Lithuanian texts are proposals for a native read.

1. A link's code is taken on any page by a small script of the site's own (`js/lt2-invitation.js`, before Blazor starts),
   into the existing session key `lt2.kodas`, and removed from the address bar; the sign-in page then opens the
   registration with it. If no: only the bill's page takes it (an island, so only once WebAssembly runs).
2. The kept code opens the sign-in page on the registration form, as the old share link did, and is forgotten once the
   person signs in to a finished account. If no: the sign-in form first, the code filled in only when "Registruotis" is
   pressed.
3. A member whose code is off or used up shares the bill's plain address, with "Jūsų kvietimo kodas šiuo metu neveikia,
   todėl nuoroda atveria projektą be jo." If no: no link, as before. The front page's "Jūsų projektai" still shows the
   share link only with a usable code.
4. The old query forms of links (`?kodas=…`, `?tikslas=…`) are no longer read (owner: "we don't need old links to work").
5. A bill's preview uses the description only when a person wrote or checked it (not `ai`), and never the counts. If no:
   the AI text too, with "(sukurta DI)" after it.
6. A member's preview is the name and a generic line, never "Apie mane"; an organisation's is its own description while
   active and none while blocked. If no: "Apie mane" as the text (it is public on the page by the member's choice).
7. One image for every page, the logo and the name; no per-bill image with a title or numbers. If no: an image made per
   bill (needs a renderer on the server).
8. Pages of one's own account, staff, sign-in and one-time links are `noindex, nofollow` by a path list
   (`SecurityHeaders.IsPrivatePage`); the image is served `Cross-Origin-Resource-Policy: cross-origin` so other sites may
   show it.
9. Without `Site:Address` (a development machine) `og:url` and `og:image` are left out rather than taken from the
   request's host.

**Proposed by the designer while building the reporter's email (8.3), 2026-11-17, awaiting the owner**

- A text withdrawn or deleted by its own author while a report on it is open sends the reporter "Turinį atsiėmė pats
  autorius" (as the coordinator suggested); a comment that goes with the argument or statement it stood under sends "Jis
  nebeskelbiamas: pašalintas kartu su tekstu, po kuriuo buvo parašytas." If no: nothing in those cases.
- Publishing a waiting argument, confirming a description, restoring and "Palikti" all read "paliktas"; withdrawing an
  organisation's confirmation reads "Atšaukėme jos patvirtinimą". The link is given only while the text stands (kept,
  corrected, unconfirmed).
- Reports closed when the reporter's or the author's account is deleted, or when an organisation closes, send nothing.

**Proposed by the designer while building editing until someone reacts (8.2, 8.23.3), 2026-11-17, awaiting the owner**

- Every edit of a reason drops the moderator's "seen", flagged or not: only seen reasons go to MPs (8.3), and nobody has
  seen the new words. The edited reason is listed in the queue as not yet seen. If no: only a flagged edit drops it.
- An unflagged edit of a reason still waiting for a moderator keeps it waiting.
- No public mark such as "Redaguota" beside an edited reason or comment: nobody had reacted to the earlier words. If no:
  "Redaguota {data}" beside it.
- Only the comment's own writer edits a comment in an organisation's or the MP's name (deleting it is open to every
  representative, or to the MP's account and office); a comment kind switched off takes no edits, as it takes no new comments.

**Proposed by the designer while building the review of summaries (8.19.2), 2026-11-17, awaiting the owner**

- A summary with no bill left at its time is `dropped`: nothing goes, nothing is public, it counts as made for its week.
- An argument withdrawn or merged during review is replaced like a hidden one.
- A declined summary's public copy shows the bills and arguments as they were when it was declined, with the statement.
- A summary found after its time (the worker was down) is prepared and goes at once, on its sending day only.
- The box is not shown on the summaries screen itself; its counts take their Lithuanian forms ("1 projektas", "3
  projektai", "12 projektų") rather than the fixed "{n} projektų".
- `Digest:ReviewHours` takes 1 to 72 hours; the statements line of a future organisations part (8.14.9) follows the same
  rule as arguments (public when prepared).

**Proposed by the designer while building the email rhythm (8.24), 2026-11-16, awaiting the owner**

Each is built as written; "no" names what changes.

1. The daily and weekly emails go at the first run of the follow job from `Follows:ResponsesAt` (08:00, so 08:07), the
   setting relabelled "Rytinių laiškų laikas". If no: a setting of its own for each, or another hour.
2. The weekly email goes every Monday, whether or not the Seimas sits that week, and the day is fixed. If no: only in
   sitting weeks, or a setting for the day.
3. A gathered email holds what the job first saw before that morning's hour; an event seen at 08:07 waits a day. If no:
   the cut is the run itself (the 08:07 run's own events go in that morning's email).
4. "Cannot wait" means a planned date on or before the day of the person's next gathered email, decided when the date is
   first seen. If no: only a date strictly before that day (the email on the morning of the sitting is taken as in time).
5. A planned date that moves again within that window is sent early again; the one-a-day limit stays for "Iš karto" only.
   If no: at most one early email per bill per day, the later move waiting for the gathered email.
6. One email per person per run, an early "planned" line included when the run holds anything else for the person. If no:
   an early line is always an email of its own, so such a run can send two.
7. An email with one thing keeps that thing's own email; two or more make the gathered email with a part per kind and the
   subjects "Lietuva 2.0: naujienos apie tai, ką sekate" (daily: "dienos naujienos", weekly: "savaitės naujienos"). If no:
   every email of a run or a day is the gathered one, also with one thing in it.
8. After a change of the choice, what was waiting keeps its place and comes in the email it was waiting for. Going back
   within 15 minutes to the choice just left undoes the change (a corrected mis-click loses nothing, also an item waiting
   under "Iš karto"). What can still be lost: after two changes to two other choices before the first choice's email,
   what waited under the first; after an undo, what waited under the choice before the undone one's predecessor (only one
   earlier choice is kept); and an item a gathered pass took for other people during a mis-click that is then undone. If no:
   what waits moves into the new choice's first email, with a per-person mark of the carry-over.
9. MPs' answers are recorded per answer, with the three-day rule of votes, and are told within a run under "Iš karto";
   a missed morning is sent late at the next run rather than never. If no: the record per bill and day stays, and only
   the timing changes.
10. Changing the choice is audited without the choice itself. If no: not audited, like the switches.
11. At most one daily email a Vilnius day and one weekly email a week: a period's email is made once (`notify.rhythm_pass`),
   so moving `Follows:ResponsesAt` later the same day sends nothing more that day; a missed period is made at the next run.
   If no: the email follows the hour as it is at each run.
12. A switch is read when each email is made: a "planned" date that could not wait and was not sent early because its switch
   was off at that run is not told in the gathered email either, though the switch is on again by then. If no: the early
   pass marks the date per person, which the record (no person in it) cannot do.
13. The follow job's records are kept 30 days (past the 14-day look-back, a weekly email's week and the three-day rule),
   a planned date only until it is past; the daily housekeeping deletes older ones. If no: another number of days.

**Decisions needed before the stage that uses them**

| Decision | Options | Needed by |
|---|---|---|
| Email provider | An EU-owned service (for example Brevo or Scaleway) or Amazon SES in an EU region. Compare deliverability to `lrs.lt`, price, processor terms. | M0 |
| How accounts are verified once registration is open | **Stay with email** and label numbers as email-confirmed accounts; or add **phone verification** (an EU provider such as Infobip or Sinch, or a Lithuanian bulk-SMS service; paid per message; Lithuanian numbers only would exclude people abroad); or wait for the EU Digital Identity Wallet. The public pages promise one account per confirmed phone number, so staying with email also means rewording them. | Before M2b |
| CDN and DDoS protection | Bunny (EU) or Cloudflare. | Before M2b |
| Legal reading of the usage statistics | The lawyer gives the legal basis of the site's own counting (8.27: IP address and browser hashed with a salt kept until the day, month or year ends; counts only; `Privacy.Basis.Fact2.Value` is "[bus įrašyta]") and says whether e-privacy Art. 5(3) applies to it. The EDPB's Guidelines 2/2023 bring tracking by IP address under 5(3) when the address goes to a third party for tracking across sites; ours is first-party, on one site, and keeps nothing beyond the period. If 5(3) applies after all, the counting stops or asks consent; it never becomes a banner by default. | Before going live |
| Legal reading of the consent texts | A lawyer reads the positions consent texts together with the rule that writing a reason needs no consent (8.2): a reason is the author's own deliberate publication, public with name and chosen side, and withdrawable. Since 2026-11-18 (owner, version 2026-11-18) the positions consent covers "Man svarbu" marks as well as sides (6.5; the keys are in 12-decisions of that day); a question for the reading: whether a private mark on an argument, given on either side, is special-category data as a side is (the design treats it so). **[Owner, 2026-11-16: until going live we keep the terms, the privacy notice and the consent texts correct as drafts as the site changes; the lawyer reads them all once, before going live, not per change.]** When comments, "Sutinku" and the public profile are built, these keys change with them (a new terms version, 8.22.2): the terms (`LegalText.resx`: `Terms.Purpose.2`, `Terms.Publish.2`, `Terms.Publish.5`, `Terms.Moderation.1`, `Terms.Moderation.2`, `Terms.Leave.1`) and the privacy notice (`Privacy.Data.Fact2.Value`, `Privacy.Data.Fact5.Value`, `Privacy.Rights.Fact5.Value`) name what a member publishes or allows: comments, "Sutinku" under the name, the public profile with "Apie mane", being followed; the positions consent texts (`UiText.resx` `PositionConsent.Text`, `LegalText.resx` `Privacy.Positions.3`, which name "Man svarbu") and the methodology's "Kas matoma viešai, o kas ne" (`AppealText.resx` `Method.Privacy` and its part) say in one sentence that "Man svarbu" stays private (it tells the side) while "Sutinku" on a comment is public under the name and tied to no side (8.23.10); a new consent text, the "Viešas profilis" form's (kind `public-profile`), with its own version, saying that the page
is indexed, that others may follow and that the number of followers is shown (8.25.1); the privacy notice says a
search engine's copy outlives switching the profile off. With follower numbers built (8.11.2), the privacy notice says that how many follow a bill, an MP, an organisation or a member is public and who follows is not: `Privacy.Data.Fact6.Value` says each follow is counted in a public number naming nobody; to check with it: `Terms.Purpose.2` (following as what an account is for), `Terms.Leave.1` (deleting the account lowers the shared numbers, the follower numbers among them) and `Privacy.Rights.Fact5.Value` (unfollowing); a change to a `Terms.*` text is a new terms version (8.22.2). The methodology's `AppealText.resx` `Method.Privacy.1` and `Method.Privacy.Follow` (`/metodika#sekimas`) say what the number is and is not. Questions for the reading: comments carry no side and need no consent, as reasons (8.23.1); a public "Sutinku" is opinion data the person makes public themself (whether GDPR Art. 9(2)(e) is the ground, and whether the inline sentence suffices); a public profile gathers one person's published opinions in one place, by their choice (8.25), and "Apie mane" can state a political opinion or membership. | Once, before going live |
| AI provider for pre-screening | The description draft is decided (2026-11-13, in the confirmed list above). AI pre-screening of users' texts would need an EU-region route chosen first: the first-party Claude API has no EU-only processing. Until then pre-screening stays rule-based. | Before any AI pre-screening |
| Telemetry backend | Hosted free tier with EU storage, or self-hosted. | M0 |
| MP photos | Use the Seimas photos if their terms allow, otherwise initials. | M1 |
| Lithuanian terms for the new objects | Proposal: *kreipimasis*, *prašo balsuoti už / prieš*, *argumentas* (chosen by the owner on 2026-11-12 in place of *priežastis*), *suvestinė*, *atsakymas*; for comments (8.23.1, proposed 2026-11-16) *aptarimas* (the part under a text) and *komentaras* (one item), never *atsakymas*. | M2a; the comment terms before M2c |
| Comments under MPs' responses once real MPs are on the site (8.23.5) | Keep `Comments:OnResponses` on, as during the trial (the owner's decision of 2026-11-16 for the trial); or switch it off when going live. Decided with what the trial showed and after the talks with MPs' offices. | Before real MPs are on the site (with the introduction letter, 8.19.5) |
| Comments: the phone rule and links in arguments (8.23.3) | **The phone rule: decided by the owner, 2026-11-17 (8.23.3: Lithuanian shapes only).** For the links: arguments keep waiting for a moderator when they hold a link, because they can reach MPs. Or: leave the rule as it is; or let links in arguments through as in comments. | Before M2c |
| Follower numbers: counts, not rankings (8.11.2, 8.25.5) | **Proposed:** the number only on the thing's own page (the bill's page, the MP's, the organisation's, the member's profile), not in list rows (the agenda, the front page, `/projektai`, the MPs' list, `/organizacijos`) nor, for a member, in the card or beside the person's texts; nothing sorted, ranked or added up by it and no "most followed" list, until delegation is designed (a table of MPs ordered by it is a league table, 7.1). Or: a sort by followers on `/projektai` (bills only, the first candidate) and/or on the MPs' list, and the number in list rows. | Before follower counts are built |
| Follower numbers: zero and the wording (8.11.2) | **Proposed:** nothing shown with none (or "Sekėjai: 0"); a hint beside the number on every page that shows it, at least on the MP's profile, saying these are self-selected Lietuva 2.0 users who asked for emails, not support and not a representative poll, with the methodology's part on following saying it at length (or the methodology only). | Before follower counts are built |
| A follow and a future delegation (1.1) | **Proposed:** a follow never becomes a delegation by itself; delegation is its own explicit act, and follows that exist then are not converted (people follow those they disagree with, to watch them). No alternative recommended: a constraint on stage 3's design. | Stage 3 design |
| Public profile: the "Sutinku" a person gave (8.25.2) | **Proposed (built so):** not listed on the profile. Or: listed. | Before M2d |
| "Apie mane" only with a public profile (8.25.1) | **Proposed:** yes, one switch and one explanation. Or: "Apie mane" shown (in a card) also for members without a page. | Before M2d |
| "Apie mane": a personal code (8.25.3) | **Proposed:** refused, as everywhere. Or: allowed like the person's own contacts. | Before M2d |
| Following a member: what it sends (8.25.5) | **Proposed (built so):** a line when the member publishes an argument or a comment in their own voice, never the text, by the follower's rhythm, with its own switch. Or: arguments only. | Before M2d |
| Public profile: the party shown (8.25.9) | **Decided by the owner, 2026-11-17 ("Add party to public profile"), built:** the supported party already on "Profilis" (not a free field), shown on the public page only with its own box and consent (`party-public`), never by "Viešas profilis" alone; withdrawn at once with the box, the party or the profile; nothing for the MP's account; the "at least 20" rule unchanged (since made the setting `Digest:PartyGroupMinimum`, the owner's decision of the same day above). Still the designer's reading: changing the party withdraws it as removing does. | Done |
| Public profile: a display name to switch it on (8.25.6) | **Proposed (built so):** "Viešas profilis" needs a display name, and the name cannot be emptied while the profile is public (`profile.name-required`). Or: a page without a name, titled "Narys". | Before going live |
| Public profile: no cache, 410 and 404 marked `noindex` (8.25.2) | **Proposed (built so):** the page is `no-cache` for everyone so switching off is at once; the 410 and 404 answers carry `X-Robots-Tag: noindex`. Or: ordinary caching. | Before going live |
| A comment in an organisation's name when its last representative's account is deleted (8.23.7, 8.14.14) | Open, found while connecting comments and profiles: 8.23.7 says a comment written in an organisation's name stays when its writer's account is deleted (as statements do), but deleting the last representative's account closes the organisation (8.14.14), which withdraws its statements, and a withdrawn statement takes its comments with it (8.23.7). The code does both as written; since an organisation comments only under its own statement (8.23.2), the comments of the last representative go with the statements when the account is deleted, and the rule "stays" holds only while another representative remains. Either: keep it so (the closing decides); or keep a closed organisation's statements and their comments; or move the comment's rule to "stays unless its organisation is closed". | Before going live |
| The action row under a text (8.16.1) | **Decided by the owner, 2026-11-17 (built):** one row under every argument, statement, MP's response and comment, never wrapping from 360 px: "Man svarbu · n", "Aptarimas (n)", "Sutinku", then "⋯" (the browser's popover) with "Pranešti", one's own "Atsiimti"/"Ištrinti" and a moderator's "Moderuoti"; a chosen action opens directly under the row, an open discussion below it; small inline icons beside the words; the arguments at the page's full width, so the word "Aptarimas" shows at desktop widths and the sides stay side by side with a discussion open (only a phone under about 390 px keeps the word for screen readers). | Done |
| A link to a comment or an argument (8.23.6, 8.23.14 point 8) | **Proposed (built so, 2026-11-17):** `?komentaras={id}` / `?argumentas={id}` with the element's own fragment, resolved by the server: the target shown even behind "show all" or the responses' paging, its "Aptarimas" open through the linked comment rounded up to the next ten, the linked one marked; a comment not shown changes nothing (one 404 for hidden and unknown). Or: the fragment alone with a script that opens the thread (no help without script, and none for a comment not rendered). | Before going live |
| Public profile: the party shown (8.25.9) – the texts | Built 2026-11-17 (the row above). The box's sentence (`UiText` `Party.PublicText`, version 2026-11-17), the page's line ("Palaikoma partija: …"), `Party.Text`, and the privacy notice's `Privacy.Data.Fact7.Value`, `Privacy.Data.Fact12.Value`, `Privacy.Rights.Fact3.Value`, the terms' `Terms.Publish.6` and the methodology's `Method.Privacy.Follow` go to the native reader and the lawyer's one reading with the others. | Before going live |

**Risks**

| Risk | Mitigation |
|---|---|
| MPs' mail system filters the summaries, or MPs see them as spam | Introduction letter, low and capped frequency, both sides in every email, one-click stop, no tracking, discussions with offices before the first send. |
| A campaign registers many accounts to inflate one side. With email-only registration this costs nothing, so it is the main weakness of the numbers | Invitation-only trial, one account per normalised address, disposable domains refused, the 30-day count beside every number, surge note, moderator hold; phone verification stays available as an upgrade. The numbers stay a signal, and say so. |
| The upcoming-agenda page changes layout or is late | Watcher alerts, moderator-entered dates, "Planuojama …" wording, no extra emails when dates move. |
| Open-data service outages (seen today) | Raw responses stored, retries, pages show the last import time, imports resume. |
| Someone who knows a person's address uses up its daily codes, so the person cannot sign in by code, or cannot finish registering | [changed while building (2026-11-16): known browsers are built, 8.20.2: the person's usual browser keeps its own five codes an hour and ten a day, the same two settings counted separately, and a code only it can use, and a passkey is not affected.] What stays possible is keeping a person from signing in by code from a browser they have never signed in with, and keeping a new address from finishing its registration (a pending account has no known browser until it completes). Nothing leaks either way. |
| A leak of positions | Minimised data, no position in logs or admin screens, encrypted backups, impact assessment, penetration test, incident runbook. |
| Moderation load for a volunteer team | One record per bill and moderation after the fact (nothing waits for approval but flagged or reported arguments, 8.12.3), plain-text members' texts whose length is one setting (`Texts:MaxLength`, 2 000 characters for arguments, statements and responses, owner 2026-11-16: longer texts mean more reading per item, so the number can be lowered without a deploy, 5.6, 8.2), rule-based pre-screening, hourly rate limits per account, and limits per person per bill ready to switch on (off at the start, 8.2). [Changed on 2026-11-16: "no comments" was a mitigation until the owner decided to have them (8.23). In its place: comments are never in the moderators' list unless reported, pre-screening refuses a comment with personal data instead of holding it, there are no replies to replies and "Sutinku" carries no text, one hourly limit of their own and a ceiling per person per text ready to switch on, and a switch per kind that stops new comments at once. What stays: insults, spam and spam links in comments (links are allowed, 8.23.3) reach a moderator only by a report.] |
| MPs see "Aptarimas" under their answers as a place of abuse: a pile-on under an answer, the expectation that the MP answers every comment, offices less willing to answer at all (8.23.2) | The switch for responses: on during the trial by the owner's decision of 2026-11-16, decided again before real MPs are on the site (above); the MP's account and office comment only as the MP under its own response, never pressed to; no email to the MP about comments; nothing counts or lists comments an MP left unanswered; reports and moderation on the page; writing restrictions. |
| One person's arguments and comments gathered into a picture of their political views (8.25) | A public profile only by the member's choice (proposed), said in the form before saving, indexing included, so being found by search engines is the person's choice; the address retired (410) when the profile goes off; no page, id or link for anyone else; nothing on a profile about sides, "Man svarbu", what the person follows, counts of their texts or rankings. What stays: a member who chose a profile can be collected by anyone who visits it. |
| A followed member watched and pressed (8.25.5) | The profile switch is the consent to being followed and ends every follow at once; only the number of followers shows, nobody (the person included) is told who follows; harassment in comments answered by reports, hiding and the writing restriction. What stays: one particular follower cannot be blocked without showing followers. |
| The product drifts from "people tell MPs" towards a social network (8.25, 8.11.2) | Chosen by the owner on 2026-11-16, with a number of followers on every thing that can be followed, members included. The number stands only on the thing's own page and nothing is ranked, sorted or listed by it (proposed, 8.11.2); who follows is invisible to all but the follower; summaries carry no follower number. What stays: a visible count invites collecting followers, which is what a social network rewards. |
| MPs' follower numbers read as a popularity score or a scorecard (7.1, 8.11.2) | Before 2026-11-16 the rule was that no follower count is shown, so the question did not arise; the owner chose the numbers as delegation's predecessor. What now stands between a number and a scorecard: no ordering (no list sorted by it, no "most followed", no number in list rows), no comparison (no total per faction or party, nothing set beside another MP's number), and the note that these are self-selected users who asked for emails, not supporters and not a representative poll (all proposed, 8.11.2). What stays: anyone can open profiles one by one and build the table elsewhere; the site does not make it. |
| A bill rejected or withdrawn before its adoption stage is never compared on the analytics page (8.26.7): 8.1 says its record closes then, but the code closes a record only at the adoption-stage vote, which such a bill never gets | Said in 8.26.7; the comparison covers bills with an adoption-stage vote. Closing on an earlier rejection needs the open data to say so (the bill's status, 5.1), which is not read yet. |
| A member's comparison kept in server memory (8.26.6) reveals their sides to anyone who can read the web process's memory | Kept only in memory, never in a table, a log or the browser, bounded to 10 000 members and dropped after 12 idle hours, at sign-out, account deletion and withdrawal of the positions consent; the same exposure as the sides themselves in the database. |
| Running costs outlive enthusiasm (the old platform's fate) | One VPS, no paid infrastructure beyond email and backup storage. |
| Stage 2 ships before stage 3 and the public pages say otherwise | Update the pages when this order is agreed (below). |

**If this order is agreed, the public pages need:** the scope paragraph in `AGENTS.md` ("first version = voting",
"second version = posts"); chapter 09 of `koncepcija.html` and all of `antroji-versija.html` (appeals on bills instead of
free topics); the "many unrelated emails" passage in chapter 01, which already states this problem and could introduce the
summaries; `politikams.html`, where the summary email becomes the first concrete thing an MP receives. To be discussed
in chat before any page is edited, as `AGENTS.md` requires.

