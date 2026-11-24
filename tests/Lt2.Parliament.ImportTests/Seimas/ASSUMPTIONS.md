# Register of assumptions about the Seimas open data

Every element, attribute, value, format, query parameter and behaviour the parsers and the import rely on, each marked
**seen** (seen in a live response, with the source) or **assumed**. `AGENTS.md` says to confirm any field not seen live
before relying on it. To check the register against the official feeds, run the live
contract suite (`LT2_SEIMAS_LIVE=1`, see `Recorded/README.md`) and it reports on every line.

Sources of "seen":

- **live XML**: official `apps.lrs.lt/sip` responses for term 10, session 145, sittings -502176 and -502205, and vote -60034;
- the following are from the September 2026 discovery work on the `docs` branch:

- **notes**: `discovery/design-notes.md`, section 3 "Seimas open data";
- **fetch_vote**: `discovery/prototypes/seimas-vote-hemicycle/fetch_vote.py`, whose regular expressions matched live
  responses of `p2b.ad_sp_balsavimo_rezultatai` (vote `-60034`, 2026-09-15);
- **design**: `design/07-mp-profiles.md` (section 7.1), endpoints marked ✔ (checked September 2026);
- **AGENTS**: `agents/seimas-data.md`.

How the tests use this file:

- `RegisterTests` parses every synthetic response while recording each element and attribute the parsers read, and
  requires the `element` and `attribute` lines of each service to be exactly that set: a parser cannot read a name that
  is not here, and no line here is stale.
- `LiveContractTests` checks every line against the real responses. Verdicts: **confirmed**, **contradicted** (the
  response has the context but not this), **not observed** (the sample did not contain the case, for example no
  consensus vote), **manual** (a behaviour no single response can show).

Columns: `Within` is the element an element is looked for under, or an attribute's element; `(document)` means anywhere
in the response, so neither the root element's name nor the nesting depth is relied on. Elements are matched by local
name; a namespace on the real elements would contradict every element line.

