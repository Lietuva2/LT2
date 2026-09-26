# Design notes and decision history

Background from the discovery work in September 2026 that is **not** in the pages or in `CLAUDE.md`:
how the concept got its shape, what was tried and rejected (and why), the research behind the facts,
and questions still open. If this file and the pages or `CLAUDE.md` disagree, the pages and `CLAUDE.md` win –
they are newer. Read this before re-opening a settled question.

## 1. How the concept evolved

**The owner's original brief:** monitor the Seimas (bills from lrs.lt by scraping or an API), AI summaries,
public votes on bills, no bank-level authentication like the old platform, verified badges for politicians,
vote delegation to trusted people per topic (economy, education…), with a fallback to a party or several weighted politicians,
own vote always possible, Parliament-vs-public analytics, useful around elections. Early answers:
delegates can be both users and MPs; unverified accounts are fine; should work beyond the Seimas (later narrowed, see below);
first readers are politicians and other stakeholders the owner knows; keep the Lietuva 2.0 name and the logo
(three speech bubbles that form the map of Lithuania); interactive whitepaper; Lithuanian.

**Delegation went through six versions** before the current one:

1. Two levels: people and organisations (live) → MPs and parties (only after the Seimas vote), with optional weights (e.g. 60 % MP / 40 % party).
2. Three levels: choose a party → MPs and ministers pre-filled per topic from Seimas committees → people.
3. Delegates cover topics (declared, else committee, else "general"); gaps get suggestions; party as fallback.
4. Only LT2 votes count; order "topic delegates → general → party"; equal split among several delegates.
5. No levels: a short list of 1–3 delegates, majority decides, list order breaks ties, party and organisations allowed as delegates.
6. Experts first, then majority; individuals only in the first version; no fixed number of delegates.

What drove the changes: topics are fuzzy and bills often have several; levels were hard to explain ("delegation was turning into a set of counters");
declaring topics must *increase* a delegate's influence, not limit it; everything that counts must be overridable before the Seimas vote.

**Posts** started as the old platform's "Pasisakymai" (the `Problem` entity in the legacy code) plus questions to politicians,
became two post types (klausimas / pasisakymas), were merged into one post type, and got a topic page and the
"Mano temos" view for every delegate. Later (another session) they were moved to the second version.

**Scope narrowed** from "Seimas, municipal councils, Government, EP" to Seimas only until the 2028 elections,
with extensibility mentioned but not promised. Organisations were dropped from the first version.

## 2. Rejected alternatives and why

Delegation
- **Weighted or fractional votes** ("your vote counts 0.5 for"): fairer maths, but hard to explain. Majority chosen.
- **Party as a fallback level or as a delegate** (its vote = majority of its politicians on LT2): dropped with the levels.
  **Party supporters as a fallback** was rejected too: anyone can declare support, so a campaign could hijack it.
  Party support survives only as a private, self-declared setting for ordering suggestions and for supporter statistics.
- **Organisations as delegates.** Designs considered: board decides vs members' vote with a quorum; organisations vote only
  within their own topics; members first, board as fallback (never board overriding members); memberships expiring after
  12 months; a minimum of 20 voters; verified badge by company code in Registrų centras. Dropped for the first version because
  member lists go stale and are hard to maintain, and party member lists can only be matched by personal code, which we
  deliberately don't collect. May return later, only if needed.
- **Ministers and the Government as delegates.** A non-MP minister has no Seimas vote. The Government's opinion on a bill may be shown as information, never as a delegation.
- **Official Seimas votes counting for delegation** (e.g. "if my delegate didn't vote on LT2, use their Seimas vote"): the delegator could not override it,
  and it would remove MPs' reason to vote on LT2. Kept only as the separate "Po Seimo" result.
- **"Only these topics" restriction on a delegate:** dropped – limits give nobody a reason to declare topics; expertise gives priority instead.
- **One suggestion per uncovered topic:** too few; moot after the flat list.
- **Delegation chains** and **a default vote** when no delegate voted: never accepted (results must stay explainable).

