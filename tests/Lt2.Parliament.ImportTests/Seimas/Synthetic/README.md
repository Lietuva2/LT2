# Synthetic Seimas responses — invented, NOT recorded

Every file here was written by Lietuva 2.0, not received from the Seimas. Each one starts with the comment
`LT2-SYNTHETIC`, and the stand-in service (`tools/Lt2.SeimasStandIn`) also sends the header `X-Lt2-Synthetic` with
it, so whatever the import stores from them is marked as invented and the site says so on every page.

These fixtures give the stand-in and tests repeatable input. Their shape comes from the evidence listed in
[`../ASSUMPTIONS.md`](../ASSUMPTIONS.md). Real responses go to [`../Recorded/`](../Recorded/), never here.

File names: `<service>.xml`, or `<service>@<parameter>=<value>.xml` for a request with a query parameter. The stand-in
answers a known service asked about an unknown identifier with an empty `<SeimoInformacija/>`.

## The invented parliament

Term 10 (2024–2028). All identifiers start with 99, which no Seimas identifier seen so far does, so these rows cannot
be confused with real ones. Only 14 of the 141 seats are filled: the **totals** of each vote are Seimas-wide numbers
(XVP-0412: 71 : 38 after presentation, 74 : 44 after consideration), so the per-MP votes here never add up to the
totals. The per-MP list of a vote includes every sitting MP of the 14, with an empty `kaip_balsavo` for one who did not
vote. The real results all carry the same standing `komentaras` (register R21), which the import drops unless the rows do
not add up; here they never do, so every synthetic vote with rows carries that sentence (design 7.2). The synthetic
results leave `komentaras` empty, except 99100004, whose note stands for one the Seimas gives on a single vote.

| `asmens_id` | MP | Nominated by | Faction (ballot label) | Notes |
|---|---|---|---|---|
| 99000001 | Jonas Petraitis | Reformų partija | RPF | votes against XVP-0412 while his faction votes for |
| 99000002 | Marius Ąžuolaitis | Reformų partija | RPF | faction elder; constituency |
| 99000003 | Eglė Beržinytė | Reformų partija | RPF | chairs the budget committee |
| 99000004 | Vytautas Klevinskas | Reformų partija | RPF | constituency |
| 99000005 | Ona Jonaitienė | Pilietinė sąjunga | PSF | faction elder |
| 99000006 | Laima Uosytė | Pilietinė sąjunga | PSF | chairs the social affairs committee |
| 99000007 | Darius Liepaitis | Pilietinė sąjunga | PSF until 2026-04-30, then RAF | **changed faction** |
| 99000008 | Asta Pušinskienė | Pilietinė sąjunga | PSF | |
| 99000009 | Kęstutis Gluosnis | Regionų aljansas | RAF | faction elder |
| 99000010 | Indrė Eglaitė | Regionų aljansas | RAF | |
| 99000011 | Tadas Alksninis | Regionų aljansas | RAF | **mandate ended** 2026-04-15 |
| 99000012 | Gintarė Šermukšnytė | Regionų aljansas | RAF | |
| 99000013 | Aurelija Kadagytė | Regionų aljansas | RAF | mandate from 2026-04-21 (took Alksninis's seat) |
| 99000014 | Rasa Ievaitė | Reformų partija | RPF until 2026-04-30, then MG | **left the faction for the mixed group**, which is not a faction |

Factions: Reformų partijos frakcija (99100001), Pilietinės sąjungos frakcija (99100002), Regionų aljanso frakcija
(99100003); the mixed group, Mišri Seimo narių grupė (99100009), is a group, not a faction. Committees: Biudžeto ir finansų komitetas (99200001), Socialinių reikalų ir darbo komitetas (99200002).
Official addresses are `pavyzdys.<vardas>.<pavardė>@lrs.lt`, so no invented address can be a real mailbox's.

## Sittings and votes

| Sitting | Date | Item | Vote | What it exercises |
|---|---|---|---|---|
| 99001001 | 2026-03-17 | agenda | 99100001 procedural | an MP who did not vote; no bill |
| | | XVP-0412 presentation | 99100002, 71 : 38 : 14 | Regionų aljanso frakcija splits 2 : 2, a **free vote** |
| | | XVP-0533 presentation | 99100003 | **approved by consensus**: no per-MP votes, no results file |
| 99001002 | 2026-04-14 | XVP-0618 presentation | 99100004, 64 : 20 : 30 | the official **mismatch note**; PSF free (2 : 2) |
| 99001003 | 2026-05-19 | XVP-0412 consideration | 99100005 amendment | after the faction change and the new mandate |
| | | | 99100006, 74 : 44 : 10 | stage result; Liepaitis votes in RAF |
| 99001101 | 2026-09-15 | agenda | 99100101 procedural | autumn session |
| | | XVP-0618 consideration | 99100102 | PSF exactly two thirds (2 of 3); RAF 3 of 5 = free |

XVP-0412's adoption is still planned. Open data lists only sittings that have started, so it is not here.

## Statistics by hand

Seven votes have per-MP results (99100003 is a consensus vote and counts nowhere). A faction's position is the option at
least two thirds of its members who voted chose; "free" when no option reaches that. The mixed group (label MG) has no
position and its votes are neither counted nor free [decided 2026-11-12].

| MP | Recorded votes in mandate | Took part | With faction / faction had a position and MP voted | Free votes MP took part in |
|---|---|---|---|---|
| Jonas Petraitis | 7 | 7 | 5 / 7 | 0 |
| Ona Jonaitienė | 7 | 7 | 5 / 6 | 1 |
| Darius Liepaitis | 7 | 6 | 3 / 4 | 2 |
| Tadas Alksninis | 3 | 2 | 1 / 1 | 1 |
| Aurelija Kadagytė | 4 | 4 | 2 / 2 | 2 |
| Rasa Ievaitė | 7 | 7 | 3 / 3 (the four mixed-group votes have no figure) | 0 |

`tests/Lt2.Parliament.ImportTests/Import/ImportTests.cs` checks these numbers after a full import.

## The local stack's larger parliament (not here)

The stand-in container also loads its overlay, `tools/Lt2.SeimasStandIn/Recent`, over these files; tests never do. There
the parliament has the real Seimas's shape: these 14 MPs unchanged and 128 more (141 seated), in seven invented groups of
the real sizes (Reformų partijos frakcija 52, Pilietinės sąjungos 28, Regionų aljanso 17, Atviros visuomenės partijos 13,
Darnos sąjūdžio 11, Bendruomenių sąjungos 9, the mixed group 11), the votes above again with the ballots of all 141 (these
14 rows and the totals unchanged; the factions' positions and the statistics above hold for these files only), and the recent and upcoming sitting days.
`python tools/standin_overlay.py` writes it (fixed seeds); edit the generators, not the files.
