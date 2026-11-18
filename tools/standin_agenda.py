"""The stand-in's sitting days around today, written by tools/standin_overlay.py into tools/Lt2.SeimasStandIn/Recent.

Local stack and trial only (tests never load the overlay). Shaped after 67 real sitting days (lrs.lt, 2025-09..2026-11):
- the upcoming agenda page (Recent/agenda/darbotvarke.html): the next four sitting days ({{sitting:1}}..{{sitting:4}},
  Tuesdays and Thursdays at least two days ahead, as lrs.lt publishes about 10-12 days ahead), each with a morning and an
  evening sitting and reserve items, about 43 bill rows a day, packages of related bills, the real mix of stages
  (pateikimas 42 %, svarstymas 26 %, priėmimas 23 %, all three at once 8 %) and of title kinds. Its structure is the
  agenda parser's assumption (tests/Lt2.Parliament.ImportTests/Agenda/ASSUMPTIONS.md), unchanged;
- the last four sitting days in the open data ({{past:1}}..{{past:4}}), the same shape, with the votes: a bill considered
  or adopted on the upcoming agenda was presented or considered on one of them, so the open data knows its title, as it
  would know a real one's; a bill to be presented is not in the open data yet, as a real one would not be.
Law names are real Lithuanian laws' (public); the numbers, titles' articles, resolutions, reporters and votes are invented.
Bill numbers XVP-0900..XVP-2089 (and a few XIVP-), apart from the demonstration bills XVP-0412, XVP-0533, XVP-0618 and
XVP-0777 that the end-to-end tests and the demonstration data use.
"""

from __future__ import annotations

import collections
import dataclasses
import datetime as dt
import random

import invented_seimas as seimas

GENERATED = "Written by tools/standin_overlay.py (fixed seed); do not edit by hand (tools/standin_agenda.py)."
PAST_NOTE = ("{{past:N}} is the N-th Tuesday or Thursday before today (yyyy-MM-dd): the last four sitting days, with their\n"
             "     votes, shaped after the real Seimas's. " + GENERATED)

PRESENT, CONSIDER, ADOPT, ALL3, CONS_ADOPT = (
    "Pateikimas", "Svarstymas", "Priėmimas", "Pateikimas, svarstymas, priėmimas", "Svarstymas, priėmimas")
STAGE_MIX = {PRESENT: 42, CONSIDER: 26, ADOPT: 23, ALL3: 8, CONS_ADOPT: 1.5}
# Package sizes over 67 real days; "big" is 9 and more.
PACKAGES = {2: 175, 3: 118, 4: 55, 5: 30, 6: 12, 7: 13, 8: 4, 9: 22}
PACKAGE_SHARE = 0.25  # of the items that are not a resolution in all three stages
DAYS = 4

