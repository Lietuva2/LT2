# Seimas open data

Read before touching the Parliament module's client or import. The design is [07-mp-profiles.md](../design/07-mp-profiles.md); the register of assumptions is `tests/Lt2.Parliament.ImportTests/Seimas/ASSUMPTIONS.md`.

- Base `https://apps.lrs.lt/sip/` (CC BY 4.0; show the attribution). Chain: `p2b.ad_seimo_kadencijos` →
  `p2b.ad_seimo_sesijos?kadencijos_id=` → `p2b.ad_seimo_posedziai?sesijos_id=` → `p2b.ad_sp_darbotvarke?posedzio_id=` →
  `p2b.ad_seimo_posedzio_eiga_full?posedzio_id=` → `p2b.ad_sp_balsavimo_rezultatai?balsavimo_id=`. MPs:
  `p2b.ad_seimo_nariai?kadencijos_id=` (`kadencijos_id=10` is 2024–2028; `asmens_id` is stable across terms).
- Votes approved by consensus have no per-MP votes: show them without numbers. Every result carries the same `komentaras`, a
  standing disclaimer that per-MP votes do not match the protocol totals, even where they add up exactly (register R19,
  R21). We count lrs.lt's per-MP rows against the totals instead (`VoteRules.RowsAddUp`): only when they differ does
  `VoteRules.OfficialNote` keep that sentence; any other note is shown next to the result (design 7.2). Every synthetic
  vote carries it (Seimas-wide totals, 14 MPs).
- Skip open-data sitting rows without a start time. The upcoming-agenda watcher is a separate source.
- The sitting agenda's `KlausimoStadija` provides the agenda ID, stage and e-seimas link. The full proceedings
  link via `kl_stad_id` and include votes on items absent from the agenda and votes on groups of items. Prefer a
  direct agenda link when a vote is repeated under a group. A group-only vote is recorded once without a bill.
- A bill is identified by its registration number (`parliament.bill.number`, unique; the Appeals module's records use it
  too); agenda items and votes by the Seimas's own IDs. No service gives the number as a field (checked 2026-11-24: the
  agenda has the title and an e-seimas link, the full proceedings the title and a `dok_key`; `p2b.tais_docs` has
  `projectRegistrationNo` but is a snapshot of one day's events across all of TAIS, without parameters). So an item's bill
  is read from its title (`SeimasParsers.BillInTitle`, `BillNumbers`; the upcoming-agenda pages by the same rule): the first
  number in the title's last "(Nr. …)" part that holds one, with its letters and without its revision
  (`(Nr. XIVP-4009GR(2))` is XIVP-4009GR; every revision is one bill). The proceedings only add a bill to an item the
  agenda listed without one; its title, stage and sitting stay the agenda's.
- `p2b.tais_docs` (design 7.2) is read last in the walk, at most hourly (`ImportSchedule.IsProjectsDue`), and no manual
  job forces it: it only creates bills and fills a missing title or e-seimas link, never links an agenda item
  (`SeimasParsers.Projects`, `ParliamentImporter.ApplyProjects`). A document is a bill when its whole
  `projectRegistrationNo` is a number (`BillNumbers.Whole`); of several revisions of one bill the highest wins. Seen
  2026-11-25: root `TAISProjEvents` (`eventsDate`, `creationDate`), `Events/Event`, and `Documents/Document` with
  `projectRegistrationNo`, `title`, `url`, `currentStatus`, `itemType`; about one in six documents is a Seimas bill
  (`XVP-…`, revisions as `XVP-1334(2)`). The parser reads only `Document` and its number, title and url.
- The upcoming agenda is not in the open data: the sittings are listed without a start time and their agendas are
  empty until approved. Staff enter it from the e-seimas agenda documents (design 8.4); `www.lrs.lt` and `e-seimas.lrs.lt`
  answer a server's request with a Cloudflare challenge (403), `apps.lrs.lt/sip/p2b.*` does not.
- The proceedings preserve the official decision as part of `aprasas`. `Sprendimas nepriimtas` reports no decision
  on the motion and does not count as a stage result. A vote's outcome and the bill's status are distinct (design 8.1).
- `BillCatalogue.RejectionsAsync` recognises the confirmed plenary wording for an approved motion to reject a specific
  bill (for example vote -60862). A later vote on that bill supersedes the rejection for automatic closure.
- The service is slow at times and drops connections: retry, and never let an outage take the site down.
- Confirm any field you have not seen in a live response before relying on it.
- A vote's `kind` and `stage` are the classification in force: `VoteClassification.Effective` of the rules'
  (`VoteRules.Classify`) and a moderator's correction (`kind_override`, `stage_override`, design 7.2). The import writes
  them through it on every parse and never touches the correction columns; anything new that classifies votes goes
  through it too, and readers read `kind`/`stage` only.
- The import (`ParliamentImporter.WalkAsync`) walks the chain once per run; each request has its own freshness rule
  (terms, MPs and sessions daily; open sessions' sittings every run; agendas, proceedings and results of sittings within
  `Seimas:RefetchDays`; a missing result on `ImportSchedule.IsMissingResultDue`; the TAIS projects hourly). `RunAsync(ImportParts force, …)`:
  the scheduled job passes `None`; the manual jobs (`ParliamentImportPartsJob` in `Lt2.Jobs`, ids `parliament-members`,
  `-sittings`, `-votes`, `-everything`, registered with `Cron.Never()`) force a part, which skips its rule
  (`ImportSchedule.IsForced`) and nothing else. The forced parts are written after the address in `import_run.source`
  (`ImportSchedule.RunSource`). The worker's Hangfire uses a sliding invisibility timeout, so a run over 30 minutes (the
  first import, a forced full re-read) is not fetched a second time by the other worker.
