# Register of assumptions about the lrs.lt upcoming-agenda pages

The planned dates (design 8.4) come from lrs.lt pages, not from the open-data service, and no such page has been read
by this code yet: every line below is **assumed**. `AgendaParser` (src/Modules/Lt2.Parliament/Import/Agenda) names each
one in `AgendaAssumptions`; `AgendaRegisterTests` requires every id there to be listed here and nothing else.

To check them against the real site, set `LT2_LRS_LIVE=1` (and `LT2_LRS_AGENDA_URL` to the page, if not the default
below) and run `AgendaLiveTests`: it fetches the page once and reports what the parser found. Nothing fetches lrs.lt
otherwise: the worker's watcher is off unless `Agenda:BaseAddress` is set, and the host must be on the allowlist.

When the page cannot be read (no sitting date found), the watcher refuses it, logs an error (the alert of design 8.4),
and leaves the planned dates and the moderators' corrections as they were.

| Id | What the parser assumes | Status | Evidence |
|---|---|---|---|
| A1 | A sitting is introduced by its date written "2026 m. lapkričio 17 d." (year, genitive month, day); the bills that follow, until the next date, are planned for that sitting. Any other date on the page (for example "updated on …") starts an empty sitting and does no harm. | assumed | none; the stand-in page is written this way |
| A2 | A bill is named by its registration number, `[XVI]+P-` and digits, optionally with a version suffix "(2)", for example XVP-0412 or XIVP-1234(2). | assumed | the open data uses such numbers (`AGENTS.md`); not seen on the agenda page |
| A3 | The stage is a word of the row: pateikimas, svarstymas or priėmimas, in any grammatical form; a row without one has no stage. The first one named counts; several in one cell separated by commas ("Pateikimas, svarstymas, priėmimas") are one combined stage, read as the furthest of them (the day may end in it; owner, 2026-11-18). A title word naming a stage before the stage cell is still read as the stage. | assumed | the stand-in's page writes combined stages so |
| A4 | Each agenda item is a table row (`tr`), a list item (`li`) or a heading; text of one row belongs to one item. | assumed | none |
| A5 | A link to `e-seimas.lrs.lt` inside the row is the item's source (the bill's document, linked for a bill the open data does not know yet); otherwise the page address is. Where the date itself is published is the agenda page (A7): the bill's page links that address, kept with each snapshot, not this link (design 8.4, 2026-11-16). | assumed | none |
| A6 | The page is UTF-8 encoded. | assumed | none |
| A7 | The page lists the upcoming sittings at one address, configured as `Agenda:BaseAddress` + `Agenda:ListPath` (default `darbotvarke`); the real address is not known yet. | assumed | none |