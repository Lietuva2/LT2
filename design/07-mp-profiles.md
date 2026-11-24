# 7. MP profiles

## 7.1 Sources

Everything below comes from the Seimas open data service (`https://apps.lrs.lt/sip/`, CC BY 4.0, attribution on every
page). The import reads one chain of services (`SeimasServices`), one query parameter each:

| Data | Service |
|---|---|
| Terms | `p2b.ad_seimo_kadencijos` |
| MPs of the term: person id (`asmens_id`), names, gender, mandate dates, nominating party, how elected, constituency, number of terms, memberships with roles and dates (`Pareigos`), contacts (`Kontaktai`, including the `@lrs.lt` address) | `p2b.ad_seimo_nariai?kadencijos_id=` |
| Sessions, sittings | `p2b.ad_seimo_sesijos?kadencijos_id=`, `p2b.ad_seimo_posedziai?sesijos_id=` |
| Agenda items with the bill number in the title, and the stage's ID, name and e-seimas link in `KlausimoStadija` | `p2b.ad_sp_darbotvarke?posedzio_id=` |
| Votes of the sitting's items, with the motion and official outcome in `aprasas`, joined to the agenda by `kl_stad_id` when available | `p2b.ad_seimo_posedzio_eiga_full?posedzio_id=` |
| Totals, the official note, and each MP's vote with faction | `p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=` |

The term imported is `Seimas:TermId` (10, 2024–2028). Factions, committees, commissions and groups come from the MPs'
memberships. Parliamentary groups have their own ID field. Other bodies' kinds are read from their names
("…frakcija", "…komitetas", "…komisija", "…grupė"), so the mixed
group of MPs is a group, not a faction. Every element and attribute the parsers read is a line of the register
`tests/Lt2.Parliament.ImportTests/Seimas/ASSUMPTIONS.md`, marked seen in a live response or assumed; the live contract
suite (`LT2_SEIMAS_LIVE=1`) checks every line against the real service.

The MP's photo, birth date and biography are not imported: a profile shows the MP's initials. The bill's e-seimas link
comes from the agenda item; nothing is read from e-seimas pages, the Central Electoral Commission (VRK) or the Chief
Official Ethics Commission (VTEK).

Deliberately not shown: parliamentary expenses and anything else that grades the MP's work, and no ranking or comparison
of MPs by any number, with one exception: a member's own "Kas Seime balsuoja taip, kaip prašote jūs", seen by that
member only (8.26.4). The profile shows what the MP did. The one number on it that is not about the MP's work is how
many members follow the MP (8.11.2): it says how many chose to hear about the MP, not what the MP did or how well; it
stands only on the profile, orders no list and is never added up per faction or party.

## 7.2 Import

- **One walk** (`ParliamentImporter`): terms → MPs → sessions → sittings → agenda → proceedings → vote results, at most
  one request a second, each with retries and exponential back-off. Each request has its own freshness rule: terms, MPs
  and sessions daily; the sittings of a session not ended, or ended within `Seimas:RefetchDays` (14), on every run; the
  agenda, proceedings and vote results of sittings that started within those days on every run, older ones once; a vote
  result still missing is asked for again daily while the vote is under 60 days old, weekly after that. The first run on
  an empty database reads the whole term; a run that broke off is resumed by the next one. Statistics are recomputed
  after each run that changed anything.
