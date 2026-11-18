"""Writes the Seimas stand-in's local overlay, tools/Lt2.SeimasStandIn/Recent: every file in it.

Local stack and trial only: the container loads Recent/ over the base fixture; tests never load it, so their data stays
the base fixture's 14 MPs. The overlay holds
- the invented parliament of 141 seats (tools/invented_seimas.py), and the base fixture's votes again with the per-MP
  rows of all 141 (the base fixture's 14 rows unchanged, the totals unchanged);
- the invented closed bills of the analytics page and XVP-0707 nine days ago (tools/demo_closed_bills.py, design 8.26.5);
- the last four and the next four sitting days, shaped after the real Seimas's (tools/standin_agenda.py);
- the open autumn session's list of sittings, which names them all.
Fixed seeds: running it again writes the same files.

    python tools/standin_overlay.py            # writes the files (and removes others from Recent/), prints the agenda counts
    python tools/standin_overlay.py --check    # exits 1 when Recent/ differs from what it would write
"""

from __future__ import annotations

import sys

sys.dont_write_bytecode = True  # no __pycache__ beside the scripts

import demo_closed_bills  # noqa: E402
import invented_seimas as seimas  # noqa: E402
import standin_agenda  # noqa: E402

SESSION_NOTE = (
    "{{day:N}} is today plus N days and {{past:N}} the N-th Tuesday or Thursday before today (yyyy-MM-dd).\n"
    "     " + "Written by tools/standin_overlay.py (fixed seed); do not edit by hand: the autumn session's sittings, the base\n"
    "     fixture's first, then the recent sitting days and the invented closed bills' sittings, listed in this open session's\n"
    "     answer whatever their date because the import reads a closed session's sittings only once.")


def build() -> tuple[dict[str, str], list[str]]:
    files: dict[str, str] = {seimas.MEMBERS_FILE: seimas.members_file()}

    def add(more: dict[str, str]) -> None:
        clash = files.keys() & more.keys()
        assert not clash, f"written twice: {sorted(clash)}"
        files.update(more)

    add(seimas.revoted_base_files())
    closed, closed_sittings = demo_closed_bills.files()
    add(closed)
    recent, recent_sittings, report = standin_agenda.files()
    add(recent)
    add({"p2b.ad_seimo_posedziai@sesijos_id=99000102.xml": (
        seimas.HEADER.format(note=SESSION_NOTE) + "<SeimoInformacija>\n"
        '  <SeimoSesija sesijos_id="99000102" pavadinimas="Rudens sesija" data_nuo="2026-09-10" data_iki="">\n'
        '    <SeimoPosėdis posėdžio_id="99001101" numeris="241" tipas="Rytinis" pradžia="2026-09-15 10:00" pabaiga="2026-09-15 13:15"/>\n'
        + "\n".join(recent_sittings + closed_sittings) + "\n  </SeimoSesija>\n</SeimoInformacija>\n")})
    return files, report


def main() -> int:
    files, report = build()
    check = "--check" in sys.argv[1:]
    present = {p.relative_to(seimas.RECENT).as_posix() for p in seimas.RECENT.rglob("*") if p.is_file()}
    stale = sorted(present - files.keys())
    if check:
        differs = [name for name, text in sorted(files.items())
                   if not (seimas.RECENT / name).exists() or (seimas.RECENT / name).read_text(encoding="utf-8") != text]
        for name in differs:
            print(name)
        for name in stale:
            print(f"{name} (not written by this script)")
        if not differs and not stale:
            print("up to date")
        return 1 if differs or stale else 0
    for name in stale:
        (seimas.RECENT / name).unlink()
    for name, text in files.items():
        path = seimas.RECENT / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {len(files)} files to {seimas.RECENT.relative_to(seimas.ROOT).as_posix()}" + (f", removed {len(stale)}" if stale else ""))
    print("\n".join(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
