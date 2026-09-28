# AGENTS.md – Lietuva 2.0 (LT2)

Working notes for anyone (human or AI agent) editing this repository. Read this before changing the documents. `CLAUDE.md` only imports this file (Claude Code reads `CLAUDE.md`); edit this file, not that one.

## What this is

Lietuva 2.0 (LT2) is a planned Seimas (Lithuanian parliament) engagement platform, run by VšĮ LT2.0 – the team that ran the first Lietuva 2.0 in 2011–2020. The new product:

- imports active Seimas bills and every MP's vote from the Seimas open data;
- explains bills in plain language (AI summaries, reviewed by people, linked to articles, with edit history);
- lets users vote on bills while the Seimas debates them, directly or by entrusting their vote to several delegates (liquid democracy, platform-only, no legal force);
- compares the LT2 result with the actual Seimas vote and lets verified politicians explain their votes.

This repository currently holds **pre-implementation documents only** (the concept, audience versions, a prototype). No product code yet.

## Branches

| Branch | Contents |
|---|---|
| `docs` | All current work: `docs/` (the website) and `discovery/` (team notes, prototypes). Develop here. |
| `legacy` | The old 2011–2020 ASP.NET MVC platform. Do not modify. |
| `master` | Intentionally empty (one commit removed all files; history kept). |

`docs` started from commit `eab53e7`, shared with `simelis/LT2:docs`, so the two repositories can exchange pull requests. Squash-merging breaks this link; prefer merge commits.

## Files on `docs`

| Path | Audience | Notes |
|---|---|---|
| `docs/index.html` | Citizens and first-time visitors | **Start page of lietuva2.lt**, built as a landing page: hero with two calls to action and the result card → why → four possibilities → bill page mockup (vote or delegate) → a politician's vote explanation → cards to the audience versions → trust → experience → ways to contribute and the contact. Detail (delegation table, analytics, rules) stays in `koncepcija.html`; keep only the most informative mockups here. |
| `docs/koncepcija.html` | Everyone | The main whitepaper: the canonical, complete concept of the first version with interactive mockups. |
| `docs/antroji-versija.html` | Everyone | The second version (posts, topics, answers) with its mockups. Split out of the main whitepaper to keep it short; the main whitepaper's chapter 09 summarises it and links here. |
| `docs/politikams.html` | Politicians | Short version plus the invitation to take part (the two steps below, what we ask / what they get, questions). These details live only here. |
| `docs/zurnalistams.html` | Journalists | What LT2 would offer and story ideas. The citation guide (correct vs incorrect wording) was removed as premature at this stage. |
| `docs/kas-nauja.html` | Former LT2 users | Lessons from 2011–2020, old vs new, what stays. |
| `docs/dokumentai.html` | People who want everything | Lists every page. Not linked from any page and marked `noindex`; the owner shares it on request. Update it when a page is added, renamed or removed. |
| `discovery/README.md` | – | English overview and table of pages. Keep in sync with `docs/dokumentai.html`. |
| `discovery/design-notes.md` | Team | Decision history: rejected alternatives and why, research sources (Seimas data, 2011–2020 figures), open questions, working with the owner. Read before re-opening a settled question. |
| `discovery/prototypes/seimas-vote-hemicycle/` | Team | `fetch_vote.py` + `make_page.py` draw a real Seimas vote as a seat map from open data. |

`docs/` is the website: its contents are uploaded as they are to the web root of lietuva2.lt, and nothing outside it is published. All links between pages are relative (`politikams.html`, `koncepcija.html#delegation`), so the folder works under any base URL, opened from disk, or on GitHub Pages from `/docs`. Keep it that way: no absolute site paths, and no internal notes inside `docs/`. In the text "the main whitepaper" means `koncepcija.html`; the page names were `lietuva20-*.html` before 2026-09.

Removed on purpose (do not recreate): `lietuva20-politikams-baltoji-knyga.html` (merged into the main whitepaper / politicians' short version), `lietuva20-aktyviems-pilieciams.html` (merged into the citizens' short version).

## Structure of the main whitepaper

Hero (result card) → 01 Kodėl dabar → 02 Kam ir kokia nauda →
**I Balsavimas:** 03 Projekto puslapis, 04 Balsavimo inicijavimas, 05 Balso delegavimas →
**II Politikai ir duomenų analizė:** 06 Politikai platformoje, 07 Analitika, 08 Rinkimų režimas →
**III Antroji versija:** 09 Kas rūpi žmonėms (a short summary with a link to `antroji-versija.html`) →
**IV Įgyvendinimas:** 10 Pasitikėjimas ir privatumas, 11 Mūsų patirtis, 12 Kaip prisidėti →
Priedas: Kas veikia arba buvo išbandyta kitur (table of e-democracy platforms).

Chapter numbers are written by hand in each `eyebrow`; the table of contents numbers itself with a CSS counter (`li.toc-intro` and `li.toc-part` are not counted). When chapters move, update eyebrows and every "žr. N skyrių" / "N skyriuje" / "N skyrius" reference.