- **Recurring:** the scheduled job (`parliament-import`) is asked every 15 minutes and runs when due: every 15 minutes on
  a day with a sitting, hourly otherwise, and every 15 minutes until a first run succeeds. A run that imports nothing
  says why, never silently: a log line at information level ("Parliament import not due (last start yyyy-MM-dd HH:mm);
  skipped") and the parameter "Skipped" on the job's page of the jobs dashboard (6.3), also when an administrator
  triggered it by hand.
- **By hand:** four jobs on the jobs dashboard that never run on a schedule, only by "Trigger now":
  `parliament-members` (the terms, the MP list with memberships and contacts, the sessions), `parliament-sittings` (every
  session's sittings, every sitting's agenda and proceedings, however old), `parliament-votes` (every recorded vote's
  per-MP result, however old) and `parliament-everything` (all three, a full re-read as on an empty database). Each is
  the scheduled walk with its part forced: that part's requests are fetched again however fresh, the rest keeps the
  recurring rules, and everything else is as in a scheduled run (raw storage by hash, an unchanged body not applied
  again unless the parser is newer, one import at a time, one `import_run` per run with the forced parts after the
  address in its source, statistics recomputed). Ballots are matched to the MPs already imported, so the MPs come before
  the votes, and `parliament-everything` walks MPs, then sittings, then votes. A manual run that finds another import
  running, or that the service fails, fails the job, listed with its reason; its outcome is the job's parameter "Outcome".
- Every response is stored in `raw_document` with its hash; parsing is a separate, repeatable step. Upserts are keyed by
  Seimas identifiers, so re-running any step changes nothing. A ballot is matched to an MP by `asmens_id`, or by name
  among the term's MPs when the row has none.
- **Vote classification.** One agenda item can have many votes (amendments, procedure, the stage result). A rule set over
  the motion's wording (`VoteRules.Classify`) marks each vote a stage result, an amendment, a procedural vote or "other".
  An attempt reported as `Sprendimas nepriimtas` and anything the rules cannot classify are shown as "other", without a stage.
  The proceedings can supply an item absent from the agenda. A group-only vote is recorded once, without a bill.
- **A moderator corrects a classification** (owner, 2026-11-20). A moderator or an administrator in a staff-passkey
  session sees, on the vote's page (`/seimas/balsavimai/{id}`), a "Moderavimas" part: the classification in force
  ("Klasifikacija: Stadijos balsavimas · Priėmimas."), and either "Nustatė taisyklės pagal balsavimo formuluotę." or
  "Pataisė moderatorius {yyyy-MM-dd HH:mm}: {statement}" with the rules' own classification under it; never who. Nothing
  waits on a vote, so there is no filled button (8.3): "Veiksmai" holds "Pataisyti klasifikaciją" (the kind as radios:
  stage result, amendment, procedural, other; the stage as radios, shown only for a stage result; a required statement)
  and, on a corrected vote, "Grąžinti taisyklių klasifikaciją" (a required statement). Only the kind and a stage result's
  stage are corrected: an amendment takes its agenda item's stage, as the rules place one, and a procedural or other vote
  none; the totals, the ballots and the wording are Seimas data and stay as imported. A correction to what the vote has
  already (`vote-classification.unchanged`) or to the rules' own classification (`vote-classification.rules`: that is
  the return) is refused, so "corrected" always differs from the rules. Each action is audited (`vote.reclassified`,
  `vote.classification-reset`: the classification before and after and the statement, nothing about any person). The
  moderators' queue lists no votes.
- **The correction survives imports.** It is kept apart from the rules' result (`vote.kind_override`, `stage_override`,
  `overridden_at`, `overridden_by`, `override_statement`). One pure function (`VoteClassification.Effective`: the
  correction when there is one, otherwise the rules') writes the classification in force to `vote.kind` and `stage`, on
  every parse of the proceedings and on every moderator's action; re-parsing and re-importing run the rules again and
  never touch the correction. Every reader reads `kind` and `stage`: the vote, a bill's votes and stages, an MP's votes,
  a faction's votes, the history of votes and its filters (8.18), the agenda's recently voted, the follow emails, the
  bill's final vote (closing, MPs' responses) and through it the analytics (8.26).
- **The effects follow the classification in force.** A vote corrected into the adoption-stage result is the bill's
  final vote: the hourly job closes the bill's record at its next run (8.1 step 6). A record closed by the vote is never
  reopened, so a vote corrected out of the adoption stage could leave a closed record without its final vote; instead,
  the closing vote of a record (closed, or hidden after it closed) stays the adoption-stage result: a correction or a
  return to rules that classify it otherwise is refused (`vote-classification.closing-vote`, 409, whose text says why),
  and the moderators' part says so in place of the actions. Whether a vote closed a record is the Appeals module's answer
  (`IClosingVotes`).
- **Readers know.** The vote's page says "Klasifikaciją pataisė moderatoriai" beside the stage: a hint (8.15) whose
  answer says the rules classify a vote by its wording and that the moderators corrected this one, "Plačiau" to the
  methodology (`/metodika#stages`, which says moderators correct a wrong classification and that the correction
  survives re-reading the data). Never who or why. The API's vote answer carries `classificationCorrected`.