# Real Lithuanian laws and codes (their names are public), by area: a package takes its bills from one area.
FAMILIES = {
    "mokesčiai": ["Gyventojų pajamų mokesčio įstatymo", "Pelno mokesčio įstatymo", "Pridėtinės vertės mokesčio įstatymo",
                  "Akcizų įstatymo", "Mokesčių administravimo įstatymo", "Nekilnojamojo turto mokesčio įstatymo",
                  "Žemės mokesčio įstatymo", "Aplinkos teršimo mokesčio įstatymo", "Rinkliavų įstatymo"],
    "socialinė apsauga": ["Socialinių paslaugų įstatymo", "Valstybinio socialinio draudimo įstatymo", "Išmokų vaikams įstatymo",
                          "Socialinės paramos mokiniams įstatymo", "Piniginės socialinės paramos nepasiturintiems gyventojams įstatymo",
                          "Šalpos pensijų įstatymo", "Socialinio draudimo pensijų įstatymo", "Užimtumo įstatymo",
                          "Nedarbo socialinio draudimo įstatymo", "Ligos ir motinystės socialinio draudimo įstatymo", "Darbo kodekso"],
    "sveikata": ["Sveikatos sistemos įstatymo", "Sveikatos draudimo įstatymo", "Farmacijos įstatymo",
                 "Pacientų teisių ir žalos sveikatai atlyginimo įstatymo", "Sveikatos priežiūros įstaigų įstatymo",
                 "Psichikos sveikatos priežiūros įstatymo", "Visuomenės sveikatos priežiūros įstatymo", "Alkoholio kontrolės įstatymo"],
    "švietimas": ["Švietimo įstatymo", "Mokslo ir studijų įstatymo", "Profesinio mokymo įstatymo", "Sporto įstatymo",
                  "Jaunimo politikos pagrindų įstatymo", "Neformaliojo suaugusiųjų švietimo ir tęstinio mokymosi įstatymo"],
    "energetika": ["Energetikos įstatymo", "Elektros energetikos įstatymo", "Atsinaujinančių išteklių energetikos įstatymo",
                   "Gamtinių dujų įstatymo", "Šilumos ūkio įstatymo", "Energijos išteklių rinkos įstatymo"],
    "aplinka": ["Aplinkos apsaugos įstatymo", "Miškų įstatymo", "Atliekų tvarkymo įstatymo", "Saugomų teritorijų įstatymo",
                "Vandens įstatymo", "Aplinkos oro apsaugos įstatymo", "Laukinės gyvūnijos įstatymo", "Klimato kaitos valdymo įstatymo"],
    "transportas": ["Kelių įstatymo", "Saugaus eismo automobilių keliais įstatymo", "Kelių transporto kodekso",
                    "Geležinkelių transporto kodekso", "Transporto lengvatų įstatymo", "Aviacijos įstatymo", "Prekybinės laivybos įstatymo"],
    "teisingumas": ["Baudžiamojo kodekso", "Baudžiamojo proceso kodekso", "Administracinių nusižengimų kodekso", "Civilinio kodekso",
                    "Civilinio proceso kodekso", "Bausmių vykdymo kodekso", "Teismų įstatymo", "Advokatūros įstatymo",
                    "Notariato įstatymo", "Antstolių įstatymo", "Prokuratūros įstatymo"],
    "valdymas": ["Vietos savivaldos įstatymo", "Valstybės tarnybos įstatymo", "Viešojo administravimo įstatymo", "Rinkimų kodekso",
                 "Vyriausybės įstatymo", "Viešųjų ir privačių interesų derinimo įstatymo", "Lobistinės veiklos įstatymo",
                 "Korupcijos prevencijos įstatymo", "Teritorijų administracinių vienetų ir jų ribų įstatymo"],
    "gynyba": ["Krašto apsaugos sistemos organizavimo ir karo tarnybos įstatymo", "Karo prievolės įstatymo", "Civilinės saugos įstatymo",
               "Mobilizacijos ir priimančiosios šalies paramos įstatymo", "Valstybės sienos ir jos apsaugos įstatymo",
               "Ginklų ir šaudmenų kontrolės įstatymo"],
    "ekonomika": ["Viešųjų pirkimų įstatymo", "Pirkimų, atliekamų perkančiųjų subjektų, įstatymo", "Akcinių bendrovių įstatymo",
                  "Juridinių asmenų nemokumo įstatymo", "Finansinių priemonių rinkų įstatymo", "Bankų įstatymo", "Kredito unijų įstatymo",
                  "Vartojimo kredito įstatymo", "Konkurencijos įstatymo", "Vartotojų teisių apsaugos įstatymo"],
    "žemės ūkis": ["Žemės įstatymo", "Žemės ūkio, maisto ūkio ir kaimo plėtros įstatymo", "Ūkininko ūkio įstatymo",
                   "Žemės ūkio paskirties žemės įsigijimo įstatymo", "Veterinarijos įstatymo", "Gyvūnų gerovės ir apsaugos įstatymo",
                   "Žuvininkystės įstatymo"],
    "kultūra": ["Kultūros paveldo apsaugos įstatymo", "Visuomenės informavimo įstatymo", "Muziejų įstatymo", "Bibliotekų įstatymo",
                "Kino įstatymo", "Kultūros centrų įstatymo"],
    "statyba": ["Statybos įstatymo", "Teritorijų planavimo įstatymo", "Daugiabučių namų savininkų bendrijų įstatymo",
                "Paramos būstui įsigyti ar išsinuomoti įstatymo", "Nekilnojamojo turto registro įstatymo", "Geodezijos ir kartografijos įstatymo"],
    "skaitmena": ["Elektroninių ryšių įstatymo", "Kibernetinio saugumo įstatymo", "Asmens duomenų teisinės apsaugos įstatymo",
                  "Valstybės informacinių išteklių valdymo įstatymo"],
    "migracija": ["Užsieniečių teisinės padėties įstatymo", "Pilietybės įstatymo", "Gyvenamosios vietos deklaravimo įstatymo"],
}
CODES = [law for laws in FAMILIES.values() for law in laws if law.endswith("kodekso")]
LAWS = [law for laws in FAMILIES.values() for law in laws if not law.endswith("kodekso")]
INSTITUTIONS = ["Valstybės kontrolės", "Lietuvos banko", "Seimo kontrolierių įstaigos", "Lygių galimybių kontrolieriaus tarnybos",
                "Vaiko teisių apsaugos kontrolieriaus įstaigos", "Lietuvos radijo ir televizijos komisijos", "Vyriausiosios rinkimų komisijos",
                "Valstybinės energetikos reguliavimo tarybos", "Konkurencijos tarybos", "Žurnalistų etikos inspektoriaus tarnybos",
                "Lietuvos mokslo tarybos", "Valstybinės lietuvių kalbos komisijos"]
YEARS_OF = ["Vietos bendruomenių", "Upių", "Tautodailės", "Jaunimo savanorystės", "Lietuvos girių", "Kaimo amatų"]
POLICIES = ["Dėl Valstybinės energetikos strategijos įgyvendinimo", "Dėl Nacionalinio saugumo strategijos įgyvendinimo",
            "Dėl Tautinių mažumų politikos gairių", "Dėl Regioninės politikos baltosios knygos", "Dėl ilgalaikės demografijos politikos krypčių",
            "Dėl Valstybės ilgalaikės raidos strategijos įgyvendinimo", "Dėl Seimo laikinosios tyrimo komisijos sudarymo",
            "Dėl atmintinų dienų sąrašo papildymo", "Dėl Seimo narių grupės sudarymo", "Dėl Seimo 2027 metų pavasario sesijos darbų programos",
            "Dėl Seimo delegacijos tarptautinėje parlamentinėje organizacijoje sudėties pakeitimo", "Dėl Valstybinės visuomenės sveikatos stiprinimo programos"]
