"""The stand-in's invented parliament, shared by the overlay's generators (tools/standin_overlay.py runs them all).

Local stack and trial only: tools/Lt2.SeimasStandIn/Recent is the stand-in container's overlay, never loaded by tests.
Its parliament has the real Seimas's shape (141 seats, seven groups of the real sizes, men and women as in the real
groups, a majority of three groups) and nothing else of it: every name, group, party, constituency and vote is invented.

The 14 MPs of the base fixture (tests/Lt2.Parliament.ImportTests/Seimas/Synthetic) are read from it and kept as they
are, so tests, the end-to-end suite and the demonstration data find them; 128 more fill the seats (Tadas Alksninis's
mandate ended, so 142 people, 141 seated). Fixed seeds: the same files every run.
"""

from __future__ import annotations

import dataclasses
import datetime as dt
import pathlib
import random
import unicodedata
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
RECENT = ROOT / "tools" / "Lt2.SeimasStandIn" / "Recent"
SYNTHETIC = ROOT / "tests" / "Lt2.Parliament.ImportTests" / "Seimas" / "Synthetic"
MEMBERS_FILE = "p2b.ad_seimo_nariai@kadencijos_id=10.xml"

TERM_FROM = "2024-11-14"
COMMITTEES_FROM = "2024-11-21"
SEATS = 141

HEADER = (
    '<?xml version="1.0" encoding="UTF-8"?>\n'
    "<!-- LT2-SYNTHETIC: invented by Lietuva 2.0 for the LOCAL stand-in only (tools/Lt2.SeimasStandIn/Recent). NOT a recorded\n"
    "     Seimas response. {note} -->"
)

FOR, AGAINST, ABSTAIN, ABSENT = "Už", "Prieš", "Susilaikė", ""
CHOICES = (FOR, AGAINST, ABSTAIN)


@dataclasses.dataclass(frozen=True)
class Group:
    body_id: int
    name: str
    label: str  # as a vote's per-MP rows state it
    party: str | None
    men: int
    women: int
    majority: bool | None  # None: the mixed group, which is not a faction

    @property
    def size(self) -> int:
        return self.men + self.women


# Sizes and men/women as the real groups' on 2026-11-18 (52, 28, 17, 13, 11, 9, 9 + the 2 seats outside groups put in
# the mixed group); the majority is the groups of 52, 17 and 9 (78 seats). The names are invented: the first three and
# the mixed group are the base fixture's, the other three are new; none is a Lithuanian party's.
GROUPS = [
    Group(99100001, "Reformų partijos frakcija", "RPF", "Reformų partija", 34, 18, True),
    Group(99100002, "Pilietinės sąjungos frakcija", "PSF", "Pilietinė sąjunga", 18, 10, False),
    Group(99100003, "Regionų aljanso frakcija", "RAF", "Regionų aljansas", 14, 3, True),
    Group(99100004, "Atviros visuomenės partijos frakcija", "AVPF", "Atviros visuomenės partija", 8, 5, False),
    Group(99100005, "Darnos sąjūdžio frakcija", "DSF", "Darnos sąjūdis", 9, 2, False),
    Group(99100006, "Bendruomenių sąjungos frakcija", "BSF", "Bendruomenių sąjunga", 6, 3, True),
    Group(99100009, "Mišri Seimo narių grupė", "MG", None, 10, 1, None),
]
GROUP_BY_ID = {g.body_id: g for g in GROUPS}
GROUP_BY_LABEL = {g.label: g for g in GROUPS}