Politicians
- **Constituency polling** shown to an MP (groups ≥ 50): many MPs are elected on the party list and have no constituency.
  Replaced by showing how the people who trust the politician voted themselves.
- **"Kalbėjo viena, balsavo kitaip"** (said one thing, voted another): subjective, needs interpretation of speeches, defamation risk.
  An intermediate "Balsavo pagal nurodytą poziciją" (stated position per bill and stage, shown after ≥ 10) was replaced by
  "Seime balsavo taip pat kaip LT2" once the MP's LT2 vote itself became the stated position.
- **"Teisė atsakyti"** (a pinned reply to summaries and indicators): unclear; ordinary comments cover it.
- **Answer rate always public:** replaced by public only at ≥ 70 % (the politician always sees it) – an incentive, not a pillory.

Posts
- **Separate question and statement types:** users shouldn't have to classify; the four responses (answer, preparing a bill, link to a Seimas bill, disagree) cover both.
- **"Noriu žinoti" button:** merged into one support button (now "Man svarbu").
- **Only MPs in the topic inbox / only MPs taggable:** experts and other delegates can answer too; one view for all delegates, MP-only extras.
- **Answers copied under every grouped post:** answers belong to the topic.
- **"DI sujungė X įrašų"** wording: plain "Sujungta X panašių įrašų".

Platform and paper
- **Importing every bill:** most are irrelevant; a user initiates a vote from the list of active bills.
- **Bank / VIISP identity checks** like 2011–2020: they put most people off; kept only as an optional stronger ID later.
- **A before/after switch inside the app:** the page switches by itself when the Seimas votes. The switch in the mockup is only a presentation device ("the same page at two moments").
- **The delegation simulator** in the whitepaper: replaced by an example table; a **click counter** in the delegate picker (left over from "as few clicks as possible") removed.
- **A fixed number of delegates (1–3):** removed; the tie rule is kept only in the full whitepaper (in short versions it invites debate).
- **Promises to drop:** "free", "same tools for everyone" (paid plans may come), GPL-3.0 (licence may change), municipalities/EP/API as plans, parties/organisations as future delegates.

Headlines considered for the hero (the current one is in `CLAUDE.md`)
- "Balsuokite, kol Seimas dar svarsto. Tada palyginkite su jo sprendimu." – accurate, not inspiring.
- "Seimas balsuoja. Jūs irgi.", "Demokratija ne tik rinkimų dieną", "Balsuokite ne kas ketverius metus, o kiekvieną savaitę".
- "El. demokratija – perkrauta" / "Lietuva 2.0 · perkrauta": *perkrauta* also means "overloaded".
- "Jūs esate Lietuva / Valstybė": sounds populist to politicians (could work as a closing line).
- "Rinkimai nėra viskas. Sprendimai yra viskas": belittles elections; softer rhythm: "Rinkimai – kas ketverius metus. Sprendimai – kiekvieną savaitę."
- "Kas vyksta Seime": a good name for a Seimas feed or weekly digest; possible collaboration with kasvyksta.lt
  (kaunas.kasvyksta.lt was among the top referrers in 2013).
- Unused lines that could open "Kodėl dabar": "Ar esame atstovaujami teisingai? Ar valdžios sprendimai atitinka rinkėjų valią? Mes galime tai sužinoti."

## 3. Research and sources

### Seimas open data (apps.lrs.lt/sip, CC BY 4.0) – details beyond `CLAUDE.md`
- The full chain works: term → session → sitting → agenda item (bill number XVP-…, stage, e-seimas link) →
  `p2b.ad_sp_klausimo_svarstymo_eiga` (the item's votes and their IDs) → `p2b.ad_sp_balsavimo_rezultatai` (each MP's vote with faction).