COUNTRIES = ["Islandijos", "Kanados", "Japonijos", "Korėjos Respublikos", "Australijos", "Čilės Respublikos", "Singapūro Respublikos",
             "Norvegijos Karalystės", "Moldovos Respublikos", "Urugvajaus Rytų Respublikos"]
AGREEMENTS = ["sutarties dėl pajamų dvigubo apmokestinimo išvengimo", "susitarimo dėl socialinės apsaugos",
              "susitarimo dėl bendradarbiavimo gynybos srityje", "susitarimo dėl oro susisiekimo",
              "sutarties dėl savitarpio teisinės pagalbos baudžiamosiose bylose"]
BUDGET = [
    "2027 metų valstybės biudžeto ir savivaldybių biudžetų finansinių rodiklių patvirtinimo įstatymo projektas",
    "Valstybinio socialinio draudimo fondo biudžeto 2027 metų rodiklių patvirtinimo įstatymo projektas",
    "Privalomojo sveikatos draudimo fondo biudžeto 2027 metų rodiklių patvirtinimo įstatymo projektas",
]
# Kind of a single bill that is not a resolution in all three stages; the shape over all rows: law 69 %, resolution 14 %,
# code 10 %, new wording 2 %, ratification 2 %, other 3 %.
SINGLE_KINDS = {"įstatymas": 60, "nutarimas": 11, "kodeksas": 14, "nauja redakcija": 4, "ratifikavimas": 4, "kita": 5}
STAGE_WORDS = ("pateikim", "svarstym", "priėmim")


@dataclasses.dataclass
class Unit:
    """One agenda item: a bill, or a package of related bills presented, considered and adopted together."""
    bills: list[tuple[str, str]]  # (number, title)
    kind: str
    side: str  # whose bill: majority, consensual or opposition (how the groups vote on it)
    reporters: list[str]
    committee: str
    known: bool = False  # in the base fixture already (the demonstration bills)


@dataclasses.dataclass
class Row:
    unit: Unit
    stage: str
    reserve: bool = False
    must_pass: bool = True  # an earlier stage the upcoming agenda needs: its vote passed


class Titles:
    def __init__(self, rng: random.Random):
        self.rng = rng
        self.used: set[str] = set()
        self.subjects = ([f"Dėl {i} 2025 metų veiklos ataskaitos" for i in INSTITUTIONS]
                         + [f"Dėl {name[:-len('komitetas')]}komiteto sudėties pakeitimo" for _, name, _ in seimas.COMMITTEES]
                         + [f"Dėl 2027 metų paskelbimo {y} metais" for y in YEARS_OF] + POLICIES)
        rng.shuffle(self.subjects)
        self.treaties = [(c, a) for c in COUNTRIES for a in AGREEMENTS]
        rng.shuffle(self.treaties)

    def _articles(self) -> str:
        r = self.rng.random()
        if r < 0.2:
            return ""
        numbers = sorted(self.rng.sample(range(2, 80), 1 if r < 0.65 else 2 if r < 0.9 else 3))
        if len(numbers) == 1:
            return f"{numbers[0]} straipsnio "
        return ", ".join(map(str, numbers[:-1])) + f" ir {numbers[-1]} straipsnių "

    def amendment(self, law: str) -> str:
        for _ in range(50):
            title = f"{law} {self._articles()}pakeitimo įstatymo projektas"
            if title not in self.used:
                return self._take(title)
        raise AssertionError(law)

    def of_kind(self, kind: str) -> str:
        if kind == "įstatymas":
            return self.amendment(self.rng.choice(LAWS))
        if kind == "kodeksas":
            return self.amendment(self.rng.choice(CODES))
        if kind == "nutarimas":
            return self._take(f"Seimo nutarimo „{self.subjects.pop()}“ projektas")
        if kind == "nauja redakcija":
            return self._take(f"{self.rng.choice([l for l in LAWS if f'{l} pakeitimo įstatymo projektas (nauja redakcija)' not in self.used])} pakeitimo įstatymo projektas (nauja redakcija)")
        if kind == "ratifikavimas":
            country, agreement = self.treaties.pop()
            return self._take(f"Lietuvos Respublikos ir {country} {agreement} ratifikavimo įstatymo projektas")
        if kind == "kita":
            n = self.rng.choice([n for n in range(10, 240) if f"Seimo statuto {n} straipsnio pakeitimo statuto projektas" not in self.used])
            return self._take(f"Seimo statuto {n} straipsnio pakeitimo statuto projektas")
        raise ValueError(kind)

    def _take(self, title: str) -> str:
        assert title not in self.used, title
        assert not any(w in title.lower() for w in STAGE_WORDS), f"a stage word in a title: {title}"
        self.used.add(title)
        return title


