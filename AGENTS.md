# AGENTS.md – Lietuva 2.0 (LT2)

Working notes for anyone (human or AI agent) editing this repository. Read this before changing the documents. `CLAUDE.md` only imports this file (Claude Code reads `CLAUDE.md`); edit this file, not that one.

## What this is

Lietuva 2.0 (LT2) is a Seimas (Lithuanian parliament) engagement platform run by VšĮ LT2.0, the team that ran the first Lietuva 2.0 in 2011–2020. This branch holds its public information website (`docs/`) and the discovery notes (`discovery/`). The product code and its design (the source of truth for what is built) are on `master`: `design/README.md` there.

## Branches

| Branch | Contents |
|---|---|
| `docs` | The information website (`docs/`) and team notes (`discovery/`). |
| `master` | The product code and its design (`design/`). |
| `legacy` | The old 2011–2020 ASP.NET MVC platform. Do not modify. |

Prefer merge commits over squash.

## Stages (keep consistent everywhere)

- **Stage 1 = built**, running as a closed trial (registration by invitation) at `https://test.lietuva2.lt`: MP profiles, votes and the agenda from Seimas open data; every active bill open on one page where members ask MPs to vote for or against and give reasons ("Man svarbu"); summaries to all MPs (prepared a day ahead for moderators to check; they go unless a moderator stops them; made and published, not sent during the trial); one MP response per bill; organisations' statements; following and emails; comments ("Aptarimas"); optional public profiles; the statistics page; moderation after the fact. Not publicly launched.
- **Stage 2 = idea:** free posts and questions to representatives, grouped into topics, answered once for everyone.
- **Stage 3 = idea:** voting on bills directly or through several delegates (*patikėtiniai*), the LT2 result beside the Seimas decision, election comparison.
- Stages 2 and 3 are ideas, never promises: not designed, no dates, "jei pirmasis etapas pasiteisins". They are described only in `idejos.html` and in one short part of `index.html`; `kas-nauja.html` may map the old menu to them.
- Describe stage 1 as built from `master`'s design, not from memory; when the design and a page disagree, the design wins. Keep pages high level: exact rules and numbers live on the site's own "Metodika" page.

## Files on `docs`

| Path | Audience | Notes |
|---|---|---|
| `docs/index.html` | First-time visitors | Start page of lietuva2.lt: what it is, how it works (four steps and one small example), what is built, trust, stages, audience cards, experience, how to help. |
| `docs/kaip-veikia.html` | Everyone | Every part of stage 1 briefly, and what remains before a public launch. |
| `docs/idejos.html` | Everyone | Stage 2 and 3 ideas. |
| `docs/politikams.html` | MPs and their offices | Summaries, taking over the profile, the office, one response, what we do not do, questions before the first summary. |
| `docs/organizacijoms.html` | Organisations | Profile, confirmation, statement on a bill, what an organisation gets. |
| `docs/zurnalistams.html` | Journalists | What is there, how to cite the numbers, story ideas. |
| `docs/kas-nauja.html` | Former LT2 users | Lessons from 2011–2020, old vs new, what stays. |
| `docs/vadovas-nariams.html`, `vadovas-seimo-nariams.html`, `vadovas-organizacijoms.html`, `vadovas-zurnalistams.html` | Members, MPs, organisations, journalists | Step-by-step guides with screenshots from a local stack with invented data (`docs/vadovas/*.webp`); a notice on each says so. Retake the shots when the screens they show change. |
| `docs/dokumentai.html` | People who want everything | Lists every page; `noindex`, not linked from any page. Update it when a page is added, renamed or removed. |
| `docs/lt2-preview.png` | – | The link preview image (1200 × 630) of every page: logo, "Padėkite Seimui jus atstovauti." Pages name it as `https://lietuva2.lt/lt2-preview.png` in `og:image`, the one absolute address allowed (share previews need one). |
| `docs/lt2-logo-spin.js` | – | `<lt2-logo-spin>`: the logo turning in 3D, edge-on a man and a woman, in the hero of every page. The only shared file the pages load. |
| `discovery/README.md` | – | English overview and table of pages. Keep in sync with `docs/dokumentai.html`. |
| `discovery/design-notes.md` | Team | Decision history of the 2026-09 concept (voting first). Historical: the pages and `master`'s design win where it disagrees. |
| `discovery/going-live.md` | Team | Questions to settle before a public launch of stage 1. |
| `discovery/prototypes/` | Team | The Seimas vote seat-map prototype and the logo-spin design history. |

`docs/` is the website: its contents are uploaded as they are to the web root of lietuva2.lt, and nothing outside it is published. All links between pages are relative, so the folder works under any base URL or opened from disk. No absolute site paths and no internal notes inside `docs/`. The pages share one frame (top bar, styles, footer) copied into each page; when it changes, change it in every page. Removed on purpose: `koncepcija.html` and `antroji-versija.html` (the 2026-09 concept of voting first; in git history).

## Wording

- Pages are in **Lithuanian**; new copy is read by a native speaker before it is shown widely. Neutral-to-positive towards politicians: connection and explanation, not control; "skirtumas", not "atotrūkis"; no rankings or scorecards.
- Terms as on the site: *kreipimasis*, *pozicija* ("prašo balsuoti už / prieš"), *argumentas*, "Man svarbu", *atsakymas* (an MP's), *suvestinė*, *sritis*, *patikėtinis* (stage 3 only). Members' sides are positions, never "balsai".
- Numbers come from self-selected "Lietuva 2.0 naudotojai", never the public or a referendum; say so beside any comparison. Planned Seimas dates are "Planuojama …".
- Never promise: that the service is free or ad-free, release dates, a public API, municipal councils or the European Parliament, a licence, parties or organisations as delegates. Never name the 2013 referendum initiative.
- Say plainly what the trial is: real Seimas data, participants' activity possibly invented, summaries not sent to MPs.

## Example data (all fictional; keep consistent with `master`)

Bill **XVP-0412** "Gyventojų pajamų mokesčio įstatymo pataisa": 6 800 ask to vote for, 4 280 against. MP **Jonas Petraitis** (Reformų partija), response "balsuosiu prieš". MP **Ona Jonaitienė** (Pilietinė sąjunga). Expert **Rūta Mockutė**. Every example is marked as invented.

## Working with the owner

- Discuss complex mechanics in chat first; the owner's suggestions are proposals: say where you disagree.
- State each thing once, most important first, in plain language. Don't present undecided things as settled.
- Never push, open a pull request or publish without the owner asking.

## Editing practices

- On Windows, run every command (git, python, dotnet, docker) in PowerShell, not Bash (Git Bash has broken mid-session on the owner's machine). Write multi-line scripts to a file and run the file. Cloud sessions run Linux and use their own shell.
- Pages are self-contained HTML (inline CSS, Google Fonts only), except `lt2-logo-spin.js`, which every page loads by a relative path; upload it together with the pages. Pages use LF line endings.
- Numbers with thousands separators use a non-breaking space (`6 800`).
- After editing, open every page at 1280 px and 390 px width (Playwright with Chromium at `/opt/pw-browsers/chromium` in the cloud environment): no page errors, `scrollWidth` equal to viewport width, all `href="#…"` anchors and relative `.html` links resolve.

## Seimas open data

`https://apps.lrs.lt/sip/` (CC BY 4.0). How the product reads it: `master`, `design/07-mp-profiles.md` and `agents/seimas-data.md`. The prototype under `discovery/prototypes/seimas-vote-hemicycle/` reads one vote.