- `kadencijos_id=10` is the 2024–2028 term. The list of about 23 services is on lrs.lt, `portal.show?p_r=35391`.
- `p2b.ad_seimo_nariai`: `asmens_id` stays the same across terms (`kadencijų_skaičius` gives the count), `data_iki` marks the end of a mandate,
  nominating party (`iškėlusi_partija`), how elected (list or constituency), faction and committee memberships with dates (`Pareigos`),
  and the official email (`<Kontaktai rūšis="El. p." reikšmė="…@lrs.lt"/>`, 145 records in Sept 2026). This is what makes automatic MP verification possible.
- Items "Pritarta bendru sutarimu" (approved by consensus) have **no per-MP votes** – show them without numbers; such a vote gives nothing for "Po Seimo".
- Some results carry an official comment that the electronic per-MP votes don't match the protocol totals ("…neatitinka protokole įrašytų suminių rezultatų") – show it next to the result.
- No seat positions and no left-to-right faction order in the data; the seat map orders factions itself (by size, or coalition vs opposition).
- e-seimas bill page: Reg. Nr., Reg. data, Parengė (proposer), Būsena, Chronologija, related documents (committee conclusions, new versions). No convenient API – parse the pages.
  "Which article the committee changed" needs our own comparison of versions. The committee-sittings service exists, but its parameters weren't worked out (empty responses).
- Verified example: vote `-60034` (XVP-1586, presentation, 2026-09-15): 68 for, 14 against, 23 abstained, 105 of 140 voted, with the mismatch comment.
  The per-MP votes summed exactly to the totals. Prototype: `prototypes/seimas-vote-hemicycle/`.
- The service is sometimes slow or drops connections; retry requests.

### The 2011–2020 platform (code on the `legacy` branch)
- ASP.NET MVC 3, .NET 4.5, SQL Server (Entity Framework) + MongoDB. Pipeline Problem → Idea → Issue; wiki-like versioned summaries; for/against arguments; PDF export of issues.
- Bank-verified votes were stored in SQL with personal code, name, address and ID document number, digitally signed; other votes as `AdditionalVotes`.
  Storing that next to political opinions would be a GDPR problem today (Art. 9) – one reason to rebuild rather than port.
- The Seimas result was typed in by hand (`OfficialVote`). Login through banks (SEB, Swedbank, Nordea, DnB…) and VIISP. Politicians were only a flag (`User.IsPolitician`).
- Google Analytics ID `UA-266484-11`. Universal Analytics data was deleted by Google on 2024-07-01; it can't be recovered.

### Track record figures ("Mūsų patirtis") – where they come from
- **Archived list pages** (web.archive.org, `lietuva2.lt/pasiulymai` and `/sprendimai`, 2019 snapshots): about 320 ideas; 68 public votes (14 open, 51 finished, 3 other).
  Only the first 15 items per list were archived, so sums are lower bounds: 12 364 signatures on 47 visible ideas, over 11 000 votes on 35 of 68 votes,
  over 700 000 views (the top initiative alone had 149 296). The archive lists 9 signature drives.
- **The 12 000+ signatures include 7 393 e-signatures of the 2013 referendum initiative** (about 5 000 without it). The owner asked not to name the referendum in public documents – it was controversial.
- **Blog** (blog.lietuva2.lt, 12 posts): all e-signatures accepted by VRK while about 10 % of paper ones were not; 4 375 users at the end of 2013 (556 at its start);
  74 476 unique visitors in 2013; one blogger sent more visitors than any news portal; the platform ran on volunteers' time and its money ran out; data to be destroyed at the end of 2020.
  The blog's "possibly the first in the world" claim was not used (can't be backed up).