class Numbers:
    """Bill numbers: a package's are consecutive (registered together); older bills have lower numbers."""

    def __init__(self, rng: random.Random):
        self.rng = rng
        self.used: set[int] = set()
        # The first upcoming day takes none of the last term's numbers: XIVP- sorts before XVP-, and the front page shows
        # the first eight planned bills by number, the demonstration bills first.
        self.last_term = True

    def take(self, count: int, new: bool) -> list[str]:
        if not new and self.last_term and self.rng.random() < 0.03:
            return [f"XIVP-{self.rng.randint(3100, 3999):04d}" for _ in range(count)]
        low, high = (1700, 2089) if new else (900, 1699)
        for _ in range(10_000):
            start = self.rng.randint(low, high - count)
            span = set(range(start, start + count))
            if not span & self.used:
                self.used |= span
                return [f"XVP-{n:04d}" for n in sorted(span)]
        raise AssertionError("bill numbers ran out")


class World:
    def __init__(self) -> None:
        self.rng = random.Random(4302)
        self.titles = Titles(self.rng)
        self.numbers = Numbers(self.rng)
        today = dt.date(2026, 11, 18)
        self.seated = [m for m in seimas.PARLIAMENT if m.seated(today)]

    def reporters(self) -> list[str]:
        r = self.rng.random()
        n = 1 if r < 0.22 else self.rng.randint(2, 3) if r < 0.82 else self.rng.randint(4, 10) if r < 0.95 else self.rng.randint(11, 25)
        return [f"{m.first} {m.last}" for m in self.rng.sample(self.seated, n)]

    def unit(self, stage: str, new: bool) -> Unit:
        rng = self.rng
        side = rng.choices(["majority", "consensual", "opposition"], [70, 18, 12])[0]
        committee = rng.choice(seimas.COMMITTEES)[1]
        if stage == ALL3:
            kind = "nutarimas"
            bills = [(self.numbers.take(1, True)[0], self.titles.of_kind(kind))]
            return Unit(bills, kind, "consensual", self.reporters(), committee)
        if rng.random() < PACKAGE_SHARE:
            size = rng.choices(list(PACKAGES), list(PACKAGES.values()))[0]
            if size == 9:
                size = rng.randint(9, 16)
            family = rng.choice([f for f, laws in FAMILIES.items() if len(laws) >= min(size, 6)])
            laws = FAMILIES[family][:]
            rng.shuffle(laws)
            if size > len(laws):  # a large package reaches into a neighbouring area
                laws += [l for l in LAWS + CODES if l not in laws][: size - len(laws)]
            numbers = self.numbers.take(size, new)
            bills = list(zip(numbers, [self.titles.amendment(law) for law in laws[:size]]))
            return Unit(bills, "paketas", side, self.reporters(), committee_of(bills[0][1], committee))
        kind = rng.choices(list(SINGLE_KINDS), list(SINGLE_KINDS.values()))[0]
        bills = [(self.numbers.take(1, new)[0], self.titles.of_kind(kind))]
        return Unit(bills, kind, "consensual" if kind == "ratifikavimas" else side, self.reporters(), committee_of(bills[0][1], committee))


# The committee that leads a bill's consideration, by the law's area.
FAMILY_COMMITTEES = {
    "mokesčiai": "Biudžeto ir finansų komitetas", "socialinė apsauga": "Socialinių reikalų ir darbo komitetas",
    "sveikata": "Sveikatos reikalų komitetas", "švietimas": "Švietimo ir mokslo komitetas", "energetika": "Ekonomikos komitetas",
    "aplinka": "Aplinkos apsaugos komitetas", "transportas": "Ekonomikos komitetas", "teisingumas": "Teisės ir teisėtvarkos komitetas",
    "valdymas": "Valstybės valdymo ir savivaldybių komitetas", "gynyba": "Nacionalinio saugumo ir gynybos komitetas",
    "ekonomika": "Ekonomikos komitetas", "žemės ūkis": "Kaimo reikalų komitetas", "kultūra": "Kultūros komitetas",
    "statyba": "Aplinkos apsaugos komitetas", "skaitmena": "Valstybės valdymo ir savivaldybių komitetas",
    "migracija": "Žmogaus teisių komitetas",
}


def committee_of(title: str, otherwise: str) -> str:
    if "ratifikavimo" in title:
        return "Užsienio reikalų komitetas"
    if title.startswith("Seimo statuto"):
        return "Teisės ir teisėtvarkos komitetas"
    for family, laws in FAMILIES.items():
        if any(title.startswith(law + " ") for law in laws):
            return FAMILY_COMMITTEES[family]
    return otherwise


def _stage(rng: random.Random, weights: dict[str, float]) -> str:
    return rng.choices(list(weights), list(weights.values()))[0]


def _target(rng: random.Random) -> int:
    # Real days: median 43 bill rows, p25 28, p75 57.
    return max(24, min(64, round(rng.gauss(43, 13))))


