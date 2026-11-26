"""Makes the static fonts the vote preview image is drawn with (design 8.22.5), from the site's own web fonts.

The web fonts are variable WOFF2 files split into a Latin and a Latin Extended subset (wwwroot/fonts). The image is drawn
on the server by SkiaSharp, which wants one plain TrueType file per weight, so this script fixes each weight, joins the two
subsets and writes src/Lt2.Web/Previews/Fonts/*.ttf. Run it again only when the web fonts change:

    pip install fonttools brotli
    python tools/make_preview_fonts.py
"""
import os
import tempfile
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools.merge import Merger

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WEB = os.path.join(ROOT, "src", "Lt2.Web", "wwwroot", "fonts")
OUT = os.path.join(ROOT, "src", "Lt2.Web", "Previews", "Fonts")

FONTS = [
    ("sofia-sans-extra-condensed", 800, "SofiaSansExtraCondensed-ExtraBold.ttf"),
    ("sofia-sans", 500, "SofiaSans-Medium.ttf"),
    ("sofia-sans", 700, "SofiaSans-Bold.ttf"),
]


def static(path, weight, into):
    font = TTFont(path)
    instancer.instantiateVariableFont(font, {"wght": weight}, inplace=True)
    font.flavor = None
    font.save(into)


def main():
    with tempfile.TemporaryDirectory() as tmp:
        for family, weight, name in FONTS:
            parts = []
            for subset in ("latin", "latin-ext"):
                part = os.path.join(tmp, f"{family}-{subset}-{weight}.ttf")
                static(os.path.join(WEB, f"{family}-{subset}.woff2"), weight, part)
                parts.append(part)
            merged = Merger().merge(parts)
            merged.save(os.path.join(OUT, name))
            print(name)


if __name__ == "__main__":
    main()
