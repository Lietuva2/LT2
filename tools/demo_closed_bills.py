"""The stand-in's invented closed bills (design 8.26.5), written by tools/standin_overlay.py into tools/Lt2.SeimasStandIn/Recent.

Local stack and trial only: Recent/ is the container's overlay, never loaded by tests. Thirty invented bills
(XVP-0701..XVP-0731 without XVP-0707) get an adoption vote at a sitting on a Tuesday or Thursday between March and
early November 2026, with per-MP ballots for every MP of the invented parliament sitting that day (tools/invented_seimas.py),
groups voting along their lines; the totals are the ballots'. Three are adopted by consensus (no ballots) and four
rejected (the majority of those who voted not "for"; XVP-0706 with more than half "prieš", the others without a majority).
XVP-0707 is adopted at a sitting nine days before today. The migrator's --demo-appeal gives these bills invented
positions and closes their records (src/Lt2.Migrator/DemoClosedBills.cs holds the same numbers).

The sittings are listed in the overlay's answer for the autumn session (99000102), whatever their date: the import asks
for a closed session's sittings only once, so a stack that imported the spring session before would never see sittings
added to it, while the open session's list is read again on every run. Sessions are shown nowhere. The last of them is
more than two weeks before 2026-11-18, so none shares a day with the recent sittings, which are dated relative to today
(tools/standin_agenda.py).
"""

from __future__ import annotations

import datetime as dt
import random

import invented_seimas as seimas

GENERATED = "Written by tools/standin_overlay.py (fixed seed); do not edit by hand: invented closed bills for the analytics page (design 8.26.5)."

# The invented closed bills: number, title. Titles name real kinds of law; the numbers and everything else are invented.
BILLS = [
    ("XVP-0701", "Vietos savivaldos įstatymo pataisa"),
    ("XVP-0702", "Darbo kodekso pataisa"),
    ("XVP-0703", "Sveikatos sistemos įstatymo pataisa"),
    ("XVP-0704", "Švietimo įstatymo pataisa"),
    ("XVP-0705", "Kelių priežiūros ir plėtros programos finansavimo įstatymo pataisa"),
    ("XVP-0706", "Pridėtinės vertės mokesčio įstatymo pataisa"),
    ("XVP-0708", "Miškų įstatymo pataisa"),
    ("XVP-0709", "Aplinkos apsaugos įstatymo pataisa"),
    ("XVP-0710", "Socialinių paslaugų įstatymo pataisa"),
    ("XVP-0711", "Valstybės tarnybos įstatymo pataisa"),
    ("XVP-0712", "Energetikos įstatymo pataisa"),
    ("XVP-0713", "Atsinaujinančių išteklių energetikos įstatymo pataisa"),
    ("XVP-0714", "Transporto lengvatų įstatymo pataisa"),
    ("XVP-0715", "Mokslo ir studijų įstatymo pataisa"),
    ("XVP-0716", "Teritorijų planavimo įstatymo pataisa"),
    ("XVP-0717", "Statybos įstatymo pataisa"),
    ("XVP-0718", "Alkoholio kontrolės įstatymo pataisa"),
    ("XVP-0719", "Tabako, tabako gaminių ir su jais susijusių gaminių kontrolės įstatymo pataisa"),
    ("XVP-0720", "Gyvūnų gerovės ir apsaugos įstatymo pataisa"),
    ("XVP-0721", "Nekilnojamojo turto mokesčio įstatymo pataisa"),
    ("XVP-0722", "Akcizų įstatymo pataisa"),
    ("XVP-0723", "Valstybinio socialinio draudimo įstatymo pataisa"),
    ("XVP-0724", "Išmokų vaikams įstatymo pataisa"),
    ("XVP-0725", "Visuomenės informavimo įstatymo pataisa"),
    ("XVP-0726", "Asmens duomenų teisinės apsaugos įstatymo pataisa"),
    ("XVP-0727", "Kibernetinio saugumo įstatymo pataisa"),
    ("XVP-0728", "Krašto apsaugos sistemos organizavimo ir karo tarnybos įstatymo pataisa"),
    ("XVP-0729", "Civilinės saugos įstatymo pataisa"),
    ("XVP-0730", "Turizmo įstatymo pataisa"),
    ("XVP-0731", "Kultūros paveldo apsaugos įstatymo pataisa"),
]
CONSENSUS = {"XVP-0705", "XVP-0720", "XVP-0729"}
REJECTED = {"XVP-0706", "XVP-0718", "XVP-0722", "XVP-0726"}
MORE_AGAINST = "XVP-0706"  # rejected with more "prieš" than "už": the statistics page shows "prieš" as the Seimas's majority