def plan() -> tuple[World, list[list[Row]], list[list[Row]]]:
    """The upcoming days (1..4) and the past days (index 0 = {{past:1}}, the most recent), each a list of rows."""
    w = World()
    rng = w.rng
    special = {
        "XVP-0412": Unit([("XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa")], "įstatymas", "majority", ["Jonas Petraitis"], "Biudžeto ir finansų komitetas", known=True),
        "XVP-0533": Unit([("XVP-0533", "Vietinių rinkliavų įstatymo pataisa")], "įstatymas", "majority", ["Ona Jonaitienė"], "Valstybės valdymo ir savivaldybių komitetas"),
        "XVP-0618": Unit([("XVP-0618", "Atliekų tvarkymo įstatymo pataisa")], "įstatymas", "majority", ["Laima Uosytė"], "Aplinkos apsaugos komitetas", known=True),
        "XVP-0777": Unit([("XVP-0777", "Vietinių rinkliavų įstatymo 11 straipsnio pakeitimo įstatymo projektas")], "įstatymas", "opposition", ["Kęstutis Gluosnis"], "Valstybės valdymo ir savivaldybių komitetas"),
    }
    w.titles.used.add("Vietinių rinkliavų įstatymo 11 straipsnio pakeitimo įstatymo projektas")

    upcoming: list[list[Row]] = []
    needs: list[tuple[Unit, str]] = []  # (unit, the stage it had on a past day)
    for d in range(DAYS):
        target = _target(rng)
        bias = {s: v * rng.uniform(0.6, 1.5) for s, v in STAGE_MIX.items()}
        rows: list[Row] = []
        w.numbers.last_term = d != 0
        if d == 0:
            # The demonstration bills first (the front page shows the first eight planned bills, by number).
            rows += [Row(special["XVP-0412"], ADOPT), Row(special["XVP-0533"], ADOPT), Row(special["XVP-0618"], ADOPT), Row(special["XVP-0777"], PRESENT)]
            needs.append((special["XVP-0533"], CONSIDER))
        if d == 1:
            budget = Unit(list(zip(w.numbers.take(3, False), [w.titles._take(t) for t in BUDGET])), "kita", "majority", w.reporters(), "Biudžeto ir finansų komitetas")
            rows.append(Row(budget, CONSIDER))
            needs.append((budget, PRESENT))
        while sum(len(r.unit.bills) for r in rows) < target:
            stage = _stage(rng, bias)
            unit = w.unit(stage, new=stage in (PRESENT, ALL3))
            rows.append(Row(unit, stage))
            if stage in (CONSIDER, CONS_ADOPT):
                needs.append((unit, PRESENT))
            elif stage == ADOPT:
                needs.append((unit, CONSIDER))
        upcoming.append(rows)

    # The past days: first the earlier stages the upcoming agenda needs (spread evenly),
    # then other items, by the stage mix still missing.
    past: list[list[Row]] = [[] for _ in range(DAYS)]
    targets = [_target(rng) for _ in range(DAYS)]
    past[0].append(Row(special["XVP-0533"], CONSIDER))
    for unit, stage in needs:
        if unit is special["XVP-0533"]:
            continue
        # The day with the fewest rows so far.
        day = min(range(DAYS), key=lambda d: (sum(len(r.unit.bills) for r in past[d]), rng.random()))
        past[day].append(Row(unit, stage))
    total = sum(targets)
    for d in range(DAYS):
        bias = {s: v * rng.uniform(0.6, 1.5) for s, v in STAGE_MIX.items()}
        while sum(len(r.unit.bills) for r in past[d]) < targets[d]:
            have = collections.Counter()
            for day in past:
                for r in day:
                    have[r.stage] += len(r.unit.bills)
            missing = {s: max(0.5, STAGE_MIX[s] / sum(STAGE_MIX.values()) * total - have[s]) * bias[s] for s in STAGE_MIX}
            stage = _stage(rng, missing)
            past[d].append(Row(w.unit(stage, new=stage in (PRESENT, ALL3)), stage, must_pass=False))
        rng.shuffle(past[d])
        # XVP-0533 stays the first item of the most recent morning (its agenda item and vote keep their identifiers).
        if d == 0:
            past[0].sort(key=lambda r: r.unit is not special["XVP-0533"])
    for rows in upcoming:
        first = rows[:4] if rows[0].unit is special["XVP-0412"] else []
        rest = rows[len(first):]
        rng.shuffle(rest)
        rows[:] = first + rest
    for rows in upcoming + past:
        # Reserve items (median two a day): resolutions in all three stages first, at the end of the day.
        resolutions = [r for r in rows if r.stage == ALL3]
        for r in resolutions[: rng.choice([1, 2, 2, 3])]:
            r.reserve = True
        rows.sort(key=lambda r: r.reserve)
    return w, upcoming, past


# ---------------------------------------------------------------------------------------------------------------------
# A day's two sittings: (morning rows, evening rows, reserve rows)

def split(rows: list[Row], rng: random.Random) -> tuple[list[Row], list[Row], list[Row]]:
    main = [r for r in rows if not r.reserve]
    reserve = [r for r in rows if r.reserve]
    bills = sum(len(r.unit.bills) for r in main)
    morning, count = [], 0
    share = rng.uniform(0.5, 0.6)
    for r in main:
        if morning and count >= bills * share:
            break
        morning.append(r)
        count += len(r.unit.bills)
    evening = main[len(morning):]
    return morning, evening, reserve


def slot(row: Row) -> int:
    size = len(row.unit.bills)
    if size > 1:
        return min(25, 8 + 2 * size)
    return 15 if row.stage == PRESENT else 5


