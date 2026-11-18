"""Writes the stand-in's invented closed bills into tools/Lt2.SeimasStandIn/Recent (design 8.26.5).

Local stack and trial only: Recent/ is the container's overlay, never loaded by tests. Thirty invented bills
(XVP-0701..XVP-0731 without XVP-0707) get an adoption vote at a sitting on a Tuesday or Thursday between March and
November 2026, with per-MP ballots for every MP of the invented parliament sitting that day; three are adopted by
consensus (no ballots) and four rejected (the majority of those who voted not "for"). Fixed seed: running it again
writes the same files. The migrator's --demo-appeal gives these bills invented positions and closes their records
(src/Lt2.Migrator/DemoClosedBills.cs holds the same numbers).

The sittings are listed in the overlay's answer for the autumn session (99000102), whatever their date: the import asks
for a closed session's sittings only once, so a stack that imported the spring session before would never see sittings
added to it, while the open session's list is read again on every run. Sessions are shown nowhere.

    python tools/demo_closed_bills.py            # writes the files
    python tools/demo_closed_bills.py --check    # exits 1 when the files differ from what it would write
"""

import datetime as dt
import pathlib
import random
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
RECENT = ROOT / "tools" / "Lt2.SeimasStandIn" / "Recent"

HEADER = (
    '<?xml version="1.0" encoding="UTF-8"?>\n'
    "<!-- LT2-SYNTHETIC: invented by Lietuva 2.0 for the LOCAL stand-in only (tools/Lt2.SeimasStandIn/Recent). NOT a recorded\n"
    "     Seimas response. {note} -->"
)
GENERATED = "Written by tools/demo_closed_bills.py (fixed seed); do not edit by hand: invented closed bills for the analytics page (design 8.26.5)."
RELATIVE = "{{day:N}} is today plus N days (yyyy-MM-dd), so the local agenda page has votes from the last week."

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

# Sittings: date, number, morning or evening, how many of the bills were adopted at it.
SITTINGS = [
    ("2026-03-24", 204, 3), ("2026-04-02", 207, 2), ("2026-04-23", 215, 2), ("2026-05-07", 221, 3),
    ("2026-05-26", 229, 2), ("2026-06-11", 234, 2), ("2026-06-23", 238, 2), ("2026-09-24", 244, 3),
    ("2026-10-06", 247, 2), ("2026-10-15", 250, 2), ("2026-10-27", 253, 3), ("2026-11-05", 255, 2),
    ("2026-11-12", 260, 2),
]

# The invented parliament (tests/Lt2.Parliament.ImportTests/Seimas/Synthetic/README.md): who sits on a day, and with which label.
MPS = [
    (99000001, "Jonas", "Petraitis"), (99000002, "Marius", "Ąžuolaitis"), (99000003, "Eglė", "Beržinytė"),
    (99000004, "Vytautas", "Klevinskas"), (99000005, "Ona", "Jonaitienė"), (99000006, "Laima", "Uosytė"),
    (99000007, "Darius", "Liepaitis"), (99000008, "Asta", "Pušinskienė"), (99000009, "Kęstutis", "Gluosnis"),
    (99000010, "Indrė", "Eglaitė"), (99000011, "Tadas", "Alksninis"), (99000012, "Gintarė", "Šermukšnytė"),
    (99000013, "Aurelija", "Kadagytė"), (99000014, "Rasa", "Ievaitė"),
]
BASE_LABEL = {1: "RPF", 2: "RPF", 3: "RPF", 4: "RPF", 5: "PSF", 6: "PSF", 7: "PSF", 8: "PSF", 9: "RAF", 10: "RAF",
              11: "RAF", 12: "RAF", 13: "RAF", 14: "RPF"}
CHANGE = dt.date(2026, 4, 30)  # the last day of Liepaitis in PSF and Ievaitė in RPF


def label(person: int, day: dt.date) -> str | None:
    n = person - 99000000
    if n == 11 and day > dt.date(2026, 4, 15):
        return None
    if n == 13 and day < dt.date(2026, 4, 21):
        return None
    if day > CHANGE and n == 7:
        return "RAF"
    if day > CHANGE and n == 14:
        return "MG"
    return BASE_LABEL[n]


FOR, AGAINST, ABSTAIN = "Už", "Prieš", "Susilaikė"
# How likely each faction's line is "for": adopted bills, rejected bills.
LEAN_FOR = {"RPF": (0.85, 0.45), "RAF": (0.65, 0.3), "PSF": (0.4, 0.15)}


def ballots(rng: random.Random, day: dt.date, rejected: bool) -> list[tuple[int, str, str, str, str]]:
    lines = {}
    for faction, (adopted_p, rejected_p) in LEAN_FOR.items():
        if rng.random() < 0.08:
            lines[faction] = None  # a free vote
            continue
        p = rejected_p if rejected else adopted_p
        r = rng.random()
        lines[faction] = FOR if r < p else (AGAINST if r < p + (1 - p) * 0.75 else ABSTAIN)
    rows = []
    for person, first, last in MPS:
        lab = label(person, day)
        if lab is None:
            continue
        r = rng.random()
        if r < 0.06:
            choice = ""
        else:
            line = lines.get(lab)
            if line is None:
                choice = rng.choices([FOR, AGAINST, ABSTAIN], [0.45, 0.35, 0.2])[0]
            elif rng.random() < 0.87:
                choice = line
            else:
                choice = rng.choice([c for c in (FOR, AGAINST, ABSTAIN) if c != line])
        rows.append((person, first, last, lab, choice))
    return rows