# Sittings: date, number, how many of the bills were adopted at it.
SITTINGS = [
    ("2026-03-24", 204, 3), ("2026-04-02", 207, 2), ("2026-04-23", 215, 2), ("2026-05-07", 221, 3),
    ("2026-05-26", 229, 2), ("2026-06-11", 234, 2), ("2026-06-23", 238, 2), ("2026-09-24", 244, 3),
    ("2026-10-06", 247, 2), ("2026-10-15", 250, 2), ("2026-10-27", 253, 3), ("2026-10-29", 254, 2),
    ("2026-11-03", 256, 2),
]

# XVP-0707 (src/Lt2.Migrator/DemoAppeal.cs ClosedBill): adopted at an unscheduled sitting nine days before today, in the
# evening after the day's sittings, so it never takes the place of a recent sitting day.
CLOSED_0707 = ("XVP-0707", "Viešųjų pirkimų įstatymo pataisa")
RELATIVE_0707 = ("{{day:N}} is today plus N days (yyyy-MM-dd), so the local stack has a bill whose adoption vote was nine days ago "
                 "(design 8.26).\n     " + GENERATED)


def accusative(title: str) -> str:
    return title[: -len("pataisa")] + "pataisą" if title.endswith("pataisa") else title


def _adoption(rng: random.Random, day: dt.date, bill: str) -> list:
    """Per-MP rows for an adoption vote: adopted ("for" more than half of those who voted) unless the bill is rejected."""
    rejected = bill in REJECTED
    profile = "rejected-against" if bill == MORE_AGAINST else "rejected-split" if rejected else rng.choice(["majority", "majority", "consensual"])
    for _ in range(200):
        rows = seimas.ballots(rng, day, profile)
        f, a, s = seimas.count(rows)
        adopted = f * 2 > f + a + s
        total = f + a + s
        against = a * 2 > total  # "prieš" on the statistics page; otherwise a rejected bill has "nėra daugumos"
        if adopted != rejected and against == (bill == MORE_AGAINST):
            return rows
    raise AssertionError(f"no ballots fit {bill}")