def wording(stage: str) -> str:
    return {PRESENT: "Pritarti po pateikimo", CONSIDER: "Pritarti po svarstymo"}.get(stage, "Priimti")



def page(upcoming: list[list[Row]], w: World) -> str:
    rng = random.Random(1717)
    out = [
        "<!doctype html>",
        "<!-- LT2-SYNTHETIC (local overlay): an INVENTED upcoming-agenda page for the local stand-in, written by\n"
        "     tools/standin_overlay.py (fixed seed; do not edit by hand). Its structure is the parser's assumption\n"
        "     (tests/Lt2.Parliament.ImportTests/Agenda/ASSUMPTIONS.md), not a copy of a real lrs.lt page; its counts are shaped\n"
        "     after the real Seimas's. {{sitting:N}} is the N-th Tuesday or Thursday at least two days after today, written as\n"
        "     lrs.lt writes dates (assumption A1). Law names are real laws'; numbers, articles, resolutions and reporters invented. -->",
        '<html lang="lt">',
        '<head><meta charset="utf-8"><title>Seimo plenarinių posėdžių darbotvarkių projektai (išgalvota)</title></head>',
        "<body>",
        "<h1>Seimo plenarinių posėdžių darbotvarkių projektai</h1>",
        "<p>Išgalvotas puslapis vietiniam bandymui: posėdžių darbotvarkių projektai artimiausioms dviem savaitėms.</p>",
    ]
    for d, rows in enumerate(upcoming, start=1):
        morning, evening, reserve = split(rows, rng)
        for kind, sitting_rows, start in (("rytinis", morning, (10, 0)), ("vakarinis", evening, (14, 0))):
            out.append(f"<h2>{{{{sitting:{d}}}}} {kind} posėdis</h2>")
            out.append("<table>")
            out.append("<tr><th>Nr.</th><th>Laikas</th><th>Klausimas</th><th>Stadija</th><th>Pranešėjai</th></tr>")
            at = dt.datetime(2000, 1, 1, *start)
            n = 1
            out.append(f"<tr><td>{n}.</td><td>{at:%H.%M}</td><td>Posėdžio darbotvarkės tvirtinimas</td><td></td><td></td></tr>")
            at += dt.timedelta(minutes=2)
            n += 1
            since_vote = 0
            items = sitting_rows + (reserve if kind == "vakarinis" else [])
            for i, row in enumerate(items):
                if row.reserve and (i == 0 or not items[i - 1].reserve):
                    out.append(f"<tr><td>{n}.</td><td>{at:%H.%M}</td><td>BALSAVIMAS DĖL PROJEKTŲ</td><td></td><td></td></tr>")
                    n += 1
                    out.append('<tr><td colspan="5">Rezerviniai klausimai</td></tr>')
                out.extend(_page_rows(row, n, at, w, rng, reserve_number=sum(1 for r in items[:i] if r.reserve) + 1 if row.reserve else None))
                if not row.reserve:
                    n += len(row.unit.bills) if len(row.unit.bills) > 1 else 1
                    at += dt.timedelta(minutes=slot(row))
                    since_vote += 1
                    if since_vote >= rng.randint(8, 12) and i + 1 < len(sitting_rows):
                        out.append(f"<tr><td>{n}.</td><td>{at:%H.%M}</td><td>BALSAVIMAS DĖL PROJEKTŲ</td><td></td><td></td></tr>")
                        n += 1
                        at += dt.timedelta(minutes=5)
                        since_vote = 0
            if not any(r.reserve for r in items):
                out.append(f"<tr><td>{n}.</td><td>{at:%H.%M}</td><td>BALSAVIMAS DĖL PROJEKTŲ</td><td></td><td></td></tr>")
                n += 1
                at += dt.timedelta(minutes=5)
            if kind == "rytinis":
                out.append(f"<tr><td>{n}.</td><td>{at:%H.%M}</td><td>Seimo narių pareiškimai</td><td></td><td></td></tr>")
            out.append("</table>")
    out += ["</body>", "</html>", ""]
    return "\n".join(out)


def _page_rows(row: Row, n: int, at: dt.datetime, w: World, rng: random.Random, reserve_number: int | None) -> list[str]:
    lines = []
    size = len(row.unit.bills)
    reporters = row.unit.reporters
    who = (f"Seimo narių grupė ({len(reporters)})" if len(reporters) > 10 else ", ".join(reporters))
    if row.stage in (CONSIDER, ADOPT, CONS_ADOPT):
        who += f"; {row.unit.committee}"
    for j, (number, title) in enumerate(row.unit.bills, start=1):
        if reserve_number is not None:
            label = f"r - {reserve_number}."
            time = ""
        elif size > 1:
            label = f"{n} - {n + size - 1}. {j}."
            time = f"{at:%H.%M}" if j == 1 else ""
        else:
            label = f"{n}."
            time = f"{at:%H.%M}"
        shown = number
        if row.stage in (CONSIDER, ADOPT, CONS_ADOPT) and rng.random() < 0.4:
            shown += f"({rng.choice([2, 2, 3])})"
        link = f'<a href="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-{number}">{shown}</a>'
        lines.append(f"<tr><td>{label}</td><td>{time}</td><td>{link} {seimas.esc(title)}</td><td>{row.stage}</td><td>{seimas.esc(who) if j == 1 else ''}</td></tr>")
    return lines


