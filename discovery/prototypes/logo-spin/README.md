# LT2 rotating logo study

Open `index.html` in a browser (or serve the folder over HTTP). The main animation has a pause button; the slider and angle buttons inspect a full turn.

- Front and back: the flat logo. At the exact front the original website SVG paths and colours are rendered directly for an exact flat view.
- The solid is the logo extruded in depth, cut by a standing person extruded sideways, so as it turns the edge gradually takes the person's shape. The faces keep the flat logo colours; the side walls are a slightly darker tone.
- 90°: a man in a jacket and trousers. 270°: a woman with a bob, a knee-length dress and her legs below.
- Edge-on, each row takes the colour of the logo piece that reaches furthest to that side, so the figures put their clothing lines on those colour splits: the left edge is grey to the hips and dark green below; the right edge is grey to the chin, light green to the knee and dark green below.
- The figure switches from man to woman at the exact back (180°) and back at the exact front, where the depth cannot be seen.
- The legs are traced from reference drawings. `figuros.html` and `poros.html` keep the figure options that led to the final pair.

The WebGL 2 component uses no libraries or network requests; all logos on a page share one WebGL context. Reduced-motion users receive the still logo; unavailable WebGL falls back to SVG. The page is a private design prototype, outside the published `docs/` site.

```html
<script src="lt2-logo-spin.js" defer></script>
<lt2-logo-spin period="10" style="width:240px"></lt2-logo-spin>
```

Checked in Chromium: rendering at 0°, 20°, 45°, 70°, 90°, 110°, 180°, 200°, 250°, 270°, 300° and 340°; both figures cover every row of the logo; the demo page at 1280 px and 375 px draws every logo with no page errors or horizontal overflow.