def totals(rng: random.Random, rejected: bool) -> tuple[int, int, int]:
    while True:
        if rejected:
            f, a, s = rng.randint(38, 60), rng.randint(36, 62), rng.randint(6, 22)
        else:
            f, a, s = rng.randint(62, 96), rng.randint(10, 46), rng.randint(3, 24)
        if (f * 2 > f + a + s) != rejected and f + a + s <= 135:
            return f, a, s


def accusative(title: str) -> str:
    return title[: -len("pataisa")] + "pataisą" if title.endswith("pataisa") else title


def main() -> int:
    rng = random.Random(707)
    order = BILLS[:]
    rng.shuffle(order)
    files: dict[str, str] = {}
    sitting_rows = []
    queue = iter(order)
    for s, (date, number, count) in enumerate(SITTINGS, start=1):
        day = dt.date.fromisoformat(date)
        assert day.weekday() in (1, 3), f"{date} is not a Tuesday or Thursday"
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
            rejected = bill in REJECTED
            f, a, ab = totals(rng, rejected)
            rows = "\n".join(
                f'  <IndividualusBalsavimoRezultatas asmens_id="{p}" vardas="{fn}" pavardė="{ln}" frakcija="{lab}" kaip_balsavo="{c}"/>'
                for p, fn, ln, lab, c in ballots(rng, day, rejected))
            files[f"p2b.ad_sp_balsavimo_rezultatai@balsavimo_id={vote_id}.xml"] = (
                HEADER.format(note=GENERATED) + "<SeimoInformacija>\n"
                f'  <BendriBalsavimoRezultatai balsavimo_id="{vote_id}" balsavimo_laikas="{held:%Y-%m-%d %H:%M:%S}" viso="141" balsavo="{f + a + ab}" už="{f}" prieš="{a}" susilaikė="{ab}" komentaras=""/>\n'
                f"{rows}\n</SeimoInformacija>\n")
        files[f"p2b.ad_sp_darbotvarke@posedzio_id={sitting_id}.xml"] = (
            HEADER.format(note=GENERATED) + "<SeimoInformacija>\n"
            f'  <SeimoPosėdis posėdžio_id="{sitting_id}" numeris="{number}" tipas="{kind}" {times}>\n'
            + "\n".join(items) + "\n  </SeimoPosėdis>\n</SeimoInformacija>\n")
        files[f"p2b.ad_sp_klausimo_svarstymo_eiga@posedzio_id={sitting_id}.xml"] = (
            HEADER.format(note=GENERATED) + "<SeimoInformacija>\n"
            f'  <SeimoPosėdis posėdžio_id="{sitting_id}">\n' + "\n".join(proceedings) + "\n  </SeimoPosėdis>\n</SeimoInformacija>\n")

    # The autumn session as the stand-in's base data and the overlay's relative sittings list it, then the invented ones.
    files["p2b.ad_seimo_posedziai@sesijos_id=99000102.xml"] = (
        HEADER.format(note=RELATIVE + "\n     The sittings 99001201-99001213 (all dates) are " + GENERATED[:-1] + "; they are listed in this open\n"
                      "     session's answer because the import reads a closed session's sittings only once.")
        + "<SeimoInformacija>\n"
        '  <SeimoSesija sesijos_id="99000102" pavadinimas="Rudens sesija" data_nuo="2026-09-10" data_iki="">\n'
        '    <SeimoPosėdis posėdžio_id="99001101" numeris="241" tipas="Rytinis" pradžia="2026-09-15 10:00" pabaiga="2026-09-15 13:15"/>\n'
        '    <SeimoPosėdis posėdžio_id="99001103" numeris="258" tipas="Rytinis" pradžia="{{day:-9}} 10:00" pabaiga="{{day:-9}} 12:40"/>\n'
        '    <SeimoPosėdis posėdžio_id="99001102" numeris="262" tipas="Rytinis" pradžia="{{day:-2}} 10:00" pabaiga="{{day:-2}} 13:05"/>\n'
        + "\n".join(sitting_rows) + "\n  </SeimoSesija>\n</SeimoInformacija>\n")

    check = "--check" in sys.argv[1:]
    differs = []
    for name, text in sorted(files.items()):
        path = RECENT / name
        if check:
            if not path.exists() or path.read_text(encoding="utf-8") != text:
                differs.append(name)
        else:
            path.write_text(text, encoding="utf-8", newline="\n")
    if check:
        print("\n".join(differs) if differs else "up to date")
        return 1 if differs else 0
    print(f"wrote {len(files)} files to {RECENT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