# ---------------------------------------------------------------------------------------------------------------------
# The past days in the open data

# The base fixture's 14 MPs on XVP-0533's consideration (vote 99100103), as the overlay had them before it had 141 MPs.
KEPT_0533 = {
    99000001: ("RPF", "Prieš"), 99000002: ("RPF", "Už"), 99000003: ("RPF", "Už"), 99000004: ("RPF", "Už"),
    99000005: ("PSF", "Už"), 99000006: ("PSF", "Susilaikė"), 99000007: ("RAF", "Prieš"), 99000008: ("PSF", "Už"),
    99000009: ("RAF", "Prieš"), 99000010: ("RAF", ""), 99000012: ("RAF", "Prieš"), 99000013: ("RAF", "Už"),
    99000014: ("MG", "Už"),
}
CONSENSUS_SHARE = {PRESENT: 0.25, CONSIDER: 0.2}


def sitting_id(k: int, evening: bool) -> int:
    return 99001102 if (k, evening) == (1, False) else 99001300 + k * 10 + (1 if evening else 0)


def past_files(past: list[list[Row]]) -> tuple[dict[str, str], list[str], dict]:
    rng = random.Random(1102)
    files: dict[str, str] = {}
    sittings: list[str] = []
    header = seimas.HEADER.format(note=PAST_NOTE)
    stats = {"votes": 0, "recorded": 0, "consensus": 0, "other": 0}
    vote_id = 99103000
    number = 260
    a_day = dt.date(2026, 11, 17)  # who sits: the same on every recent day
    for k in range(DAYS, 0, -1):  # oldest first, so the identifiers and numbers run in time order
        morning, evening, reserve = split(past[k - 1], rng)
        for is_evening, rows in ((False, morning), (True, evening + reserve)):
            sid = sitting_id(k, is_evening)
            number += 1
            token = f"{{{{past:{k}}}}}"
            start = dt.datetime(2000, 1, 1, 14 if is_evening else 10, 0)
            items, proceedings = [], []
            item_base = 99010200 if sid == 99001102 else 99130000 + k * 1000 + (100 if is_evening else 0)
            n = 1
            items.append(f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_base + n}" numeris="1" pavadinimas="Dėl posėdžio darbotvarkės" stadija="" laikas_nuo="{start:%H:%M}"/>')
            pending: list[tuple[int, str, Row, str]] = []  # (item id, bill, row, title) waiting for the voting block
            since_vote = 0

            def recorded(item_id: int, vid: int, held: str, text: str, ballots: list) -> None:
                proceedings.append(
                    f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_id}">\n'
                    f'      <Balsavimas balsavimo_id="{vid}" balsavimo_laikas="{held}" aprašas="{text}" balsavimo_tipas="Atviras"/>\n'
                    "    </DarbotvarkėsKlausimas>")
                files[f"p2b.ad_sp_balsavimo_rezultatai@balsavimo_id={vid}.xml"] = seimas.result_file(PAST_NOTE, vid, held, ballots)

            # The sitting's agenda is approved by a procedural vote.
            vote_id += 1
            stats["other"] += 1
            recorded(item_base + 1, vote_id, f"{token} {start + dt.timedelta(minutes=1, seconds=rng.randint(0, 59)):%H:%M:%S}",
                     "Dėl posėdžio darbotvarkės", seimas.ballots(rng, a_day, "procedural"))
            at = start + dt.timedelta(minutes=2)

            def vote_block(when: dt.datetime) -> dt.datetime:
                nonlocal vote_id
                t = when
                for item_id, bill, row, title in pending:
                    if row.stage == CONSIDER and bill != "XVP-0533" and rng.random() < 0.15:
                        # A member's proposal voted on before the consideration's result (mostly not adopted).
                        t += dt.timedelta(seconds=rng.randint(40, 95))
                        vote_id += 1
                        stats["other"] += 1
                        recorded(item_id, vote_id, f"{token} {t:%H:%M:%S}", "Pritarti Seimo nario pasiūlymui", seimas.ballots(rng, a_day, "opposition"))
                    t += dt.timedelta(seconds=rng.randint(40, 95))
                    held = f"{token} {t:%H:%M:%S}"
                    stats["votes"] += 1
                    if bill == "XVP-0533":
                        vid = 99100103
                    else:
                        vote_id += 1
                        vid = vote_id
                    consensus = bill != "XVP-0533" and rng.random() < CONSENSUS_SHARE.get(row.stage, 0)
                    if consensus:
                        stats["consensus"] += 1
                        proceedings.append(
                            f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_id}">\n'
                            f'      <Balsavimas balsavimo_id="{vid}" balsavimo_laikas="{held}" aprašas="Pritarta bendru sutarimu" balsavimo_tipas="Bendru sutarimu"/>\n'
                            "    </DarbotvarkėsKlausimas>")
                        continue
                    stats["recorded"] += 1
                    if bill == "XVP-0533":
                        rows_ = seimas.ballots(random.Random(99100103), a_day, "majority", fixed=KEPT_0533, totals=(69, 31, 18))
                    else:
                        rows_ = _passing(rng, a_day, row)
                    recorded(item_id, vid, held, wording(row.stage), rows_)
                pending.clear()
                return t + dt.timedelta(minutes=1)

            for i, row in enumerate(rows):
                reached = not row.reserve or rng.random() < 0.5
                for j, (bill, title) in enumerate(row.unit.bills, start=1):
                    n += 1
                    label = f"r-{n}" if row.reserve else (f"{n - j + 1}.{j}" if len(row.unit.bills) > 1 else f"{n}")
                    items.append(
                        f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_base + n}" numeris="{label}" pavadinimas="{seimas.esc(title)}" stadija="{row.stage}" laikas_nuo="{at:%H:%M}">\n'
                        f'      <Dokumentas registracijos_numeris="{bill}" nuoroda="https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-{bill}"/>\n'
                        "    </DarbotvarkėsKlausimas>")
                    if reached:
                        pending.append((item_base + n, bill, row, title))
                at += dt.timedelta(minutes=slot(row))
                since_vote += 1
                if since_vote >= rng.randint(8, 12):
                    at = vote_block(at)
                    since_vote = 0
            at = vote_block(at)
            if not is_evening:
                n += 1
                items.append(f'    <DarbotvarkėsKlausimas darbotvarkės_klausimo_id="{item_base + n}" numeris="{n}" pavadinimas="Seimo narių pareiškimai" stadija="" laikas_nuo="{at:%H:%M}"/>')
                at += dt.timedelta(minutes=15)
            assert n < 100, "item identifiers stay within the sitting's hundred"
            kind = "Vakarinis" if is_evening else "Rytinis"
            times = f'pradžia="{token} {start:%H:%M}" pabaiga="{token} {at:%H:%M}"'
            sittings.append(f'    <SeimoPosėdis posėdžio_id="{sid}" numeris="{number}" tipas="{kind}" {times}/>')
            files[f"p2b.ad_sp_darbotvarke@posedzio_id={sid}.xml"] = (
                header + "<SeimoInformacija>\n"
                f'  <SeimoPosėdis posėdžio_id="{sid}" numeris="{number}" tipas="{kind}" {times}>\n'
                + "\n".join(items) + "\n  </SeimoPosėdis>\n</SeimoInformacija>\n")
            files[f"p2b.ad_sp_klausimo_svarstymo_eiga@posedzio_id={sid}.xml"] = (
                header + "<SeimoInformacija>\n"
                f'  <SeimoPosėdis posėdžio_id="{sid}">\n' + "\n".join(proceedings) + "\n  </SeimoPosėdis>\n</SeimoInformacija>\n")
    return files, sittings, stats


