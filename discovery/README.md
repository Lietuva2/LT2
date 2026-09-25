# Lietuva 2.0 — discovery

Pre-implementation work for the new Lietuva 2.0: a Seimas monitoring and public engagement platform
(bills from lrs.lt, AI summaries, public voting, vote delegation, verified politicians, Parliament vs. public analytics).

The previous platform (2011–2020, ASP.NET MVC) is kept on the `legacy` branch.

## Contents

| Path | What |
|---|---|
| `whitepaper/lietuva20-whitepaper.html` | Full whitepaper with interactive mockups (Lithuanian). Open in a browser. |
| `whitepaper/lietuva20-trumpai.html` | Short version (3-minute read). |
| `whitepaper/pdf/` | A4 PDF exports of both versions. |
| `prototypes/seimas-vote-hemicycle/` | Feasibility check: renders a real Seimas vote from the open data API. |

All politicians, parties, bills and figures in the whitepaper mockups are fictional.

## Seimas vote hemicycle prototype

Fetches one vote from the Seimas open data service (`apps.lrs.lt/sip`, CC BY 4.0) and draws each MP's vote by faction.

```sh
cd prototypes/seimas-vote-hemicycle
python3 fetch_vote.py -60034                      # writes vote_-60034.json
python3 make_page.py -60034 "XVP-1586 priėmimas"  # writes hemi_-60034.html
```

`example-XVP-1586.png` is the result for vote `-60034` (68 for, 14 against, 23 abstained).
