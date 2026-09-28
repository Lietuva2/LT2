# Lietuva 2.0 — discovery

Pre-implementation work for the new Lietuva 2.0: a Seimas monitoring and public engagement platform
(bills from lrs.lt, AI summaries, public voting, vote delegation, verified politicians, Parliament vs. public analytics).

The previous platform (2011–2020, ASP.NET MVC) is kept on the `legacy` branch.

## Contents

The website is the `../docs/` folder: its contents are uploaded as they are to lietuva2.lt, and all links between pages are relative. The pages are in Lithuanian and open directly in a browser. `../docs/index.html` (the citizens' short version) is the start page; `../docs/dokumentai.html` lists every page and is shared on request.

| Path | Audience | What |
|---|---|---|
| `../docs/koncepcija.html` | Everyone | Full product concept with interactive mockups (the canonical version). |
| `../docs/antroji-versija.html` | Everyone | The second version: posts, topics and answers from representatives, with mockups. |
| `../docs/index.html` | Citizens | Start page (landing page): the short version with the key mockups, links to the audience versions and ways to contribute. |
| `../docs/politikams.html` | Politicians | Short version with the invitation to discuss the concept before anything is built, and questions for politicians. |
| `../docs/kas-nauja.html` | Former LT2 users | What changed since the 2011–2020 platform, lessons learned, what stays. |
| `../docs/zurnalistams.html` | Journalists | What LT2 would offer journalists and story ideas. |
| `design-notes.md` | Team | Decision history, rejected alternatives, research sources and open questions from the discovery work. |
| `prototypes/seimas-vote-hemicycle/` | Team | Feasibility check: renders a real Seimas vote from the open data API. |

When adding or renaming a page, update `../docs/dokumentai.html` as well.

All politicians, parties, bills and figures in the whitepaper mockups are fictional.

## Seimas vote hemicycle prototype

Fetches one vote from the Seimas open data service (`apps.lrs.lt/sip`, CC BY 4.0) and draws each MP's vote by faction.

```sh
cd prototypes/seimas-vote-hemicycle
python3 fetch_vote.py -60034                      # writes vote_-60034.json
python3 make_page.py -60034 "XVP-1586 priėmimas"  # writes hemi_-60034.html
```

`example-XVP-1586.png` is the result for vote `-60034` (68 for, 14 against, 23 abstained).
