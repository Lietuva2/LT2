# LT2 interface prototype

Static pages that fix the look of the product before it is built in Blazor. `lt2.css` is the part to port: every colour,
type size and component the product uses is defined there. The HTML pages show the styles in use and are thrown away
once the real components exist.

| File | What |
|---|---|
| `lt2.css` | Tokens (colour for light and dark, type, space) and components. The source of truth for the look. |
| `index.html` | Style guide: the idea, every colour with its role, the type, each component, and the do and don't lists. In English, with Lithuanian samples. |
| `kreipimasis.html` | An appeal on a bill: the two sides, reasons, MPs' answers, threshold, Seimas stages. |
| `seimo-narys.html` | An MP profile: the two statistics, recent votes, appeals, mandate. |
| `suvestines.html` | The public copy of a summary sent to MPs: frozen numbers and reasons per bill, earlier summaries. |
| `prisijungimas.html` | Sign-in by emailed code, with the code, error and registration states, and the step after the first code. |
| `pradzia.html`, `pradzia-narys.html` | The front page as chosen on 2026-11-15 (design 8.7, variant A): a visitor's and a member's view. They use the site's own stylesheet (`src/Lt2.Web/wwwroot/css/lt2.css`), so open them from the repository root over HTTP; the dashed frames mark what the site fills in the browser. |
| `lt2-theme.js` | The theme switch (system, light, dark) used by every page. |
| `fonts/` | The three font families as `woff2`, their `@font-face` rules (`fonts.css`) and licence texts. |
| `lt2-logo-spin.js` | The turning logo, copied unchanged from the `docs` branch (`docs/lt2-logo-spin.js`). |

## Look at it

```sh
python -m http.server 8931 --bind 127.0.0.1
```

Then open `http://127.0.0.1:8931/design/ui/index.html`. Run it from the repository root. The pages also open straight
from disk, except the turning logo, which needs HTTP.

## The idea in four lines

- Flat sheets of cut paper on a pale slate ground. No shadows, gradients, rounded cards or pills; the speech bubbles' small
  radius is the one rounded shape.
- One corner of the main sheet is cut at an angle, like the logo's bubbles. Filled buttons and portraits carry a small
  cut; outlined buttons are straight.
- The logo's colours each belong to one speaker: **slate** is the Seimas record, **leaf** is people, **brand green** is
  Lietuva 2.0 and the representatives' answers. Words a person wrote sit in a speech bubble, in the serif, under their
  name: a straight box with a small radius and a tail pointing up to the name (owner, 2026-11-18; design 8.16.3).
- "For" is the links' green and "against" a red of the same lightness and strength, always with the word (owner,
  2026-11-18; until then a yellower moss, so "for" would not read as Lietuva 2.0 taking a side). Blue and red were
  dropped: beside white they recalled a flag.

## Porting to Blazor

- Copy the token blocks of `lt2.css` as they are into the product's one stylesheet. Do not rename tokens; components
  refer to them by name.
- One Razor component per block in the stylesheet: `Sheet`, `Bubble` (with a `Speaker` parameter: Seimas, People, Mp),
  `Button`, `Mark`, `Field`, `Tag`, `Choice`, `Figure`, `Split`, `Ruler`, `Facts`, `Rows`, `Stages`, `Note`, `Portrait`.
  All of them render plain HTML and work without JavaScript.
- The seat map in the guide is drawn by a script only for the demo. In the product it is SVG rendered on the server,
  using the same vote tokens.
- Both themes are required. Follow the system setting, and honour `data-theme="light"` or `"dark"` on the root element.
- The theme switch is `lt2-theme.js` plus one button with `data-theme-switch` in the page header. Copy the script as it
  is and load it from our own origin in `<head>`, not deferred, so the stored choice applies before the first paint. The
  button is hidden until the script runs: without JavaScript the page follows the system setting and shows no dead
  control. The choice is kept in the browser only (`localStorage`), never sent to the server.
- Numbers use `font-variant-numeric: tabular-nums` (class `num`) and a no-break space for thousands.

## Fonts

Sofia Sans, Sofia Sans Extra Condensed and Brygada 1918, all under the SIL Open Font License 1.1. The files are in
`fonts/`: ten `woff2` files (variable fonts, Latin and Latin Extended, about 230 KB together), `fonts.css` with the
`@font-face` rules, and the three licence texts. Nothing is loaded from Google or any other third party: the content
security policy in the design allows only our own origin, and visitors' addresses should not go elsewhere.

To port: copy the whole `fonts/` folder into the product's static files and link `fonts/fonts.css` before the main
stylesheet. The paths inside `fonts.css` are relative to it, so the folder works wherever it is put. The licence texts
must stay with the font files. After porting, check that ą č ę ė į š ų ū ž render in the real face, not a fallback.

## Rules that are not about looks

Copy in these pages follows `AGENTS.md`: numbers are from "Lietuva 2.0 naudotojai", never the public; planned dates are
"Planuojama …", never a countdown; a person's side is never shown; everything is fictional and says so in the footer.
The Lithuanian wording is a draft for a native reader to check.