def files() -> tuple[dict[str, str], list[str]]:
    """The closed bills' files and their sittings' lines for the autumn session's list."""
    rng = random.Random(707)
    order = BILLS[:]
    rng.shuffle(order)
    out: dict[str, str] = {}
    sitting_rows = []
    queue = iter(order)
    header = seimas.HEADER.format(note=GENERATED)
    for s, (date, number, count) in enumerate(SITTINGS, start=1):
        day = dt.date.fromisoformat(date)
        assert day.weekday() in (1, 3), f"{date} is not a Tuesday or Thursday"
        assert day <= dt.date(2026, 11, 3), "before the recent sitting days (tools/standin_agenda.py)"
        sitting_id = 99001200 + s
        evening = rng.random() < 0.3
        start = dt.datetime.combine(day, dt.time(14 if evening else 10, 0))
        end = start + dt.timedelta(hours=2, minutes=rng.randint(30, 59))
        kind = "Vakarinis" if evening else "Rytinis"
        times = f'pradžia="{start:%Y-%m-%d %H:%M}" pabaiga="{end:%Y-%m-%d %H:%M}"'
        sitting_rows.append(f'    <SeimoPosėdis posėdžio_id="{sitting_id}" numeris="{number}" tipas="{kind}" {times}/>')
        items = [f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{99012000 + s * 10}" numeris="1" pavadinimas="Dėl posėdžio darbotvarkės" stadija="" laikas_nuo="{start:%H:%M}"/>']
        proceedings = []
        at = start + dt.timedelta(minutes=5)
        for k in range(1, count + 1):
            bill, title = next(queue)
            item_id = 99012000 + s * 10 + k
            at += dt.timedelta(minutes=rng.randint(25, 50))
            items.append(
                f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_id}" numeris="{k + 1}" pavadinimas="{title}" stadija="Priėmimas" laikas_nuo="{at:%H:%M}">\n'
                f'      <Dokumentas registracijos_numeris="{bill}" nuoroda="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-{bill}"/>\n'
                "    </DarbotvarkėsKlausimas>")
            vote_id = 99100200 + int(bill[-2:])
            held = at + dt.timedelta(minutes=rng.randint(12, 24), seconds=rng.randint(0, 59))
            if bill in CONSENSUS:
                proceedings.append(
                    f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_id}">\n'
                    f'      <Balsavimas balsavimo_id="{vote_id}" balsavimo_laikas="{held:%Y-%m-%d %H:%M:%S}" aprašas="Pritarta bendru sutarimu" balsavimo_tipas="Bendru sutarimu"/>\n'
                    "    </DarbotvarkėsKlausimas>")
                continue
            proceedings.append(
                f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_id}">\n'
                f'      <Balsavimas balsavimo_id="{vote_id}" balsavimo_laikas="{held:%Y-%m-%d %H:%M:%S}" aprašas="Priimti {accusative(title)}" balsavimo_tipas="Atviras"/>\n'
                "    </DarbotvarkėsKlausimas>")
            rows = _adoption(rng, day, bill)
            out[f"p2b.ad_sp_balsavimo_rezultatai@balsavimo_id={vote_id}.xml"] = seimas.result_file(GENERATED, vote_id, f"{held:%Y-%m-%d %H:%M:%S}", rows)
        out[f"p2b.ad_sp_darbotvarke@posedzio_id={sitting_id}.xml"] = (
            header + "<SeimoInformacija>\n"
            f'  <SeimoPosėdis posėdžio_id="{sitting_id}" numeris="{number}" tipas="{kind}" {times}>\n'
            + "\n".join(items) + "\n  </SeimoPosėdis>\n</SeimoInformacija>\n")
        out[f"p2b.ad_sp_klausimo_svarstymo_eiga@posedzio_id={sitting_id}.xml"] = (
            header + "<SeimoInformacija>\n"
            f'  <SeimoPosėdis posėdžio_id="{sitting_id}">\n' + "\n".join(proceedings) + "\n  </SeimoPosėdis>\n</SeimoInformacija>\n")

    # XVP-0707, nine days ago: its per-MP rows keep the base fixture's 14 MPs' choices and the earlier totals.
    bill, title = CLOSED_0707
    header = seimas.HEADER.format(note=RELATIVE_0707)
    times = 'pradžia="{{day:-9}} 17:00" pabaiga="{{day:-9}} 17:25"'
    sitting_rows.append(f'    <SeimoPosėdis posėdžio_id="99001103" numeris="0" tipas="Nenumatytas" {times}/>')
    out["p2b.ad_sp_darbotvarke@posedzio_id=99001103.xml"] = (
        header + "<SeimoInformacija>\n"
        f'  <SeimoPosėdis posėdžio_id="99001103" numeris="0" tipas="Nenumatytas" {times}>\n'
        '    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="99010301" numeris="1" pavadinimas="Dėl posėdžio darbotvarkės" stadija="" laikas_nuo="17:00"/>\n'
        f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="99010302" numeris="2" pavadinimas="{title}" stadija="Priėmimas" laikas_nuo="17:05">\n'
        f'      <Dokumentas registracijos_numeris="{bill}" nuoroda="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-{bill}"/>\n'
        "    </DarbotvarkėsKlausimas>\n  </SeimoPosėdis>\n</SeimoInformacija>\n")
    held = "{{day:-9}} 17:14:05"
    out["p2b.ad_sp_klausimo_svarstymo_eiga@posedzio_id=99001103.xml"] = (
        header + "<SeimoInformacija>\n"
        '  <SeimoPosėdis posėdžio_id="99001103">\n'
        '    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="99010302">\n'
        f'      <Balsavimas balsavimo_id="99100104" balsavimo_laikas="{held}" aprašas="Priimti {accusative(title)}" balsavimo_tipas="Atviras"/>\n'
        "    </DarbotvarkėsKlausimas>\n  </SeimoPosėdis>\n</SeimoInformacija>\n")
    rows = seimas.ballots(random.Random(99100104), dt.date(2026, 11, 9), "majority", fixed=KEPT_0707, totals=(58, 41, 13))
    out["p2b.ad_sp_balsavimo_rezultatai@balsavimo_id=99100104.xml"] = seimas.result_file(RELATIVE_0707, 99100104, held, rows)
    return out, sitting_rows


# The base fixture's 14 MPs on XVP-0707's adoption, as the overlay had them before it had 141 MPs.
KEPT_0707 = {
    99000001: ("RPF", "Už"), 99000002: ("RPF", "Už"), 99000003: ("RPF", "Už"), 99000004: ("RPF", "Susilaikė"),
    99000005: ("PSF", "Prieš"), 99000006: ("PSF", "Prieš"), 99000007: ("RAF", "Už"), 99000008: ("PSF", "Prieš"),
    99000009: ("RAF", "Už"), 99000010: ("RAF", "Prieš"), 99000012: ("RAF", "Už"), 99000013: ("RAF", ""),
    99000014: ("MG", "Už"),
}
