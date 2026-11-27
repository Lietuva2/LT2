# Lietuva 2.0 — information website and discovery notes

Lietuva 2.0 is a Seimas engagement platform. Stage 1 is built and runs as a closed trial at https://test.lietuva2.lt;
its code and design are on the `master` branch (`design/README.md`). This branch holds the public information website
and the team's notes. The previous platform (2011–2020) is on the `legacy` branch.

## Contents

The website is the `../docs/` folder: its contents are uploaded as they are to lietuva2.lt, and all links between pages
are relative. The pages are in Lithuanian and open directly in a browser. `../docs/dokumentai.html` lists every page
and is shared on request.

| Path | Audience | What |
|---|---|---|
| `../docs/index.html` | First-time visitors | Start page: what LT2 is, how it works, what is built, the stages, how to help. |
| `../docs/kaip-veikia.html` | Everyone | Every part of stage 1 briefly, and what remains before a public launch. |
| `../docs/idejos.html` | Everyone | Ideas for stage 2 (posts and questions) and stage 3 (voting with delegates). Not promises. |
| `../docs/politikams.html` | MPs and their offices | Summaries, taking over the profile, one response for everyone, questions before the first summary. |
| `../docs/organizacijoms.html` | Organisations | Profile, confirmation by domain, a statement on a bill: a voice, not control. |
| `../docs/zurnalistams.html` | Journalists | What is there, how to cite the numbers, story ideas. |
| `../docs/kas-nauja.html` | Former LT2 users | Lessons from 2011–2020, old vs new, what stays. |
| `../docs/vadovas-*.html` | Members, MPs, organisations, journalists | Step-by-step guides with screenshots (`../docs/vadovas/`), taken with invented data. |
| `going-live.md` | Team | Questions to settle before a public launch of stage 1. |
| `design-notes.md` | Team | Decision history of the 2026-09 concept (voting first). Historical. |
| `prototypes/seimas-vote-hemicycle/` | Team | Feasibility check: renders a real Seimas vote from the open data API. |
| `prototypes/logo-spin/` | Team | Demo and design history of `../docs/lt2-logo-spin.js`, the rotating logo at the top of every page. |

When adding or renaming a page, update `../docs/dokumentai.html` as well. All politicians, parties, bills and figures in
the examples are fictional.

## Seimas vote hemicycle prototype

```sh
cd prototypes/seimas-vote-hemicycle
python3 fetch_vote.py -60034                      # writes vote_-60034.json
python3 make_page.py -60034 "XVP-1586 priėmimas"  # writes hemi_-60034.html
```

`example-XVP-1586.png` is the result for vote `-60034` (68 for, 14 against, 23 abstained).