- **Facebook page** (205 posts, 2011-01-06 – 2019-11-22, read through the owner's browser): 7 official initiatives and signature drives (2013–2016) plus 3 appeals and letters;
  identity through DnB and Šiaulių bankas (2012-11), SEB and Swedbank (2013-03), Danske (2013-05), Citadele (2013-07) and the ID card (2012-11);
  data-controller registration 2012-07-12; VšĮ LT2.0 founded 2011-12-04; tripartite agreement LT2.0–VRK–IVPK 2013-09-29; first verified MP 2013-04-02;
  "Politikų tribūna" from 2013-12-10; in 2014 VRK got an award for accepting the e-signatures collected through Lietuva 2.0.
  The last post says the platform closed because it wasn't used actively enough. The difference between 7 (Facebook) and 9 (archive) drives: "Už blaivią" and the tobacco initiative.
- **Not claimed:** that the initiative caused the 2013 cut of required signatures from 50 000 to 25 000 (the posts don't say so). Early user counts (low, dated) were dropped.
- **Other possible sources** if exact numbers are ever needed: old emails with GA reports, the 2013 EU grant application with manabalss.lv, VšĮ LT2.0 annual reports at Registrų centras, a database backup if anyone kept one.

### Build approach (recommendation, not yet decided)
- Build from scratch rather than fork an existing platform (the platforms themselves are compared in the whitepaper appendix).
  The distinctive parts (Seimas import, AI summaries, expert-first delegation, comparisons, MP verification) must be written anyway;
  delegation without chains is a simple lookup, not graph traversal. LiquidFeedback: chains, party-internal decisions, rare Lua stack.
  DemocracyOS: unmaintained, no delegation. Decidim / CONSUL: built around municipal processes, AGPL.
- Stack depends on who builds it: Python/Django + PostgreSQL as a default (admin for moderation, handy for import jobs and AI), or modern .NET if the team knows C#.
- Liquid democracy references used in the paper: German Pirate Party (LiquidFeedback, from 2010), Liquid Friesland (from 2012); Google Votes also exists.
  Wikipedia was blocked from the cloud environment, so the links were not checked there.

### Environment notes (cloud sessions)
- Network allowlist needed: `*.lrs.lt`; `blog.lietuva2.lt` had to be added explicitly (the wildcard didn't cover it); `web.archive.org`. Facebook needs a login – use the owner's browser.
- The whitepaper pages start with `<title>` (no doctype); wrap them in `<!doctype html><html><head><meta charset="utf-8"></head><body>…` for Playwright tests.
- PDF export that worked: Playwright `page.pdf` on A4 with `scale: 0.72`, `printBackground`, print CSS hiding `.topbar`, `.toc`, `#tip`, white background,
  `break-inside: avoid` on mockups, rules and cards. The full whitepaper came out at 17 pages, the short version at 3.

## 4. Open questions carried over
- How long an MP keeps the @lrs.lt mailbox after the term – unknown; the design doesn't depend on it (verification is tied to `asmens_id`, new confirmations only for sitting MPs).
- Will MPs vote on LT2? Delegations to MPs count only when they do; early on "Po Seimo" may differ a lot from the LT2 result.
- Candidates with identical names in the VRK lists need a manual check.
- Funding and sustainability: the old platform stopped partly because money ran out; the paper has no section on it and stakeholders may ask.
- Amendments between stages: notify voters and delegates (agreed), don't reset votes.
- Tech stack and team.

## 5. Working with the owner
- Discuss complex mechanics in chat first; change the paper only after agreement.
- The owner's suggestions are proposals: weigh them, say where you disagree, then apply.
- State each thing once; most important first; plain language. No insider shorthand – every page must be clear to someone who wasn't in the discussions.
- Don't promise what isn't decided ("galėtų", "jei bus poreikis").
- Don't name the 2013 referendum initiative. In the main documents, say "we've done this before" without detailing old vs new (the former-users page is the deliberate exception).
- In English, *patikėtinis* is "delegate" (delegator → delegate); "trustee" sounds legal, "representative" is confused with MPs.
- Ask before pushing or publishing anything outward-facing.