| Id | Service | Kind | Name | Within | Status | Evidence | Relied on for |
|---|---|---|---|---|---|---|---|
| T1 | p2b.ad_seimo_kadencijos | element | SeimoKadencija | (document) | assumed | | one row per term |
| T2 | p2b.ad_seimo_kadencijos | attribute | kadencijos_id | SeimoKadencija | seen | live XML | term key |
| T3 | p2b.ad_seimo_kadencijos | attribute | pavadinimas | SeimoKadencija | assumed | | term name |
| T4 | p2b.ad_seimo_kadencijos | attribute | data_nuo | SeimoKadencija | assumed | | term start |
| T5 | p2b.ad_seimo_kadencijos | attribute | data_iki | SeimoKadencija | seen | live XML | term end; empty while current |
| M1 | p2b.ad_seimo_nariai | element | SeimoNarys | (document) | assumed | | one row per MP of the term |
| M2 | p2b.ad_seimo_nariai | attribute | asmens_id | SeimoNarys | seen | notes: "asmens_id stays the same across terms" | person key, stable for life |
| M3 | p2b.ad_seimo_nariai | attribute | vardas | SeimoNarys | seen | live XML | first name |
| M4 | p2b.ad_seimo_nariai | attribute | pavardė | SeimoNarys | seen | live XML | last name |
| M5 | p2b.ad_seimo_nariai | attribute | lytis | SeimoNarys | seen | live XML | gender (optional) |
| M6 | p2b.ad_seimo_nariai | attribute | data_nuo | SeimoNarys | seen | live XML | mandate start |
| M7 | p2b.ad_seimo_nariai | attribute | data_iki | SeimoNarys | seen | notes: "data_iki marks the end of a mandate" | mandate end; "former MP" |
| M8 | p2b.ad_seimo_nariai | attribute | iškėlusi_partija | SeimoNarys | seen | notes | nominating party |
| M9 | p2b.ad_seimo_nariai | attribute | išrinkimo_būdas | SeimoNarys | seen | live XML | list election or constituency, written in the same field |
| M11 | p2b.ad_seimo_nariai | attribute | kadencijų_skaičius | SeimoNarys | seen | notes | terms served |
| M12 | p2b.ad_seimo_nariai | element | Pareigos | SeimoNarys | seen | notes: memberships with dates (`Pareigos`) in the MP record | faction, committee and other memberships |
| M13 | p2b.ad_seimo_nariai | attribute | padalinio_id | Pareigos | assumed | | body key |
| M14 | p2b.ad_seimo_nariai | attribute | padalinio_pavadinimas | Pareigos | assumed | | body name, and its kind (M23) |
| M15 | p2b.ad_seimo_nariai | attribute | pareigos | Pareigos | assumed | | role in the body |
| M16 | p2b.ad_seimo_nariai | attribute | data_nuo | Pareigos | seen | live XML | membership start |
| M17 | p2b.ad_seimo_nariai | attribute | data_iki | Pareigos | seen | live XML | membership end; faction at the time of a vote |
| M18 | p2b.ad_seimo_nariai | element | Kontaktai | SeimoNarys | seen | notes: `<Kontaktai rūšis="El. p." reikšmė="…@lrs.lt"/>` | official contacts |
| M19 | p2b.ad_seimo_nariai | attribute | rūšis | Kontaktai | seen | notes | contact kind |
| M20 | p2b.ad_seimo_nariai | attribute | reikšmė | Kontaktai | seen | notes | contact value |
| M21 | p2b.ad_seimo_nariai | value | rūšis=El. p. | Kontaktai | seen | notes (145 records in Sept 2026) | the official `@lrs.lt` address |
| M22 | p2b.ad_seimo_nariai | behaviour | an MP whose mandate ended is still listed, with data_iki | SeimoNarys | seen | notes | former MPs keep their profile |
| M23 | p2b.ad_seimo_nariai | behaviour | a body's kind can be read from its name (frakcija, komitetas, komisija, grupė) | Pareigos | assumed | design 7.1: lists of bodies are separate services, not confirmed | faction vs committee; the mixed group ("…grupė") is not a faction, so has no position (decided 2026-11-12) |
| M24 | p2b.ad_seimo_nariai | attribute | parlamentinės_grupės_id | Pareigos | seen | live XML | group key |
| M25 | p2b.ad_seimo_nariai | attribute | parlamentinės_grupės_pavadinimas | Pareigos | seen | live XML | group name |
| S1 | p2b.ad_seimo_sesijos | element | SeimoSesija | (document) | assumed | | one row per session |
| S2 | p2b.ad_seimo_sesijos | attribute | sesijos_id | SeimoSesija | seen | live XML | session key |
| S3 | p2b.ad_seimo_sesijos | attribute | pavadinimas | SeimoSesija | assumed | | session name |
| S4 | p2b.ad_seimo_sesijos | attribute | data_nuo | SeimoSesija | assumed | | session start |
| S5 | p2b.ad_seimo_sesijos | attribute | data_iki | SeimoSesija | assumed | | session end; empty while it runs |
| P1 | p2b.ad_seimo_posedziai | element | SeimoPosėdis | (document) | assumed | | one row per sitting |
| P2 | p2b.ad_seimo_posedziai | attribute | posėdžio_id | SeimoPosėdis | seen | live XML | sitting key |
| P3 | p2b.ad_seimo_posedziai | attribute | numeris | SeimoPosėdis | assumed | | sitting number (optional) |
| P4 | p2b.ad_seimo_posedziai | attribute | tipas | SeimoPosėdis | assumed | | morning, evening… (optional) |
| P5 | p2b.ad_seimo_posedziai | attribute | pradžia | SeimoPosėdis | assumed | | start time; refetch window |
| P6 | p2b.ad_seimo_posedziai | attribute | pabaiga | SeimoPosėdis | assumed | | end time (optional) |
| P7 | p2b.ad_seimo_posedziai | behaviour | rows with no pradžia are unstarted sittings | SeimoPosėdis | seen | session 145, rows -502214 onward | skip until they start |
| A1 | p2b.ad_sp_darbotvarke | element | DarbotvarkėsKlausimas | (document) | seen | live XML | agenda item |
| A2 | p2b.ad_sp_darbotvarke | attribute | darbotvarkės_klausimo_id | KlausimoStadija | seen | live XML | agenda stage key |
| A3 | p2b.ad_sp_darbotvarke | attribute | numeris | DarbotvarkėsKlausimas | seen | live XML | agenda item number |
| A4 | p2b.ad_sp_darbotvarke | attribute | pavadinimas | DarbotvarkėsKlausimas | seen | live XML | title may be blank (sitting -502050, stage -97201); otherwise it may end in a bill number and version |
| A5 | p2b.ad_sp_darbotvarke | element | KlausimoStadija | DarbotvarkėsKlausimas | seen | live XML | one row per stage of an item |
| A6 | p2b.ad_sp_darbotvarke | attribute | pavadinimas | KlausimoStadija | seen | live XML | name of the stage |
| A7 | p2b.ad_sp_darbotvarke | attribute | dokumento_nuoroda | KlausimoStadija | seen | live XML | e-seimas link |
| E1 | p2b.ad_seimo_posedzio_eiga_full | parameter | posedzio_id |  | seen | live XML | one request per sitting |
| E2 | p2b.ad_seimo_posedzio_eiga_full | element | balsavimas | (document) | seen | live XML | one row per vote in the proceedings |
| E3 | p2b.ad_seimo_posedzio_eiga_full | element | balsavimas | darbotvarkes-klausimas | seen | live XML | the item's votes |
| E4 | p2b.ad_seimo_posedzio_eiga_full | attribute | kl_stad_id | darbotvarkes-klausimas | seen | live XML | the item key shared with the agenda, when present |
| E5 | p2b.ad_seimo_posedzio_eiga_full | attribute | bals_id | balsavimas | seen | live XML | vote key; the results request |
| E6 | p2b.ad_seimo_posedzio_eiga_full | element | nuo | balsavimas | seen | live XML | vote time |
| E7 | p2b.ad_seimo_posedzio_eiga_full | element | aprasas | balsavimas | seen | live XML | wording and official outcome, including Pritarta and Nepritarta |
| E8 | p2b.ad_seimo_posedzio_eiga_full | element | antraste | balsavimas | seen | live XML | vote numbers or consensus heading |
| E9 | p2b.ad_seimo_posedzio_eiga_full | value | aprasas or antraste contains "bendru sutarimu" | balsavimas | seen | live XML | consensus votes |
| E10 | p2b.ad_seimo_posedzio_eiga_full | attribute | svarst_kl_stad_id | darbotvarkes-klausimas | seen | live XML | discussion key, including votes on a group of items |
| E11 | p2b.ad_seimo_posedzio_eiga_full | element | nr | darbotvarkes-klausimas | seen | live XML | agenda number in proceedings |
| E12 | p2b.ad_seimo_posedzio_eiga_full | element | pavadinimas | darbotvarkes-klausimas | seen | live XML | title for an item absent from the agenda |
| E13 | p2b.ad_seimo_posedzio_eiga_full | element | stadija | darbotvarkes-klausimas | seen | live XML | actual stage of the discussion |
| R0 | p2b.ad_sp_balsavimo_rezultatai | element | BendriBalsavimoRezultatai | SeimoNariųBalsavimas | seen | live XML | group containing the totals |
| R1 | p2b.ad_sp_balsavimo_rezultatai | element | BendriBalsavimoRezultatai | (document) | seen | fetch_vote | totals; its absence means no result |
| R2 | p2b.ad_sp_balsavimo_rezultatai | attribute | balsavimo_id | SeimoNariųBalsavimas | seen | live XML | which vote the result is for |
| R3 | p2b.ad_sp_balsavimo_rezultatai | attribute | už | BendriBalsavimoRezultatai | seen | fetch_vote; notes (68 for in -60034) | total for |
| R4 | p2b.ad_sp_balsavimo_rezultatai | attribute | prieš | BendriBalsavimoRezultatai | seen | fetch_vote; notes (14 against) | total against |
| R5 | p2b.ad_sp_balsavimo_rezultatai | attribute | susilaikė | BendriBalsavimoRezultatai | seen | fetch_vote; notes (23 abstained) | total abstained |
| R6 | p2b.ad_sp_balsavimo_rezultatai | attribute | balsavo | BendriBalsavimoRezultatai | seen | live XML | how many voted |
| R7 | p2b.ad_sp_balsavimo_rezultatai | attribute | viso | BendriBalsavimoRezultatai | seen | live XML | seats (optional) |
| R8 | p2b.ad_sp_balsavimo_rezultatai | attribute | komentaras | BendriBalsavimoRezultatai | seen | live XML | official note shown with the result |
| R9 | p2b.ad_sp_balsavimo_rezultatai | element | IndividualusBalsavimoRezultatas | (document) | seen | live XML | one row per MP |
| R10 | p2b.ad_sp_balsavimo_rezultatai | attribute | asmens_id | IndividualusBalsavimoRezultatas | assumed | | ballot → person; without it the import matches by name |
| R11 | p2b.ad_sp_balsavimo_rezultatai | attribute | vardas | IndividualusBalsavimoRezultatas | seen | fetch_vote | name match |
| R12 | p2b.ad_sp_balsavimo_rezultatai | attribute | pavardė | IndividualusBalsavimoRezultatas | seen | fetch_vote | name match |
| R13 | p2b.ad_sp_balsavimo_rezultatai | attribute | frakcija | IndividualusBalsavimoRezultatas | seen | fetch_vote | faction at the time of the vote; two-thirds rule |
| R14 | p2b.ad_sp_balsavimo_rezultatai | attribute | kaip_balsavo | IndividualusBalsavimoRezultatas | seen | fetch_vote | the MP's choice |
| R15 | p2b.ad_sp_balsavimo_rezultatai | value | kaip_balsavo=Už, Prieš, Susilaikė | IndividualusBalsavimoRezultatas | seen | live XML | for, against, abstained |
| R16 | p2b.ad_sp_balsavimo_rezultatai | value | kaip_balsavo= (empty) | IndividualusBalsavimoRezultatas | seen | fetch_vote counts an empty value as "nebalsavo" | did not vote |
| R17 | p2b.ad_sp_balsavimo_rezultatai | behaviour | every sitting MP has a row, including those who did not vote | IndividualusBalsavimoRezultatas | seen | fetch_vote (empty values); notes (105 of 140 voted) | participation |
| R18 | p2b.ad_sp_balsavimo_rezultatai | behaviour | a consensus vote has no per-MP rows | IndividualusBalsavimoRezultatas | seen | notes | consensus votes count nowhere |
| R19 | p2b.ad_sp_balsavimo_rezultatai | behaviour | per-MP rows usually sum to the totals; when they do not, the official note says so | BendriBalsavimoRezultatai | seen | notes (-60034 summed exactly and carried the note) | design 7.5 check |
| R20 | p2b.ad_sp_balsavimo_rezultatai | value | frakcija=MG | IndividualusBalsavimoRezultatas | seen | fetch_vote only: the September 2026 prototype places "MG" (the mixed group) last; seen nowhere else | nothing: the mixed group is recognised by its membership's body (M23, not a faction), never by this label |
| F1 | (all) | format | dates are yyyy-MM-dd | data_nuo, data_iki | assumed | | dates |
| F2 | (all) | format | times are Vilnius local time, yyyy-MM-dd HH:mm or with seconds | pradžia, pabaiga, balsavimo_laikas, balsavimas/nuo | assumed | | times, stored in UTC |
| F3 | (all) | format | identifiers are integers and may be negative | *_id | seen | notes: vote -60034 | keys (bigint) |
| F4 | (all) | format | responses are well-formed UTF-8 XML without a namespace | | assumed | | parsing |
| F5 | (all) | behaviour | the service sometimes answers without the expected content; asking again helps | | seen | fetch_vote retries up to five times until the totals element appears | malformed answers are retried |
| Q1 | p2b.ad_seimo_nariai | parameter | kadencijos_id | | seen | AGENTS, design ✔ | MPs of term 10 |
| Q2 | p2b.ad_seimo_sesijos | parameter | kadencijos_id | | seen | AGENTS, design ✔ | sessions of a term |
| Q3 | p2b.ad_seimo_posedziai | parameter | sesijos_id | | seen | AGENTS, design ✔ | sittings of a session |
| Q4 | p2b.ad_sp_darbotvarke | parameter | posedzio_id | | seen | AGENTS, design ✔ | agenda of a sitting |
| Q5 | p2b.ad_sp_balsavimo_rezultatai | parameter | balsavimo_id | | seen | AGENTS, design ✔, fetch_vote | results of a vote |
| V4 | (import) | behaviour | stage results are recognised from the wording ("po pateikimo", "po svarstymo", "priimti"; "darbotvarkė" procedural; "pasiūlym" amendment) | aprasas | assumed | design 7.2 asks for rules tested on recorded items | vote kind and stage |

Totals are counted by `RegisterTests.The_register_counts_are_as_reported` and printed at the top of every contract
report.
