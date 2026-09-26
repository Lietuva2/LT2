# CLAUDE.md – Lietuva 2.0 (LT2)

Working notes for anyone (human or Claude) editing this repository. Read this before changing the documents.

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
| `docs` | All current work: `discovery/` and `index.html`. Develop here. |
| `legacy` | The old 2011–2020 ASP.NET MVC platform. Do not modify. |
| `master` | Intentionally empty (one commit removed all files; history kept). |

`docs` started from commit `eab53e7`, shared with `simelis/LT2:docs`, so the two repositories can exchange pull requests. Squash-merging breaks this link; prefer merge commits.

## Files on `docs`

| Path | Audience | Notes |
|---|---|---|
| `index.html` | – | Lists every page. Update it when a page is added, renamed or removed. |
| `discovery/README.md` | – | English overview and table of pages. Keep in sync with `index.html`. |
| `discovery/whitepaper/lietuva20-whitepaper.html` | Everyone | The canonical, complete concept with interactive mockups. |
| `discovery/whitepaper/lietuva20-trumpai.html` | Citizens (incl. people who mobilise an audience) | Short version. |
| `discovery/whitepaper/lietuva20-politikams-trumpai.html` | Politicians | Short version plus the closed pilot proposal (6-week plan, what we ask / what they get, questions). The pilot details live only here. |
| `discovery/whitepaper/lietuva20-zurnalistams.html` | Journalists | What LT2 offers, how to cite its results correctly, story ideas. |
| `discovery/whitepaper/lietuva20-kas-nauja.html` | Former LT2 users | Lessons from 2011–2020, old vs new, what stays. |
| `discovery/prototypes/seimas-vote-hemicycle/` | Team | `fetch_vote.py` + `make_page.py` draw a real Seimas vote as a seat map from open data. |

Removed on purpose (do not recreate): `lietuva20-politikams-baltoji-knyga.html` (merged into the main whitepaper / politicians' short version), `lietuva20-aktyviems-pilieciams.html` (merged into the citizens' short version).

## Structure of the main whitepaper

Hero (result card) → 01 Kodėl dabar → 02 Kam ir kokia nauda →
**I Balsavimas:** 03 Projekto puslapis, 04 Balsavimo inicijavimas, 05 Balso delegavimas →
**II Politikai ir palyginimas:** 06 Politikai platformoje, 07 Analitika, 08 Rinkimų režimas →
**III Antroji versija:** 09 Kas rūpi žmonėms →
**IV Įgyvendinimas:** 10 Pasitikėjimas ir privatumas, 11 Mūsų patirtis, 12 Veiksmų planas, 13 Kaip prisidėti →
Priedas: Kas jau veikia kitur (table of e-democracy platforms).

Chapter numbers are written by hand in each `eyebrow`; the table of contents numbers itself with a CSS counter (`li.toc-intro` and `li.toc-part` are not counted). When chapters move, update eyebrows and every "žr. N skyrių" / "N skyriuje" / "N skyrius" reference.

## Product decisions (keep consistent everywhere)

**Scope**
- **First version = voting:** bill pages, AI summaries, initiating a vote, delegation, verified politician profiles with vote explanations, result card (also for sharing and embedding), CSV export, analytics, 2028 election comparison by real votes.
- **Second version = posts:** user posts and questions, feed, grouping into topics and questions, answers "once for everyone", "Mano temos", response rate, "Man svarbu", petitions from topics. Described only in chapter 09 of the main whitepaper. Short versions must not mention the second version at all.
- Election mode for 2028 = comparison with MPs and factions by real votes only; newcomers shown without percentages. Candidate questionnaire on future topics is a later phase (ideally with existing election guides).
- Municipal councils / European Parliament and a full API: "ateityje, be įsipareigojimų".

**Rules of the mechanics**
- LT2 voting runs from initiation until the Seimas actually votes. The Seimas agenda is published ~10–12 days ahead but items often slip, so never show a fixed countdown; show "Planuojama …" and close only when the vote happens. Delegators get a reminder when the bill enters the agenda.
- LT2 rezultatas = direct + delegated votes, fixed at the Seimas vote; all comparisons use it. "Po Seimo" = additionally counts votes entrusted to MPs who did not vote on LT2, by their Seimas vote; informational only.
- Delegation: several delegates; topic experts decide first, otherwise the majority; ties by list order; no chains; no default vote. A delegate MP's LT2 vote is *įskaičiuojamas* for delegators "kurie nepareiškė savo nuomonės".
- Group statistics (delegators, party supporters) only when a group has ≥ 20 people.
- Registration: email + phone; optional stronger ID later via the EU Digital Identity Wallet.
- AI label never disappears after human edits: "Sukurta DI · taisė žmonės" + edit history.
- Bills are tagged in posts by name with `#`, not by XVP number.