# The Seimas's committees (real names: the bodies are public), with how many invented members each seats. The first two
# are the base fixture's. Every seated MP but Rasa Ievaitė (the base fixture gives her none) sits on one.
COMMITTEES = [
    (99200001, "Biudžeto ir finansų komitetas", 14), (99200002, "Socialinių reikalų ir darbo komitetas", 11),
    (99200003, "Ekonomikos komitetas", 11), (99200004, "Teisės ir teisėtvarkos komitetas", 11),
    (99200005, "Nacionalinio saugumo ir gynybos komitetas", 9), (99200006, "Užsienio reikalų komitetas", 9),
    (99200007, "Sveikatos reikalų komitetas", 9), (99200008, "Švietimo ir mokslo komitetas", 9),
    (99200009, "Kaimo reikalų komitetas", 9), (99200010, "Aplinkos apsaugos komitetas", 9),
    (99200011, "Valstybės valdymo ir savivaldybių komitetas", 9), (99200012, "Kultūros komitetas", 7),
    (99200013, "Audito komitetas", 8), (99200014, "Europos reikalų komitetas", 8), (99200015, "Žmogaus teisių komitetas", 7),
]
# Committees chaired by the opposition (the audit committee is, by the Seimas's own rule).
OPPOSITION_CHAIRS = {99200013, 99200015, 99200006}

MALE_NAMES = [
    "Andrius", "Antanas", "Arūnas", "Algirdas", "Audrius", "Aurimas", "Deividas", "Dovydas", "Edvardas", "Egidijus",
    "Eimantas", "Gediminas", "Giedrius", "Ignas", "Justinas", "Karolis", "Laurynas", "Linas", "Lukas", "Mantas",
    "Mindaugas", "Nerijus", "Paulius", "Povilas", "Ramūnas", "Remigijus", "Rimantas", "Robertas", "Rokas", "Saulius",
    "Simonas", "Tomas", "Valdas", "Vidmantas", "Vilius", "Virgilijus", "Žilvinas", "Juozas", "Kazimieras", "Petras",
    "Raimondas", "Stasys", "Artūras", "Evaldas", "Gintaras", "Ričardas", "Arvydas", "Julius", "Martynas", "Benediktas",
]
FEMALE_NAMES = [
    "Agnė", "Aistė", "Aušra", "Birutė", "Daiva", "Dalia", "Diana", "Edita", "Gabija", "Giedrė", "Ingrida", "Inga",
    "Irena", "Jolanta", "Jurgita", "Justina", "Kristina", "Lina", "Loreta", "Milda", "Monika", "Neringa", "Raimonda",
    "Renata", "Sandra", "Simona", "Skaistė", "Austėja", "Vaida", "Vilma", "Viktorija", "Živilė", "Danguolė", "Dovilė",
    "Evelina", "Gražina", "Ieva", "Jūratė", "Laura", "Kotryna",
]
# Invented surnames from plants, birds, animals and landscape, in the base fixture's style; the women's forms are
# derived (married -ienė, unmarried -aitė/-ytė/-iūtė). None is a root the base fixture's 14 use.
SURNAMES = [
    "Drebulis", "Guobaitis", "Skroblėnas", "Smilgaitis", "Nendrinskas", "Švendrys", "Dobilaitis", "Ramunėnas",
    "Viržinskas", "Kmynas", "Čiobrelis", "Bijūnaitis", "Rugelis", "Kvietinskas", "Avižėnas", "Miežinis", "Grikinis",
    "Aguonis", "Gegutis", "Gervinskas", "Gandrūnas", "Vyturys", "Strazdinis", "Genaitis", "Zylėnas", "Kėkštas",
    "Varnėnas", "Sakalinskas", "Ereliūnas", "Gulbėnas", "Ūdraitis", "Bebrinskas", "Briedėnas", "Stirnaitis",
    "Lūšinskas", "Kiškelis", "Eželis", "Lapelis", "Akmenaitis", "Smėlinis", "Upelinskas", "Ežerinskas", "Šaltinėnas",
    "Versmėnas", "Pelkinis", "Kalvėnas", "Slėninis", "Laukūnas", "Pievelis", "Šilinis", "Girinskas", "Miškūnas",
    "Debesinskas", "Raselis", "Vėjelis", "Audrinis", "Žaibūnas", "Saulėnas", "Žvaigždinis", "Rytelis", "Vakarinis",
    "Paukštėnas", "Kregždinis", "Pelėdinis", "Šarkelis", "Volungis", "Žvirblinskas", "Kurapkis", "Tetirvėnas",
    "Kerpinis", "Samanis", "Grybelis", "Uogėnas", "Avietinis", "Spanguolis", "Bruknėnas", "Mėlynis", "Viksvaitis",
    "Žiedelis", "Žiedūnas", "Lapūnas", "Šakelis", "Kamienis", "Šaknelis", "Žolinis", "Rasūnas", "Smilgėnas",
    "Medelis", "Gojelis", "Šilėnas", "Kalnėnas", "Kloninis", "Upėtakis", "Lydekinas", "Karšinskas", "Ešerinis",
    "Starkis", "Lyninis", "Vėžinis", "Varliūnas", "Driežinis", "Kamanis", "Bitėnas", "Vapsvinis", "Drugelis",
    "Laumėnas", "Šilkinis", "Linelis", "Kanapinis", "Pupūnas", "Žirninis", "Dygūnas", "Usninis", "Dilgėlis",
    "Šalpūnas", "Ajerinis", "Vingrūnas", "Kriaušinis", "Obelinis", "Slyvinskas", "Vyšninis", "Riešutis", "Lazdūnas",
    "Kaštonis", "Skirpstinis", "Maumedinis", "Kėninis", "Kukmedinis", "Sedulinis", "Šeivis", "Paparčinis",
    "Asiūkinis", "Gailinis", "Tujinis", "Karklinis", "Žilvitinis", "Blendinis", "Vijoklinis", "Apyninis", "Rūtinis",
]
# Invented single-member constituencies, numbered from 96 on: the Seimas's run from 1 to 71 (and the base fixture's 91–95).
CONSTITUENCIES = [
    "Pušyno", "Eglyno", "Beržyno", "Liepyno", "Alksnyno", "Drebulyno", "Guobų", "Skroblų", "Kadagyno", "Ievyno",
    "Smilgyno", "Nendryno", "Viržyno", "Dobilų", "Ramunių", "Rugiagėlių", "Kmynų", "Čiobrelių", "Bijūnų", "Lelijų",
    "Žibuoklių", "Pienių", "Varpų", "Linų", "Rugių", "Avižų", "Miežių", "Grikių", "Aguonų", "Gandrų", "Gervių",
    "Kregždžių", "Pelėdų", "Vyturių", "Strazdų", "Genių", "Zylių", "Kėkštų", "Gulbių", "Lūšių", "Briedžių", "Stirnų",
    "Bebrų", "Ūdrų", "Kiškių", "Ežių", "Lapių", "Šaltinių", "Versmių", "Upelių", "Ežerėlių", "Kalvų", "Slėnių",
    "Pievų", "Šilų", "Girių", "Vėjų", "Rasų", "Akmenų", "Smėlynų", "Samanų", "Kerpių", "Uogynų", "Spanguolynų",
    "Bruknių", "Ąžuolų", "Klevyno",
]