## Product decisions (keep consistent everywhere)

**Scope**
- **First version = voting:** bill pages, AI summaries, initiating a vote, delegation, verified politician profiles with vote explanations, result card (also for sharing and embedding), CSV export, analytics, 2028 election comparison by real votes.
- **Second version = posts:** user posts and questions, feed, grouping into topics and questions, answers "once for everyone", "Mano temos", response rate, "Man svarbu", petitions from topics. Described in `antroji-versija.html`; chapter 09 of the main whitepaper only summarises it and links there. Short versions must not mention the second version, with one exception: the former-users page maps the old menu (Veikalapis, Pasisakymai) to it, always as "galėtų atsirasti … jei pirmoji pasiteisins" and linked to `antroji-versija.html`.
- Election mode for 2028 = comparison with MPs and factions by real votes only; candidates who are not MPs are compared by their LT2 votes. Candidates may also vote on bills the Seimas already decided: such votes are marked "po Seimo sprendimo", never enter the LT2 result, and their count is shown next to the match. Candidates who do not vote on LT2 are shown without percentages. Candidate questionnaire on future topics is a later phase (ideally with existing election guides).
- Municipal councils / European Parliament and a full API: "ateityje, be įsipareigojimų".

**Rules of the mechanics**
- LT2 voting runs from initiation until the Seimas actually votes. The Seimas agenda is published ~10–12 days ahead but items often slip, so never show a fixed countdown; show "Planuojama …" and close only when the vote happens. Delegators get a reminder when the bill enters the agenda.
- LT2 rezultatas = direct + delegated votes, fixed at the Seimas vote; all comparisons use it. "Su Seimo narių balsais" (formerly "Po Seimo") = additionally counts votes entrusted to MPs who did not vote on LT2, by their Seimas vote; informational only.
- Delegation: several delegates; experts in the bill's area (*sritis*) decide first, otherwise the majority; ties by list order; no chains; no default vote. A delegate's (e.g. an MP's) LT2 vote *lemia* the votes of delegators "kurie nepareiškė savo nuomonės". Not "įskaičiuojamas" and no "iškart" (with several delegates it decides only under the delegation rules); chapter 05 explains the rules, so don't repeat "kartu su kitais patikėtiniais" everywhere.
- Group statistics (delegators, party supporters) only when a group has ≥ 20 people.
- A faction's position in a Seimas vote = the option chosen by at least two thirds of its members who voted. Otherwise the faction voted freely ("balsavo laisvai"): such votes are excluded from "Balsavo kaip frakcija" (MP profiles) and from the party table in analytics, and their count is shown. Faction cohesion is still measured over all votes.
- A delegate's profile shows how delegators changed the delegate's areas (how many added or removed each area), as a signal of whether the declared expertise convinces.
- Registration: email + phone; optional stronger ID later via the EU Digital Identity Wallet.
- AI label never disappears after human edits: "Sukurta DI · taisė žmonės" + edit history.
- Bills are tagged in posts by name with `#`, not by XVP number.

**What we must not promise**
- Nothing about being free, no ads, or no paid promotion (removed deliberately). Use "all parties and politicians are shown on equal terms" instead.
- No full API (embeds and CSV only). No municipalities/EP.
- No roadmap or dates for releases: the plan was removed from all pages (2026-09) because it is not defined yet.
- LT2 results are a signal from self-selected users, never "the public", "voters" or a referendum.
- Not "the same tools for everyone" (paid plans may come), not a specific licence (e.g. GPL-3.0), not parties or organisations as future delegates.
- Never name the 2013 referendum initiative, even when citing the 12 000+ signatures. In the main documents say "we've done this before" without old-vs-new detail; the former-users page is the deliberate exception.

## Tone and wording

- Documents are in **Lithuanian**; the audience includes politicians. Keep a neutral-to-positive tone towards them: connection and explanation, not control or "gotcha". Prefer "skirtumas" over "atotrūkis"; "sutampa ir kur skiriasi" over "išsiskiria"; neutral chips over warning colours for differences.
- Headlines address nobody in particular (e.g. "Politikai ir rinkėjai – viena komanda", "Balsuojama tiesiogiai arba per kelis patikėtinius").
- Terms: "patikėti balsą" / "patikėtinis" (not "perleisti"), "Man svarbu" (not "Man irgi svarbu"), "LT2 rezultatas", "Seimo darbai ir pilietinis įsitraukimas" (subtitle), "Gyvas pokalbis tarp žmonių ir jų atstovų" (kicker).
- *Sritis* (not *tema*) for delegates' expertise and a bill's subject areas (ekonomika, švietimas …). *Tema* is reserved for the second version's grouping of posts.
- Politician benefit to state explicitly: "Jūsų rėmėjų pozicija. Apklausos rodo visuomenės nuomonę kai kuriais klausimais, bet retai – jūsų partijos rėmėjų, o konkrečiai jūsų asmeninių rėmėjų – beveik niekada." Vote explanations = a way to persuade voters and stay credible.
- The main whitepaper does **not** link to the short versions (readers arrive from the short versions, not the other way round). Short versions link to the main whitepaper.
- New Lithuanian copy should be read by a native speaker before it is shown externally.
- In English, *patikėtinis* is "delegate" (delegator → delegate); not "trustee" or "representative".

## Example data (all fictional; keep consistent across files)

- Example bill **XVP-0412**, "Gyventojų pajamų mokesčio įstatymo pataisa" (progressive income tax). Seimas final vote **78 : 41** (12 abstained, 10 absent; 60 % for). LT2 result **57 % for, 37 % against, 6 % abstain, 15 160 votes** (direct 6 800/4 280/770 + delegated 1 800/1 320/190). Card label "Dauguma sutampa: už · Skirtumas 3 p. p. · 3 Seimo nariai paaiškino balsą". After the first reading the platform stood at 48 % for.
- Delegation example: Rūta Mockutė (expert: ekonomika) – **prieš**, decides; Jonas Petraitis (MP, Reformų partija) – **prieš**; Darius Stankus – **už**. Jonas's LT2 vote on XVP-0412 is "prieš" everywhere.
- Topic example (second version): "Mokyklose trūksta psichologų" with four grouped questions.
- Analytics dot plot entry for XVP-0412: `s:60, p:57`.
- Every page carries a notice that all MPs, factions, bills and figures are fictional.

## Editing practices

- HTML files have **mixed CRLF/LF line endings**. Do not rewrite whole files with tools that normalise newlines. Make targeted replacements and check `git diff --stat` against `git diff --ignore-cr-at-eol --stat`: the two should match.
- Pages are self-contained HTML (inline CSS/JS, Google Fonts only). The main whitepaper's mockups are driven by inline JS (`renderBill`, `resultCard`, `drawHemi` …; the second-version page has `renderMT`); the result card is one component used in the hero, on the bill page after the vote, and as the share image.
- Gotcha: a global `figure svg{min-width:720px}` rule exists; don't wrap small SVGs in `<figure>`.
- Numbers shown with thousands separators: use `fmt()` (lt-LT, non-breaking space) and regexes that allow `[\d  ]`.
- After editing, open every page at 1280 px and 390 px width (Playwright with Chromium at `/opt/pw-browsers/chromium` in the cloud environment): no page errors, `scrollWidth` equal to viewport width, all `href="#…"` anchors and relative `.html` links resolve.

## Seimas open data (verified 2026-09)

- Base: `https://apps.lrs.lt/sip/` (CC BY 4.0). Useful endpoints: `p2b.ad_seimo_kadencijos`, `p2b.ad_seimo_sesijos?kadencijos_id=`, `p2b.ad_seimo_posedziai?sesijos_id=`, `p2b.ad_sp_darbotvarke?posedzio_id=` (items with planned time slot and stage: pateikimas / svarstymas / priėmimas), `p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=` (per-MP votes).
- Votes "Pritarta bendru sutarimu" (consensus) have no per-MP votes – show them without numbers; they add nothing to "Su Seimo narių balsais". Some results carry an official note that the electronic per-MP votes don't match the protocol totals – show it next to the result. More detail in `discovery/design-notes.md`.
- Open data lists sittings that have started; upcoming agendas appear on lrs.lt (`portal.show?p_r=35727&p_k=1`) about 10–12 days ahead and change often (the same item can be scheduled for adoption on several dates).

## Working with the owner

- Discuss complex mechanics in chat first; change the documents only after agreement.
- The owner's suggestions are proposals: weigh them, say where you disagree and why, then apply.
- State each thing once, most important first, in plain language; every page must be clear to someone who wasn't in the discussions.
- Don't present undecided things as promises ("galėtų", "jei bus poreikis").
- Before re-opening a settled question, read `discovery/design-notes.md` (decision history and rejected alternatives). If it disagrees with the pages or this file, the pages and this file win.

## Open questions / possible next steps

- No mockup yet shows the before-vote breakdown of a politician's supporters and delegators (only the analytics table shows it for final votes).
- The full participation plan exists only in the politicians' short version.
- **Nothing is built before the concept is discussed in detail with stakeholders.** Step 1 (the only one on offer now): discussions over the existing mockups with politicians and their offices, experts, journalists, active citizens and former users; the mockups are revised between rounds, and a summary goes to every participant. Step 2: a closed trial with a minimal working version and a few real Seimas bills – only if step 1 agrees on what to build, with no dates. Don't describe a pilot of a working product as the next step.
- The discussion format (regular meetings, a shared chat, personal interviews) is not decided; every page asks readers how they would like to take part. Don't describe a specific format as settled.
- Every page has a call to action with the contact `info@lietuva2.lt` (a `mailto:` link).
- The e-democracy appendix and the former-users page contain facts gathered from public sources in September 2026; re-check statuses before publishing.