**What we must not promise**
- Nothing about being free, no ads, or no paid promotion (removed deliberately). Use "all parties and politicians are shown on equal terms" instead.
- No full API (embeds and CSV only). No municipalities/EP.
- LT2 results are a signal from self-selected users, never "the public", "voters" or a referendum.

## Tone and wording

- Documents are in **Lithuanian**; the audience includes politicians. Keep a neutral-to-positive tone towards them: connection and explanation, not control or "gotcha". Prefer "skirtumas" over "atotrūkis"; "sutampa ir kur skiriasi" over "išsiskiria"; neutral chips over warning colours for differences.
- Headlines address nobody in particular (e.g. "Politikai ir rinkėjai – viena komanda", "Balsuojama tiesiogiai arba per kelis patikėtinius").
- Terms: "patikėti balsą" / "patikėtinis" (not "perleisti"), "Man svarbu" (not "Man irgi svarbu"), "LT2 rezultatas", "Seimo darbai ir pilietinis įsitraukimas" (subtitle), "Gyvas pokalbis tarp žmonių ir jų atstovų" (kicker).
- Politician benefit to state explicitly: "Jūsų rėmėjų pozicija. Apklausos rodo visuomenės nuomonę, retai – jūsų partijos rėmėjų, o asmeniškai jūsų rėmėjų – beveik niekada." Vote explanations = a way to persuade voters and stay credible.
- The main whitepaper does **not** link to the short versions (readers arrive from the short versions, not the other way round). Short versions link to the main whitepaper.
- New Lithuanian copy should be read by a native speaker before it is shown externally.

## Example data (all fictional; keep consistent across files)

- Example bill **XVP-0412**, "Gyventojų pajamų mokesčio įstatymo pataisa" (progressive income tax). Seimas final vote **78 : 41** (12 abstained, 10 absent; 60 % for). LT2 result **57 % for, 37 % against, 6 % abstain, 15 160 votes** (direct 6 800/4 280/770 + delegated 1 800/1 320/190). Card label "Dauguma sutampa: už · Skirtumas 3 p. p. · 3 Seimo nariai paaiškino balsą". After the first reading the platform stood at 48 % for.
- Delegation example: Rūta Mockutė (expert: ekonomika) – **prieš**, decides; Jonas Petraitis (MP, Reformų partija) – **prieš**; Darius Stankus – **už**. Jonas's LT2 vote on XVP-0412 is "prieš" everywhere.
- Topic example (second version): "Mokyklose trūksta psichologų" with four grouped questions.
- Analytics dot plot entry for XVP-0412: `s:60, p:57`.
- Every page carries a notice that all MPs, factions, bills and figures are fictional.

## Editing practices

- HTML files have **mixed CRLF/LF line endings**. Do not rewrite whole files with tools that normalise newlines. Make targeted replacements and check `git diff --stat` against `git diff --ignore-cr-at-eol --stat`: the two should match.
- Pages are self-contained HTML (inline CSS/JS, Google Fonts only). The main whitepaper's mockups are driven by inline JS (`renderBill`, `resultCard`, `drawHemi`, `renderMT` …); the result card is one component used in the hero, on the bill page after the vote, and as the share image.
- Gotcha: a global `figure svg{min-width:720px}` rule exists; don't wrap small SVGs in `<figure>`.
- Numbers shown with thousands separators: use `fmt()` (lt-LT, non-breaking space) and regexes that allow `[\d  ]`.
- After editing, open every page at 1280 px and 390 px width (Playwright with Chromium at `/opt/pw-browsers/chromium` in the cloud environment): no page errors, `scrollWidth` equal to viewport width, all `href="#…"` anchors and relative `.html` links resolve.

## Seimas open data (verified 2026-09)

- Base: `https://apps.lrs.lt/sip/` (CC BY 4.0). Useful endpoints: `p2b.ad_seimo_kadencijos`, `p2b.ad_seimo_sesijos?kadencijos_id=`, `p2b.ad_seimo_posedziai?sesijos_id=`, `p2b.ad_sp_darbotvarke?posedzio_id=` (items with planned time slot and stage: pateikimas / svarstymas / priėmimas), `p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=` (per-MP votes).
- Open data lists sittings that have started; upcoming agendas appear on lrs.lt (`portal.show?p_r=35727&p_k=1`) about 10–12 days ahead and change often (the same item can be scheduled for adoption on several dates).

## Open questions / possible next steps

- No mockup yet shows the before-vote breakdown of a politician's supporters and delegators (only the analytics table shows it for final votes).
- The full pilot plan exists only in the politicians' short version.
- The e-democracy appendix and the former-users page contain facts gathered from public sources in September 2026; re-check statuses before publishing.