- Data quirks are first-class: consensus votes are stored with `is_consensus` and no ballots. The Seimas's note on a
  result (`komentaras`) is stored and shown next to the result, except the standing disclaimer "Elektroninėmis
  priemonėmis gauti individualūs balsavimo rezultatai neatitinka protokole įrašytų suminių rezultatų": lrs.lt puts it on
  every result whether or not the per-MP rows add up to the totals (all 6 295 results of the term, every one adding up,
  2026-11-24), so it says nothing about the vote beside it. A pure rule (`VoteRules.OfficialNote`) leaves it out when
  parsing; any other text is shown as written. The methodology says once, in "Ypatingi atvejai" (`/metodika#vote-notes`),
  that the Seimas gives this disclaimer, that the official result is the one in the sitting's protocol, and why the
  disclaimer is not shown beside each vote.
- A run the service fails is marked failed and logged as an error for the alerting (9.4); the next period retries, and
  the pages keep showing the last good data with its import time.

## 7.3 What a profile shows

The profile is `/p/{slug}`.

- Name, "Seimo narys" / "Seimo narė", or "Buvęs Seimo narys" / "Buvusi Seimo narė" when the mandate has ended; linked
  tags for the current faction and committees; how elected, constituency, nominating party, terms served, mandate dates.
- All current memberships with their roles under "Pareigos Seime", and former memberships with their dates.
- Official contacts (the e-mail address as a link).
- **Dalyvavimas balsavimuose:** of the recorded votes held during the MP's mandate, the share in which the MP voted for,
  against or abstained ("balsavo {n} iš {m}").
- **Balsavo kaip frakcija:** of the votes the MP took part in where the MP's faction had a position (at least two thirds
  of its members who voted chose the same option), the share where the MP chose that option. The number of free votes is
  shown beside it ("frakcija balsavo laisvai: {n}"). Votes the MP missed are not counted here: an absence already shows
  in the participation figure. The mixed group of MPs is not a faction and has no position: its members, and MPs outside
  any faction at the time of a vote, have no figure for those votes; when the MP's faction memberships do not cover the
  whole mandate, the profile names the periods counted ("Skaičiuojama tik narystės frakcijoje laikotarpiu: …").
- Up to ten of the latest recorded votes in which the MP voted for, against or abstained: bill, stage, date, the MP's
  choice, the faction's split and a link to the vote page. "Visi balsavimai" leads to the full mandate history
  (`/p/{slug}/balsavimai`, 8.18), including missed votes and decisions by consensus.
- The source line, as on every page of Seimas data: "Šaltinis: Lietuvos Respublikos Seimo atvirieji duomenys, CC BY 4.0
  · atnaujinta {time of the last import} · Metodika".
- The MP's responses (8.5.6), "Profilį tvarko Seimo narys arba jo biuras" when the profile is taken over (8.5.2), and
  "Sekti" with how many members follow the MP (8.11.2).

Also: the list of MPs (`/seimo-nariai`: search by name, filter by faction and committee), faction and committee pages
(`/frakcijos/{slug}`, `/komitetai/{slug}`), a vote page with the seat map rendered as SVG on the server
(`/seimas/balsavimai/{id}`), a bill page with its stages and votes (`/seimas/{number}`), and a public methodology page
(`/metodika`) that states each formula above in plain language.

## 7.4 API (all public, cached, with ETags)

```
GET /api/v1/terms/current
GET /api/v1/mps?faction=&committee=&q=&cursor=
GET /api/v1/mps/{slug}
GET /api/v1/mps/{slug}/votes?cursor=&limit=   newest first, 50 by default, at most 300
GET /api/v1/factions            GET /api/v1/factions/{slug}
GET /api/v1/committees          GET /api/v1/committees/{slug}
GET /api/v1/bills?q=&cursor=                 bills with votes, latest vote first
GET /api/v1/bills/{number}
GET /api/v1/votes?q=&kind=&until=&limit=&cursor=   the history of votes, newest first (8.18)
GET /api/v1/votes/{id}                      totals, wording, notes, faction positions, every ballot
GET /api/v1/meta/imports                    last successful import per source
```

A moderator's (7.2): `GET`, `PUT` (kind, stage, statement) and `DELETE` (statement)
`/api/v1/moderation/votes/{id}/classification`, never cached.

## 7.5 Done when

The parliament pages work at 1280 px and 390 px in both themes without sideways scrolling, and render without
JavaScript. An import outage leaves the site up and clearly dated (7.2). The two statistics are pure functions with unit
tests (`MpStatisticsRule`, `FactionRule`).