def female_forms(male: str) -> tuple[str, str]:
    """A man's surname → (married, unmarried) women's forms: Drebulis → Drebulienė, Drebulytė."""
    for end, married, unmarried in (("aitis", "aitienė", "aitytė"), ("ius", "ienė", "iūtė"), ("ys", "ienė", "ytė"),
                                    ("is", "ienė", "ytė"), ("as", "ienė", "aitė")):
        if male.endswith(end):
            stem = male[: -len(end)]
            return stem + married, stem + unmarried
    raise ValueError(male)


def fold(text: str) -> str:
    """Lower case without diacritics: "Ąžuolaitis" → "azuolaitis" (the address pattern and the page slugs)."""
    return "".join(c for c in unicodedata.normalize("NFKD", text) if not unicodedata.combining(c)).lower()


def esc(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def attrs(pairs: dict[str, str]) -> str:
    return " ".join(f'{k}="{esc(v)}"' for k, v in pairs.items())


@dataclasses.dataclass
class Mp:
    person_id: int
    first: str
    last: str
    attrs: dict[str, str]  # the SeimoNarys attributes, in order
    posts: list[dict[str, str]]  # the Pareigos attributes, in order
    email: str
    base: bool = False  # one of the base fixture's 14

    @property
    def mandate(self) -> tuple[dt.date, dt.date | None]:
        to = self.attrs["data_iki"]
        return dt.date.fromisoformat(self.attrs["data_nuo"]), dt.date.fromisoformat(to) if to else None

    def seated(self, day: dt.date) -> bool:
        start, end = self.mandate
        return start <= day and (end is None or day <= end)

    def label(self, day: dt.date) -> str | None:
        """The group label on a vote's per-MP row that day; None when the MP does not sit then. On the day of a change
        both memberships cover the day: the one that began last counts, as the import reads it."""
        if not self.seated(day):
            return None
        current = [p for p in self.posts if int(p["padalinio_id"]) in GROUP_BY_ID
                   and dt.date.fromisoformat(p["data_nuo"]) <= day
                   and (not p["data_iki"] or day <= dt.date.fromisoformat(p["data_iki"]))]
        current.sort(key=lambda p: p["data_nuo"])
        return GROUP_BY_ID[int(current[-1]["padalinio_id"])].label if current else None

    def group(self, day: dt.date) -> Group | None:
        lab = self.label(day)
        return GROUP_BY_LABEL[lab] if lab else None

    def xml(self) -> str:
        lines = [f"    <SeimoNarys {attrs(self.attrs)}>"]
        lines += [f"      <Pareigos {attrs(p)}/>" for p in self.posts]
        lines.append(f'      <Kontaktai rūšis="El. p." reikšmė="{esc(self.email)}"/>')
        lines.append("    </SeimoNarys>")
        return "\n".join(lines)


def _base_members() -> list[Mp]:
    tree = ET.parse(SYNTHETIC / MEMBERS_FILE)
    mps = []
    for e in tree.getroot().iter("SeimoNarys"):
        posts = [dict(p.attrib) for p in e.findall("Pareigos")]
        email = e.find("Kontaktai").attrib["reikšmė"]
        mps.append(Mp(int(e.attrib["asmens_id"]), e.attrib["vardas"], e.attrib["pavardė"], dict(e.attrib), posts, email, base=True))
    return mps


def _role(kind: str, gender: str) -> str:
    woman = gender == "M"
    return {
        "faction": "Frakcijos narė" if woman else "Frakcijos narys",
        "faction-elder": "Frakcijos seniūnė" if woman else "Frakcijos seniūnas",
        "faction-deputy": "Frakcijos seniūno pavaduotoja" if woman else "Frakcijos seniūno pavaduotojas",
        "group": "Grupės narė" if woman else "Grupės narys",
        "group-elder": "Grupės seniūnė" if woman else "Grupės seniūnas",
        "committee": "Komiteto narė" if woman else "Komiteto narys",
        "committee-chair": "Komiteto pirmininkė" if woman else "Komiteto pirmininkas",
        "committee-deputy": "Komiteto pirmininko pavaduotoja" if woman else "Komiteto pirmininko pavaduotojas",
    }[kind]


def build() -> list[Mp]:
    """The 142 people of the invented parliament (141 seated), the base fixture's 14 first."""
    rng = random.Random(141)
    base = _base_members()
    assert len(base) == 14, "the base fixture's 14 MPs"
    today = dt.date(2026, 11, 18)
    seated_base = [m for m in base if m.seated(today)]

    # Who is still needed in each group, by gender.
    need: dict[int, dict[str, int]] = {}
    for g in GROUPS:
        have = [m for m in seated_base if m.group(today) == g]
        men = sum(1 for m in have if m.attrs["lytis"] == "V")
        need[g.body_id] = {"V": g.men - men, "M": g.women - (len(have) - men)}
        assert need[g.body_id]["V"] >= 0 and need[g.body_id]["M"] >= 0, g.name

    surnames = SURNAMES[:]
    rng.shuffle(surnames)
    used_names: set[str] = {fold(m.first + " " + m.last) for m in base}
    single_left = 71 - sum(1 for m in seated_base if m.attrs["išrinkimo_būdas"].endswith(" apygardoje"))
    slots = [(g, gender) for g in GROUPS for gender in ("V", "M") for _ in range(need[g.body_id][gender])]
    rng.shuffle(slots)
    single = set(rng.sample(range(len(slots)), single_left))
    constituencies = CONSTITUENCIES[:]
    rng.shuffle(constituencies)
    parties = [g.party for g in GROUPS if g.party]

    new: list[Mp] = []
    for i, (g, gender) in enumerate(slots):
        person = 99000015 + i
        male_surname = surnames.pop()
        while True:
            if gender == "V":
                first, last = rng.choice(MALE_NAMES), male_surname
            else:
                married, unmarried = female_forms(male_surname)
                first, last = rng.choice(FEMALE_NAMES), married if rng.random() < 0.55 else unmarried
            if fold(first + " " + last) not in used_names:
                break
        used_names.add(fold(first + " " + last))
        if g.party:
            party = g.party
        else:  # the mixed group: MPs who left their factions, or nominated by none of them
            party = rng.choice(parties)
        how = {"išrinkimo_būdas": "Pagal sąrašą"}
        if i in single:
            how = {"išrinkimo_būdas": f"{constituencies.pop()} (Nr. {96 + len(CONSTITUENCIES) - len(constituencies) - 1}) apygardoje"}
        terms = rng.choices([1, 2, 3, 4, 5, 6, 7], [40, 25, 15, 9, 6, 3, 2])[0]
        a = {"asmens_id": str(person), "vardas": first, "pavardė": last, "lytis": gender, "data_nuo": TERM_FROM,
             "data_iki": "", "iškėlusi_partija": party, **how, "kadencijų_skaičius": str(terms)}
        kind = "faction" if g.majority is not None else "group"
        posts = [{"padalinio_id": str(g.body_id), "padalinio_pavadinimas": g.name, "pareigos": _role(kind, gender),
                  "data_nuo": TERM_FROM, "data_iki": ""}]
        email = f"pavyzdys.{fold(first)}.{fold(last)}@lrs.lt"
        new.append(Mp(person, first, last, a, posts, email))

    # Elders and their deputies, from the new members (the base fixture's three factions have their elders already).
    deputies = {99100001: 3, 99100002: 2, 99100003: 1, 99100004: 1, 99100005: 1, 99100006: 1}
    for g in GROUPS:
        members = [m for m in new if int(m.posts[0]["padalinio_id"]) == g.body_id]
        rng.shuffle(members)
        has_elder = any(m.group(today) == g and "seniūn" in m.posts[0]["pareigos"] for m in seated_base)
        if not has_elder:
            elder = members.pop()
            elder.posts[0]["pareigos"] = _role("faction-elder" if g.majority is not None else "group-elder", elder.attrs["lytis"])
        for m in members[: deputies.get(g.body_id, 0)]:
            m.posts[0]["pareigos"] = _role("faction-deputy", m.attrs["lytis"])

    # Committees: fill each to its size; one chair and one deputy chair each (the base fixture's two have their chairs).
    members_of: dict[int, list[Mp]] = {cid: [] for cid, _, _ in COMMITTEES}
    for m in seated_base:
        for p in m.posts:
            if int(p["padalinio_id"]) in members_of and not p["data_iki"]:
                members_of[int(p["padalinio_id"])].append(m)
    pool = new[:]
    rng.shuffle(pool)
    for cid, name, size in COMMITTEES:
        while len(members_of[cid]) < size:
            m = pool.pop()
            members_of[cid].append(m)
            m.posts.append({"padalinio_id": str(cid), "padalinio_pavadinimas": name, "pareigos": _role("committee", m.attrs["lytis"]),
                            "data_nuo": COMMITTEES_FROM, "data_iki": ""})
    assert not pool, "every new MP sits on a committee"
    for cid, _, _ in COMMITTEES:
        newcomers = [m for m in members_of[cid] if not m.base]
        has_chair = any("pirmininkė" in p["pareigos"] or p["pareigos"] == "Komiteto pirmininkas"
                        for m in members_of[cid] if m.base for p in m.posts if p["padalinio_id"] == str(cid))
        chair_from_majority = cid not in OPPOSITION_CHAIRS

        def side(m: Mp) -> bool:
            return GROUP_BY_ID[int(m.posts[0]["padalinio_id"])].majority is True

        if not has_chair:
            chair = next(m for m in newcomers if side(m) == chair_from_majority)
            newcomers.remove(chair)
            _set_committee_role(chair, cid, "committee-chair")
        deputy = next((m for m in newcomers if side(m) != chair_from_majority), newcomers[0])
        _set_committee_role(deputy, cid, "committee-deputy")

    everyone = base + sorted(new, key=lambda m: m.person_id)
    assert len({fold(m.first + " " + m.last) for m in everyone}) == len(everyone), "unique names"
    assert len({m.email for m in everyone}) == len(everyone), "unique addresses"
    assert sum(1 for m in everyone if m.seated(today)) == SEATS
    for g in GROUPS:
        seated = [m for m in everyone if m.group(today) == g]
        assert (sum(1 for m in seated if m.attrs["lytis"] == "V"), sum(1 for m in seated if m.attrs["lytis"] == "M")) == (g.men, g.women), g.name
    return everyone


def _set_committee_role(m: Mp, cid: int, kind: str) -> None:
    for p in m.posts:
        if p["padalinio_id"] == str(cid):
            p["pareigos"] = _role(kind, m.attrs["lytis"])


PARLIAMENT = build()


def members_file() -> str:
    """The overlay's answer to p2b.ad_seimo_nariai?kadencijos_id=10: the base fixture's 14 as they are, and 128 more."""
    base_text = (SYNTHETIC / MEMBERS_FILE).read_text(encoding="utf-8")
    for m in PARLIAMENT[:14]:
        assert m.xml() in base_text, f"the base fixture's {m.first} {m.last} is written as the fixture writes it"
    return (
        HEADER.format(note="Written by tools/standin_overlay.py (fixed seed); do not edit by hand: the invented parliament of\n"
                           "     141 seats, the base fixture's 14 MPs first (tools/invented_seimas.py).")
        + "<SeimoInformacija>\n"
        '  <SeimoKadencija kadencijos_id="10" pavadinimas="2024–2028 m. kadencija" data_nuo="2024-11-14" data_iki="">\n'
        + "\n".join(m.xml() for m in PARLIAMENT)
        + "\n  </SeimoKadencija>\n</SeimoInformacija>\n")


# ---------------------------------------------------------------------------------------------------------------------
# Votes

# How a group's line is drawn for a vote of each kind: the probability of each line (for, against, abstain); a group
# votes freely (no line) with the small probability FREE.
FREE = 0.05
PROFILES = {
    # a bill of the majority: the majority for; the opposition divided
    "majority": {True: (0.97, 0.01, 0.02), False: (0.35, 0.3, 0.35)},
    # broadly agreed: everyone for, some of the opposition abstain
    "consensual": {True: (0.98, 0.0, 0.02), False: (0.8, 0.02, 0.18)},
    # the opposition's bill: the opposition for, the majority against or abstaining
    "opposition": {True: (0.15, 0.4, 0.45), False: (0.9, 0.02, 0.08)},
    # rejected with more against than for
    "rejected-against": {True: (0.05, 0.85, 0.1), False: (0.6, 0.15, 0.25)},
    # rejected with no majority: the majority abstains
    "rejected-split": {True: (0.25, 0.15, 0.6), False: (0.5, 0.3, 0.2)},
    # procedural (the agenda): everyone for
    "procedural": {True: (1.0, 0.0, 0.0), False: (0.97, 0.0, 0.03)},
}
DISCIPLINE = {True: 0.95, False: 0.9}


def lines(rng: random.Random, profile: str) -> dict[str, str | None]:
    out: dict[str, str | None] = {}
    for g in GROUPS:
        if g.majority is None or rng.random() < FREE:
            out[g.label] = None
        else:
            out[g.label] = rng.choices(CHOICES, PROFILES[profile][g.majority])[0]
    return out


# Some MPs miss more votes than others; fixed per person.
_PRESENCE = {m.person_id: random.Random(m.person_id).uniform(0.6, 1.4) for m in PARLIAMENT}


def ballots(rng: random.Random, day: dt.date, profile: str, attendance: float | None = None,
            fixed: dict[int, tuple[str, str]] | None = None, totals: tuple[int, int, int] | None = None) -> list[tuple[Mp, str, str]]:
    """Every MP sitting that day with their label and choice (empty: did not vote). <fixed> keeps given rows (person →
    label, choice) as they are; <totals> makes the choices add up to (for, against, abstain) exactly."""
    fixed = fixed or {}
    group_lines = lines(rng, profile)
    if attendance is None:
        attendance = rng.uniform(0.72, 0.92)
    rows: list[list] = []
    for m in PARLIAMENT:
        lab = m.label(day)
        if lab is None:
            continue
        if m.person_id in fixed:
            rows.append([m, fixed[m.person_id][0], fixed[m.person_id][1], True])
            continue
        if rng.random() > attendance * _PRESENCE[m.person_id] / 1.0 and rng.random() < 0.9:
            choice = ABSENT
        else:
            line = group_lines.get(lab)
            g = GROUP_BY_LABEL[lab]
            if line is None:
                choice = rng.choices(CHOICES, (0.5, 0.25, 0.25))[0]
            elif rng.random() < DISCIPLINE[bool(g.majority)]:
                choice = line
            else:
                choice = rng.choice([c for c in CHOICES if c != line])
        rows.append([m, lab, choice, False])
    if totals is not None:
        _fit(rng, rows, totals, group_lines)
    return [(r[0], r[1], r[2]) for r in rows]


def _fit(rng: random.Random, rows: list[list], totals: tuple[int, int, int], group_lines: dict[str, str | None]) -> None:
    """Changes free rows until the choices add up to <totals>, preferring rows that already left their group's line."""
    target = dict(zip(CHOICES, totals))
    target[ABSENT] = len(rows) - sum(totals)
    assert target[ABSENT] >= 0, "more votes than MPs"
    for _ in range(10_000):
        count = {c: sum(1 for r in rows if r[2] == c) for c in (*CHOICES, ABSENT)}
        surplus = [c for c in count if count[c] > target[c]]
        deficit = [c for c in count if count[c] < target[c]]
        if not surplus:
            return
        give, take = rng.choice(surplus), rng.choice(deficit)
        movable = [r for r in rows if not r[3] and r[2] == give]
        off_line = [r for r in movable if group_lines.get(r[1]) != give]
        on_target = [r for r in movable if group_lines.get(r[1]) == take]
        row = rng.choice(on_target or off_line or movable)
        row[2] = take
    raise AssertionError("could not fit the totals")


def count(rows: list[tuple[Mp, str, str]]) -> tuple[int, int, int]:
    return tuple(sum(1 for _, _, c in rows if c == choice) for choice in CHOICES)  # type: ignore[return-value]


def result_file(note: str, vote_id: int, held: str, rows: list[tuple[Mp, str, str]], totals: tuple[int, int, int] | None = None,
                comment: str = "") -> str:
    """A p2b.ad_sp_balsavimo_rezultatai answer; the totals are the ballots' unless given (the official note's case)."""
    f, a, s = totals or count(rows)
    body = "\n".join(
        f'  <IndividualusBalsavimoRezultatas asmens_id="{m.person_id}" vardas="{m.first}" pavardė="{m.last}" frakcija="{lab}" kaip_balsavo="{c}"/>'
        for m, lab, c in rows)
    return (HEADER.format(note=note) + "<SeimoInformacija>\n"
            f'  <SeimoNariųBalsavimas balsavimo_id="{vote_id}">\n'
            f'  <BendriBalsavimoRezultatai balsavimo_laikas="{held}" viso="{SEATS}" balsavo="{f + a + s}" už="{f}" prieš="{a}" susilaikė="{s}" komentaras="{esc(comment)}"/>\n'
            f"{body}\n  </SeimoNariųBalsavimas>\n</SeimoInformacija>\n")


def base_rows(path: pathlib.Path) -> tuple[ET.Element, dict[int, tuple[str, str]]]:
    """A vote result file's totals element and its per-MP rows (person → label, choice)."""
    root = ET.parse(path).getroot()
    rows = {int(e.attrib["asmens_id"]): (e.attrib["frakcija"], e.attrib["kaip_balsavo"]) for e in root.iter("IndividualusBalsavimoRezultatas")}
    return root.find(".//BendriBalsavimoRezultatai"), rows


def agenda_item(item_id: int, number: str, title: str, stage: str, at: str, bill: str = "") -> str:
    suffix = f" (Nr. {bill})" if bill else ""
    link = f"https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/SINTETINIS-{bill}" if bill else ""
    return (
        f'    <DarbotvarkėsKlausimas numeris="{number}" pavadinimas="{title}{suffix}" laikas_nuo="{at}">\n'
        f'      <KlausimoStadija darbotvarkės_klausimo_id="{item_id}" pavadinimas="{stage}" dokumento_nuoroda="{link}"/>\n'
        '    </DarbotvarkėsKlausimas>')


def proceedings_vote(item_id: int, vote_id: int, held: str, wording: str) -> str:
    heading = "Pritarta bendru sutarimu" if "bendru sutarimu" in wording else ""
    return (
        f'      <darbotvarkes-klausimas svarst_kl_stad_id="{vote_id}" kl_stad_id="{item_id}">\n'
        '        <nr/><pavadinimas/><stadija/>\n'
        f'        <balsavimai><balsavimas bals_id="{vote_id}"><nuo>{held}</nuo><aprasas>{wording}</aprasas>'
        f'<antraste>{heading}</antraste></balsavimas></balsavimai>\n'
        '      </darbotvarkes-klausimas>')


def proceedings_file(header: str, sitting_id: int, proceedings: list[str]) -> tuple[str, str]:
    return (
        f"p2b.ad_seimo_posedzio_eiga_full@posedzio_id={sitting_id}.xml",
        header + f'<posedziai>\n  <posedis pos_id="{sitting_id}">\n    <posedzio-eiga>\n'
        + "\n".join(proceedings) + "\n    </posedzio-eiga>\n  </posedis>\n</posedziai>\n")


REVOTED_NOTE = (
    "Written by tools/standin_overlay.py (fixed seed); do not edit by hand: the base fixture's vote with\n"
    "     the per-MP rows of all 141 seats (the base fixture's 14 rows as they are), adding up to its totals.")

# The base fixture's votes, and how the other MPs voted in each. 99100004 carries the official note that the per-MP
# rows do not match the protocol's totals: its rows are made to differ from them a little.
BASE_VOTES = {99100001: "procedural", 99100002: "majority", 99100004: "majority", 99100005: "opposition",
              99100006: "majority", 99100101: "procedural", 99100102: "majority"}


def revoted_base_files() -> dict[str, str]:
    files = {}
    for vote_id, profile in BASE_VOTES.items():
        name = f"p2b.ad_sp_balsavimo_rezultatai@balsavimo_id={vote_id}.xml"
        totals_el, fixed = base_rows(SYNTHETIC / name)
        held = totals_el.attrib["balsavimo_laikas"]
        totals = (int(totals_el.attrib["už"]), int(totals_el.attrib["prieš"]), int(totals_el.attrib["susilaikė"]))
        comment = totals_el.attrib.get("komentaras", "")
        rng = random.Random(vote_id)
        fit = (totals[0] - 3, totals[1] + 2, totals[2]) if comment else totals
        rows = ballots(rng, dt.date.fromisoformat(held[:10]), profile, fixed=fixed, totals=fit)
        files[name] = result_file(REVOTED_NOTE, vote_id, held, rows, totals=totals, comment=comment)
    return files
