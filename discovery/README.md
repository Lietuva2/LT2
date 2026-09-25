# Lietuva 2.0 — discovery

Pre-implementation work for the new Lietuva 2.0: a Seimas monitoring and public engagement platform
(bills from lrs.lt, AI summaries, public voting, vote delegation, verified politicians, Parliament vs. public analytics).

The previous platform (2011–2020, ASP.NET MVC) is kept on the `legacy` branch.

## Contents

All whitepaper pages are in Lithuanian and open directly in a browser. `../index.html` at the repository root links to all of them.

| Path | Audience | What |
|---|---|---|
| `whitepaper/lietuva20-whitepaper.html` | Everyone | Full product concept with interactive mockups (the canonical version). |
| `whitepaper/lietuva20-trumpai.html` | Everyone | Short version (3-minute read). |
| `whitepaper/lietuva20-politikams-baltoji-knyga.html` | Politicians | Full partnership and pilot proposal. |
| `whitepaper/lietuva20-politikams-trumpai.html` | Politicians | Short version. |
| `whitepaper/lietuva20-aktyviems-pilieciams.html` | Active citizens | For activists and content creators: turning attention on Seimas bills into visible impact. |
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