def _passing(rng: random.Random, day: dt.date, row: Row) -> list:
    """A recorded vote on a past row. A bill the upcoming agenda carries on passed; others may fail (an opposition bill
    after its presentation, say), as the groups' lines give."""
    must_pass = row.must_pass
    for _ in range(200):
        ballots = seimas.ballots(rng, day, row.unit.side)
        f, a, s = seimas.count(ballots)
        if not must_pass or f * 2 > f + a + s:
            return ballots
    raise AssertionError("no passing ballots")


def summary(rows_by_day: list[list[Row]]) -> list[str]:
    out = []
    for d, rows in enumerate(rows_by_day, start=1):
        bills = sum(len(r.unit.bills) for r in rows)
        packages = [len(r.unit.bills) for r in rows if len(r.unit.bills) > 1]
        stages = collections.Counter()
        kinds = collections.Counter()
        for r in rows:
            stages[r.stage] += len(r.unit.bills)
            for _, title in r.unit.bills:
                kinds[_kind_of(title)] += 1
        out.append(f"day {d}: {bills} bill rows, {len(rows)} items, {len(packages)} packages {sorted(packages)}, "
                   f"{sum(1 for r in rows if r.reserve)} reserve; stages "
                   + ", ".join(f"{s} {stages[s]}" for s in STAGE_MIX) + "; kinds " + ", ".join(f"{k} {v}" for k, v in kinds.most_common()))
    return out


def _kind_of(title: str) -> str:
    if title.startswith("Seimo nutarimo"):
        return "nutarimas"
    if "ratifikavimo" in title:
        return "ratifikavimas"
    if "nauja redakcija" in title:
        return "nauja redakcija"
    if "kodekso" in title:
        return "kodeksas"
    if "pakeitimo įstatymo projektas" in title or title.endswith("pataisa"):
        return "įstatymas"
    return "kita"


def files() -> tuple[dict[str, str], list[str], list[str]]:
    w, upcoming, past = plan()
    out, sittings, stats = past_files(past)
    out["agenda/darbotvarke.html"] = page(upcoming, w)
    report = (["upcoming:"] + summary(upcoming) + ["past ({{past:1}} first):"] + summary(past)
              + [f"past stage votes: {stats['votes']} ({stats['recorded']} with per-MP rows, {stats['consensus']} by consensus); "
                 f"procedural and proposal votes: {stats['other']}"])
    return out, sittings, report
