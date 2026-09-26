# Lietuva 2.0 — discovery

Pre-implementation work for the new Lietuva 2.0: a Seimas monitoring and public engagement platform
(bills from lrs.lt, AI summaries, public voting, vote delegation, verified politicians, Parliament vs. public analytics).

The previous platform (2011–2020, ASP.NET MVC) is kept on the `legacy` branch.

## Contents

All whitepaper pages are in Lithuanian and open directly in a browser. `../index.html` at the repository root links to all of them.

| Path | Audience | What |
|---|---|---|
| `whitepaper/lietuva20-whitepaper.html` | Everyone | Full product concept with interactive mockups (the canonical version). |
| `whitepaper/lietuva20-trumpai.html` | Citizens | Short version, including a section for people who mobilise an audience. |
| `whitepaper/lietuva20-politikams-trumpai.html` | Politicians | Short version with the closed pilot proposal and questions for politicians. |
| `whitepaper/lietuva20-kas-nauja.html` | Former LT2 users | What changed since the 2011–2020 platform, lessons learned, what stays. |
| `whitepaper/lietuva20-zurnalistams.html` | Journalists | What LT2 offers journalists, how to cite its results correctly, story ideas. |
| `design-notes.md` | Team | Decision history, rejected alternatives, research sources and open questions from the discovery work. |
| `prototypes/seimas-vote-hemicycle/` | Team | Feasibility check: renders a real Seimas vote from the open data API. |

When adding or renaming a whitepaper page, update `../index.html` as well.

All politicians, parties, bills and figures in the whitepaper mockups are fictional.

## Seimas vote hemicycle prototype

Fetches one vote from the Seimas open data service (`apps.lrs.lt/sip`, CC BY 4.0) and draws each MP's vote by faction.

```sh
cd prototypes/seimas-vote-hemicycle
python3 fetch_vote.py -60034                      # writes vote_-60034.json
python3 make_page.py -60034 "XVP-1586 priėmimas"  # writes hemi_-60034.html
```

`example-XVP-1586.png` is the result for vote `-60034` (68 for, 14 against, 23 abstained).
