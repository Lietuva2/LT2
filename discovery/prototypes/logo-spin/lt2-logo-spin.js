/*!
 * <lt2-logo-spin> – the Lietuva 2.0 logo turning in 3D.
 *
 * Seen from the front the object is the flat logo; turned edge-on, its
 * outline is a person: a man on one edge, a woman on the other.
 *
 * Usage:
 *   <script src="lt2-logo-spin.js" defer></script>
 *   <lt2-logo-spin style="width:240px"></lt2-logo-spin>
 *
 * Attributes (all optional):
 *   motion       "spin" (default) – full turns: logo → man → woman → logo;
 *                "swing" – turns to one edge and back, then to the other,
 *                so the logo is never seen from behind.
 *   period       seconds per loop (default 7.2 for spin, 8.8 for swing).
 *   angle        fixed angle in degrees; disables the animation.
 *   perspective  camera distance in logo units (default 0 = orthographic).
 *   label        accessible name (default "Lietuva 2.0 logotipas").
 *
 * The element is sized by CSS (default width 200px, aspect ratio of the
 * logo). It pauses while off-screen, shows the still logo when the reader
 * prefers reduced motion, and falls back to a flat SVG without WebGL 2.
 *
 * How it works: the solid is the intersection of the logo extruded along
 * the depth axis and a standing person extruded along the width axis, so
 * the front view is the logo and the side view is the person. The front and
 * back faces are drawn in the flat logo colours without shading, so from the
 * front it reads as the 2D logo; the side walls are a darker tone, and the
 * thickness shows as it turns. Both sides of one solid would show the same
 * outline, so the person changes from man to woman at the exact back, and
 * back at the exact front, where the depth cannot be seen.
 * The shape is ray-marched in a fragment shader over signed distance fields
 * computed once, here, from the logo polygons and the figure outlines.
 *
 * The figures' colours come from the logo: edge-on, each row shows the
 * colour of the logo piece that reaches furthest to that side. So the
 * colours come in fixed bands, and the figures put their natural colour
 * changes on them:
 *   left edge (90°, man):     grey to y 321 (jacket hem), dark green below;
 *   right edge (270°, woman): grey to y 125 (hair ends at the chin), light
 *                             green to y 373 (dress hem), dark green below.
 * Each figure also covers every row the logo covers (y 53–511), or the
 * front view would get a slit.
 */
(function () {
  'use strict';
  if (typeof window === 'undefined' || !window.customElements || customElements.get('lt2-logo-spin')) return;

  // Logo polygons in the SVG's user units (viewBox 28 44 604 474), drawn
  // with a 10-unit round-joined stroke of the same colour.
  var LOGO = [
    { color: '#a7b1ac', pts: [70, 78, 345, 106, 352, 58, 396, 108, 292, 112, 293, 240, 168, 243, 165, 316, 40, 298] },
    { color: '#7cc242', pts: [318, 142, 556, 130, 558, 190, 618, 192, 566, 256, 570, 352, 330, 368] },
    { color: '#1f6b3a', pts: [200, 265, 292, 270, 300, 396, 478, 390, 532, 466, 476, 470, 474, 506, 186, 490] }
  ];
  var STROKE = 5;          // half the stroke width
  var CENTER = [330, 281]; // centre of the logo's bounding box
  // Distance-field textures: origin and size in logo units, one texel per unit.
  var LOGO_TEX = { x: 20, y: 36, w: 620, h: 490 };
  var FIG_TEX = { x: -96, y: 36, w: 192, h: 490 }; // x here is depth (z)
  var BOX = [300, 235, 88]; // half extents of the solid, centred

  // Signed distance to a polygon (flat x, y list), negative inside.
  function sdPoly(px, py, v) {
    var n = v.length / 2, d = Infinity, s = 1;
    for (var i = 0, j = n - 1; i < n; j = i, i++) {
      var ix = v[2 * i], iy = v[2 * i + 1], jx = v[2 * j], jy = v[2 * j + 1];
      var ex = jx - ix, ey = jy - iy, wx = px - ix, wy = py - iy;
      var t = Math.max(0, Math.min(1, (wx * ex + wy * ey) / (ex * ex + ey * ey)));
      var bx = wx - ex * t, by = wy - ey * t;
      d = Math.min(d, bx * bx + by * by);
      var c1 = py >= iy, c2 = py < jy, c3 = ex * wy > ey * wx;
      if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
    }
    return s * Math.sqrt(d);
  }
  // Smooth union: joins two shapes with a soft fillet about k units wide.
  function smin(a, b, k) {
    var h = Math.max(k - Math.abs(a - b), 0) / k;
    return Math.min(a, b) - h * h * k * 0.25;
  }

  // ---- Figure outlines ----
  // Paths are absolute M/L/C in (depth z, logo y) as the figure appears on
  // screen; the right-edge figure is mirrored because it is seen from the
  // other side.
  function samplePath(d) {
    var t = d.match(/[MLCZ]|-?\d*\.?\d+/g), pts = [], i = 0, cmd, x = 0, y = 0;
    function n() { return parseFloat(t[i++]); }
    while (i < t.length) {
      if (/[MLCZ]/.test(t[i])) cmd = t[i++];
      if (cmd === 'Z') continue;
      if (cmd === 'M' || cmd === 'L') { x = n(); y = n(); pts.push(x, y); continue; }
      var x1 = n(), y1 = n(), x2 = n(), y2 = n(), x3 = n(), y3 = n();
      for (var s = 1; s <= 16; s++) {
        var u = s / 16, v = 1 - u;
        pts.push(v * v * v * x + 3 * v * v * u * x1 + 3 * v * u * u * x2 + u * u * u * x3,
          v * v * v * y + 3 * v * v * u * y1 + 3 * v * u * u * y2 + u * u * u * y3);
      }
      x = x3; y = y3;
    }
    // A closing point equal to the first would be a zero-length edge.
    if (pts.length > 2 && pts[0] === x && pts[1] === y) pts.length -= 2;
    return pts;
  }
  // A symmetric outline from its right half (top centre to bottom centre).
  function halfPath(d) {
    var p = samplePath(d), out = p.slice();
    for (var i = p.length - 2; i >= 0; i -= 2) if (p[i] !== 0) out.push(-p[i], p[i + 1]);
    return out;
  }
  function mirror(p) { return p.map(function (v, i) { return i % 2 ? v : -v; }); }
  function pair(p) { return [p, mirror(p)]; }
  // Distance to a set of outlines, joined softly. Far outside an outline's
  // bounding box the distance to the box is enough (and never too large).
  function outlines(polys) {
    var parts = polys.map(function (p) {
      var b = [Infinity, Infinity, -Infinity, -Infinity];
      for (var i = 0; i < p.length; i += 2) {
        b[0] = Math.min(b[0], p[i]); b[1] = Math.min(b[1], p[i + 1]);
        b[2] = Math.max(b[2], p[i]); b[3] = Math.max(b[3], p[i + 1]);
      }
      return { p: p, b: b };
    });
    return function (z, y) {
      var d = Infinity;
      parts.forEach(function (o) {
        var dx = Math.max(o.b[0] - z, z - o.b[2], 0), dy = Math.max(o.b[1] - y, y - o.b[3], 0);
        var e = dx > 8 || dy > 8 ? Math.hypot(dx, dy) : sdPoly(z, y, o.p);
        d = d === Infinity ? e : smin(d, e, 3);
      });
      return d;
    };
  }

  // Legs traced from the reference drawings, row by row (left and right leg
  // averaged, lightly smoothed): the right leg from the outer top, round the
  // foot, to the inner top. The straight top edge is hidden under the hem.
  var MAN_LEG = [43.2,316,43.2,322,43.4,323.3,43.3,324.6,43.2,326,43.1,327.3,43,328.6,42.9,329.9,42.9,331.2,42.8,332.6,42.7,333.9,42.6,335.2,42.5,336.5,42.4,337.8,42.4,339.2,42.3,340.5,42.2,341.8,42.1,343.1,42,344.4,41.9,345.8,41.9,347.1,41.8,348.4,41.7,349.7,41.6,351,41.5,352.3,41.3,353.7,41.3,355,41.2,356.3,41,357.6,40.9,358.9,40.8,360.3,40.6,361.6,40.4,362.9,40.3,364.2,40.1,365.5,40,366.9,39.8,368.2,39.6,369.5,39.5,370.8,39.3,372.1,39.1,373.5,39,374.8,38.8,376.1,38.6,377.4,38.4,378.7,38.3,380.1,38.1,381.4,37.9,382.7,37.8,384,37.7,385.3,37.6,386.7,37.6,388,37.6,389.3,37.5,390.6,37.5,391.9,37.4,393.3,37.4,394.6,37.4,395.9,37.5,397.2,37.5,398.5,37.5,399.8,37.6,401.2,37.7,402.5,37.8,403.8,38,405.1,38.2,406.4,38.3,407.8,38.5,409.1,38.7,410.4,38.9,411.7,38.9,413,39.1,414.4,39.1,415.7,39.3,417,39.4,418.3,39.4,419.6,39.3,421,39.2,422.3,39.1,423.6,39,424.9,38.9,426.2,38.7,427.6,38.5,428.9,38.3,430.2,38,431.5,37.8,432.8,37.6,434.2,37.3,435.5,37.1,436.8,36.8,438.1,36.5,439.4,36.3,440.8,36,442.1,35.7,443.4,35.5,444.7,35.2,446,34.9,447.3,34.6,448.7,34.4,450,34.2,451.3,33.9,452.6,33.7,453.9,33.4,455.3,33.2,456.6,33.1,457.9,32.9,459.2,32.7,460.5,32.6,461.9,32.4,463.2,32.3,464.5,32.1,465.8,32,467.1,31.8,468.5,31.6,469.8,31.4,471.1,31.2,472.4,30.9,473.7,30.9,475.1,30.8,476.4,30.7,477.7,30.6,479,30.6,480.3,30.6,481.7,30.6,483,30.6,484.3,30.6,485.6,30.7,486.9,30.8,488.3,31,489.6,31.2,490.9,31.4,492.2,31.7,493.5,32.3,494.8,33,496.2,33.8,497.5,34.6,498.8,35.5,500.1,36.3,501.4,36.9,502.8,37.4,504.1,37.6,505.4,37.5,506.7,36.9,508,36.9,508.3,36.6,508.5,36.4,508.7,36.3,508.9,36,509.1,35.6,509.4,35.4,509.6,35,509.8,34.6,510,34.3,510.2,33.5,510.5,33,510.7,32.3,510.9,31.5,511.1,30.5,511.3,29.3,511.6,27.8,511.8,25.4,512,20.9,512,19,511.8,17.8,511.6,16.8,511.3,16.1,511.1,15.6,510.9,15.1,510.7,14.8,510.5,14.4,510.2,14.1,510,13.9,509.8,13.6,509.6,13.5,509.4,13.3,509.1,13.1,508.9,13,508.7,12.9,508.5,12.8,508.3,12.6,508,12.3,506.7,12.1,505.4,12.1,504.1,12.2,502.8,12.2,501.4,12.3,500.1,12.3,498.8,12.4,497.5,12.4,496.2,12.3,494.8,12.2,493.5,12,492.2,11.9,490.9,11.8,489.6,11.6,488.3,11.6,486.9,11.6,485.6,11.5,484.3,11.4,483,11.4,481.7,11.4,480.3,11.4,479,11.4,477.7,11.5,476.4,11.6,475.1,11.7,473.7,11.8,472.4,11.9,471.1,12,469.8,12,468.5,12.1,467.1,12.1,465.8,12.1,464.5,12,463.2,12,461.9,12,460.5,12,459.2,12,457.9,11.9,456.6,11.8,455.3,11.7,453.9,11.6,452.6,11.4,451.3,11.3,450,11.1,448.7,11,447.3,10.8,446,10.7,444.7,10.5,443.4,10.4,442.1,10.3,440.8,10.1,439.4,10,438.1,9.9,436.8,9.8,435.5,9.7,434.2,9.6,432.8,9.6,431.5,9.5,430.2,9.5,428.9,9.5,427.6,9.5,426.2,9.6,424.9,9.6,423.6,9.7,422.3,9.8,421,9.9,419.6,10,418.3,10.1,417,10.3,415.7,10.4,414.4,10.6,413,10.7,411.7,10.9,410.4,10.9,409.1,10.9,407.8,11,406.4,11,405.1,11,403.8,11,402.5,10.9,401.2,10.8,399.8,10.7,398.5,10.5,397.2,10.3,395.9,10.2,394.6,10,393.3,9.9,391.9,9.8,390.6,9.8,389.3,9.8,388,9.7,386.7,9.7,385.3,9.5,384,9.4,382.7,9.2,381.4,9.1,380.1,8.9,378.7,8.7,377.4,8.6,376.1,8.4,374.8,8.3,373.5,8.1,372.1,7.9,370.8,7.8,369.5,7.6,368.2,7.5,366.9,7.3,365.5,7.1,364.2,7,362.9,6.8,361.6,6.6,360.3,6.5,358.9,6.3,357.6,6.2,356.3,6,355,5.8,353.7,5.7,352.3,5.5,351,5.3,349.7,5.2,348.4,5,347.1,4.8,345.8,4.7,344.4,4.5,343.1,4.4,341.8,4.2,340.5,4.1,339.2,3.9,337.8,3.7,336.5,3.6,335.2,3.4,333.9,3.2,332.6,3,331.2,2.8,329.9,2.6,328.6,2.4,327.3,1.3,326,0,324.6,0,323.3,0,322,0,316];
  var WOMAN_LEG = [33.9,367,33.9,373,33.8,374.4,33.5,375.8,33.3,377.2,33,378.6,32.7,380,32.5,381.4,32.2,382.8,31.9,384.2,31.7,385.6,31.4,387,31.2,388.4,31,389.8,30.8,391.2,30.6,392.6,30.5,394,30.3,395.4,30.2,396.8,30.2,398.2,30.2,399.6,30.2,401,30.2,402.4,30.2,403.8,30.4,405.2,30.6,406.6,30.9,408,31.1,409.4,31.4,410.8,31.7,412.2,32,413.6,32.2,415.1,32.5,416.5,32.8,417.9,33,419.3,33.2,420.7,33.4,422.1,33.5,423.5,33.7,424.9,33.7,426.3,33.8,427.7,33.9,429.1,33.9,430.5,33.9,431.9,33.8,433.3,33.7,434.7,33.7,436.1,33.5,437.5,33.3,438.9,33.1,440.3,32.9,441.7,32.7,443.1,32.4,444.5,32.1,445.9,31.8,447.3,31.5,448.7,31.2,450.1,30.9,451.5,30.6,452.9,30.3,454.3,30,455.7,29.6,457.1,29.3,458.5,28.9,459.9,28.6,461.3,28.3,462.7,27.9,464.1,27.5,465.5,27.2,466.9,26.9,468.3,26.6,469.7,26.3,471.1,26,472.5,25.7,473.9,25.5,475.3,25.3,476.7,25.2,478.1,25.2,479.5,25.3,480.9,25.7,482.3,26.1,483.7,26.6,485.1,26.8,486.5,26.8,487.9,27,489.3,27.3,490.7,27.7,492.1,28.1,493.5,28.5,494.9,28.9,496.3,29.5,497.7,30,499.2,30.7,500.6,31.3,502,31.8,503.4,32,504.8,32.1,506.2,31.8,507.6,31.5,508,31.5,508.3,31.5,508.5,31.3,508.7,31,509,31,509.2,30.7,509.4,30.5,509.7,30.2,509.9,29.8,510.1,29.4,510.4,28.9,510.6,28.4,510.8,27.8,511.1,27,511.3,26.2,511.5,25,511.8,23.1,512,16.8,512,15.4,511.8,14.3,511.5,13.5,511.3,13,511.1,12.5,510.8,11.9,510.6,11.7,510.4,11.3,510.1,11,509.9,10.9,509.7,10.6,509.4,10.3,509.2,10.3,509,10.1,508.7,9.9,508.5,9.8,508.3,9.8,508,9.5,507.6,9.4,506.2,9.3,504.8,9.4,503.4,9.5,502,9.5,500.6,9.5,499.2,9.6,497.7,9.8,496.3,9.8,494.9,9.8,493.5,10,492.1,10.1,490.7,10.1,489.3,10.3,487.9,10.3,486.5,10.3,485.1,10.3,483.7,10.3,482.3,10.4,480.9,10.7,479.5,10.9,478.1,10.9,476.7,10.8,475.3,10.7,473.9,10.5,472.5,10.5,471.1,10.4,469.7,10.2,468.3,10.2,466.9,10.1,465.5,10,464.1,9.8,462.7,9.7,461.3,9.6,459.9,9.5,458.5,9.4,457.1,9.2,455.7,9,454.3,8.9,452.9,8.7,451.5,8.5,450.1,8.4,448.7,8.2,447.3,8,445.9,7.9,444.5,7.7,443.1,7.6,441.7,7.4,440.3,7.3,438.9,7.2,437.5,7.1,436.1,6.9,434.7,6.9,433.3,6.9,431.9,6.9,430.5,6.9,429.1,6.9,427.7,7.1,426.3,7.2,424.9,7.2,423.5,7.4,422.1,7.5,420.7,7.7,419.3,7.7,417.9,7.9,416.5,8,415.1,8,413.6,8.1,412.2,8,410.8,8,409.4,7.8,408,7.6,406.6,7.4,405.2,7.1,403.8,6.8,402.4,6.6,401,6.3,399.6,6,398.2,5.8,396.8,5.7,395.4,5.5,394,5.3,392.6,5.3,391.2,5.1,389.8,5,388.4,5,387,4.8,385.6,4.8,384.2,4.7,382.8,4.6,381.4,4.5,380,4.5,378.6,4.4,377.2,4.2,375.8,4.2,374.4,4.2,373,4.2,367];

  // Left edge: a man with an egg-shaped head, a jacket to the hips and
  // trousers; his hands end at the jacket's hem so they stay grey.
  var sdMan = outlines([
    halfPath('M 0 52 C 13 52 21 62 21 77 C 21 90 18 100 13 105 C 12 106 12.5 107 12.5 108 L 12.5 120 ' +
      'C 13 125 20 128 30 131 C 40 134 48 137 54 142 C 59 147 62 154 62 164 C 60 172 51 178 46 183 ' +
      'C 42 200 38 222 38 240 C 38 262 42 296 44 314 C 44.5 318 44.5 321 44 322 L 0 322')
  ].concat(
    pair(samplePath('M 57 147 C 64 153 67 166 67 182 C 68 200 70 222 70 240 C 70 258 68 272 66 286 ' +
      'C 67 296 67 306 64 313 C 61 319 55 321 51 319 C 47 317 46 312 47 306 C 48 302 50 298 49 294 ' +
      'C 50 282 50 262 49 246 C 48 225 47 200 46 183 C 45 170 50 151 57 147 Z')),
    pair(MAN_LEG)
  ));
  // Right edge: a woman with a full bob that ends at the chin (grey), a
  // dress to the knee (light green) and legs below (dark green).
  var sdWoman = outlines([
    halfPath('M 0 52 C 19 52 28 64 29 83 C 30 99 29 111 27 118 C 25 123 21 125 16 124 C 13 124 11 124 10 124 L 0 124'),
    halfPath('M 0 120 L 10 120 L 10 135 C 12 139 24 142 36 145 C 46 148 53 152 56 160 C 57 168 50 180 40 192 ' +
      'C 35 208 32 228 32 246 C 32 262 40 278 44 292 C 47 312 48 345 49 366 C 49 370 47 371 44 371 L 0 371')
  ].concat(
    pair(samplePath('M 52 154 C 59 160 61 175 61 190 C 61 212 62 235 61 255 C 60 275 60 288 60 296 ' +
      'C 61 306 61 316 58 322 C 55 327 49 328 46 324 C 43 320 43 314 44 308 C 45 304 46 300 45 296 ' +
      'C 45 282 46 268 46 256 C 47 236 47 214 45 196 C 44 184 46 160 52 154 Z')),
    pair(WOMAN_LEG)
  ).map(mirror));

  function logoField() {
    var L = new Float32Array(LOGO_TEX.w * LOGO_TEX.h * 4), i = 0;
    for (var r = 0; r < LOGO_TEX.h; r++) {
      var y = LOGO_TEX.y + r + 0.5;
      for (var c = 0; c < LOGO_TEX.w; c++, i += 4) {
        var x = LOGO_TEX.x + c + 0.5;
        for (var k = 0; k < 3; k++) L[i + k] = sdPoly(x, y, LOGO[k].pts) - STROKE;
      }
    }
    return L;
  }
  // Both figures in one texture: red = man, green = woman.
  function figureField() {
    var F = new Float32Array(FIG_TEX.w * FIG_TEX.h * 4), i = 0;
    for (var r = 0; r < FIG_TEX.h; r++) {
      var y = FIG_TEX.y + r + 0.5;
      for (var c = 0; c < FIG_TEX.w; c++, i += 4) {
        var z = FIG_TEX.x + c + 0.5;
        F[i] = sdMan(z, y);
        F[i + 1] = sdWoman(z, y);
      }
    }
    return F;
  }

  function hexToVec(h) {
    return [1, 3, 5].map(function (o) { return parseInt(h.substr(o, 2), 16) / 255; });
  }

  var VS = '#version 300 es\n' +
    'void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2.-1.,0.,1.);}';

  var FS = '#version 300 es\n' +
    'precision highp float;\n' +
    'uniform sampler2D uLogo,uFig;\n' +
    'uniform vec2 uRes,uHalf;\n' +
    'uniform float uAng,uSide,uDist;\n' +
    'uniform vec3 uCol0,uCol1,uCol2;\n' +
    'out vec4 o;\n' +
    'const vec2 LO=vec2(' + LOGO_TEX.x + '.,' + LOGO_TEX.y + '.),LS=vec2(' + LOGO_TEX.w + '.,' + LOGO_TEX.h + '.);\n' +
    'const vec2 FO=vec2(' + FIG_TEX.x + '.,' + FIG_TEX.y + '.),FSZ=vec2(' + FIG_TEX.w + '.,' + FIG_TEX.h + '.);\n' +
    'const vec2 C=vec2(' + CENTER[0] + '.,' + CENTER[1] + '.);\n' +
    'const vec3 BOX=vec3(' + BOX[0] + '.,' + BOX[1] + '.,' + BOX[2] + '.);\n' +
    'const float R=3.;\n' +
    // Outside a texture, clamp and add the distance to the clamped point.
    'vec3 logoAt(vec2 q){vec2 c=clamp(q,LO+.5,LO+LS-.5);return texture(uLogo,(c-LO)/LS).rgb+length(q-c);}\n' +
    'float figAt(vec2 q){vec2 c=clamp(q,FO+.5,FO+FSZ-.5);vec2 s=texture(uFig,(c-FO)/FSZ).rg;return mix(s.r,s.g,uSide)+length(q-c);}\n' +
    // a: logo outline (side walls), b: figure outline (front and back faces).
    'vec2 parts(vec3 p){vec2 q=vec2(p.x+C.x,C.y-p.y);vec3 l=logoAt(q);return vec2(min(min(l.r,l.g),l.b),figAt(vec2(p.z,q.y)))+R;}\n' +
    // Intersection of the two extrusions, edges rounded by R.
    'float map(vec3 p){vec2 ab=parts(p);return length(max(ab,0.))+min(max(ab.x,ab.y),0.)-R;}\n' +
    'vec3 nrm(vec3 p){vec2 e=vec2(.5,-.5);return normalize(e.xyy*map(p+e.xyy)+e.yyx*map(p+e.yyx)+e.yxy*map(p+e.yxy)+e.xxx*map(p+e.xxx));}\n' +
    'vec3 base(vec3 p){vec3 l=logoAt(vec2(p.x+C.x,C.y-p.y));return l.b<.8?uCol2:l.g<.8?uCol1:uCol0;}\n' +
    'void main(){\n' +
    ' vec2 uv=gl_FragCoord.xy/uRes*2.-1.;\n' +
    ' vec3 sp=vec3(uv*uHalf,0.),ro,rd;\n' +
    ' if(uDist>0.){ro=vec3(0.,0.,uDist);rd=normalize(sp-ro);}else{ro=sp+vec3(0.,0.,1000.);rd=vec3(0.,0.,-1.);}\n' +
    ' float c=cos(uAng),s=sin(uAng);mat3 Ry=mat3(c,0.,-s,0.,1.,0.,s,0.,c);\n' +
    // object = transpose(Ry) * world  (v*M == transpose(M)*v)
    ' vec3 oo=ro*Ry,od=rd*Ry;\n' +
    ' vec3 Lv=normalize(vec3(-.3,.4,1.)),Lo=Lv*Ry;\n' +
    ' vec3 inv=1./od,t0=(-BOX-oo)*inv,t1=(BOX-oo)*inv,tmn=min(t0,t1),tmx=max(t0,t1);\n' +
    ' float tn=max(max(tmn.x,tmn.y),tmn.z),tf=min(min(tmx.x,tmx.y),tmx.z);\n' +
    ' if(tf<max(tn,0.)){o=vec4(0.);return;}\n' +
    ' float t=max(tn,0.),md=1e9,mt=t;bool hit=false;\n' +
    ' for(int i=0;i<200;i++){if(t>tf)break;float d=map(oo+od*t);if(d<md){md=d;mt=t;}if(d<.1){hit=true;break;}t+=max(d*.9,.15);}\n' +
    ' float pix=2.*uHalf.y/uRes.y,a=hit?1.:1.-smoothstep(0.,pix,md);\n' +
    ' if(a<=0.){o=vec4(0.);return;}\n' +
    ' vec3 p=oo+od*(hit?t:mt),n=nrm(p),bc=base(p);vec2 ab=parts(p);\n' +
    // Faces keep the flat logo colour; walls get one darker tone, barely
    // modulated by the light, so the thickness reads without banding.
    ' float face=smoothstep(-R,R,ab.y-ab.x);\n' +
    ' float dif=max(dot(n,Lo),0.);\n' +
    ' vec3 wall=bc*(.8+.08*dif/Lv.z);\n' +
    ' vec3 col=mix(wall,bc,face);\n' +
    ' o=vec4(col*a,a);}\n';

  function compile(gl, type, src) {
    var s = gl.createShader(type);
    gl.shaderSource(s, src);
    gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
    return s;
  }
  function texture(gl, unit, w, h, data) {
    var t = gl.createTexture();
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA16F, w, h, 0, gl.RGBA, gl.FLOAT, data);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  }

  // Keyframes: angle (degrees), hold (s), move to the next (s).
  var TIMELINES = {
    spin: [[0, 1.4, .9], [90, 1.2, 1.6], [270, 1.2, .9], [360, 0, 0]],
    swing: [[0, 1.4, .9], [90, 1.2, .9], [0, 1.4, .9], [-90, 1.2, .9], [0, 0, 0]]
  };
  function angleAt(tl, t) {
    for (var i = 0; i < tl.length - 1; i++) {
      var k = tl[i];
      if (t < k[1]) return k[0];
      t -= k[1];
      if (t < k[2]) return k[0] + (tl[i + 1][0] - k[0]) * (0.5 - 0.5 * Math.cos(Math.PI * t / k[2]));
      t -= k[2];
    }
    return tl[tl.length - 1][0];
  }
  function loopLength(tl) {
    return tl.reduce(function (s, k) { return s + k[1] + k[2]; }, 0);
  }

  function svgFallback() {
    return '<svg viewBox="28 44 604 474" aria-hidden="true"><g stroke-width="10" stroke-linejoin="round">' +
      LOGO.map(function (p) {
        var d = 'M' + p.pts.join(' ').replace(/(\d+) (\d+)/g, '$1 $2L').replace(/L$/, 'Z');
        return '<path d="' + d + '" fill="' + p.color + '" stroke="' + p.color + '"/>';
      }).join('') + '</g></svg>';
  }

  // One WebGL context for every logo on the page: browsers allow only about
  // 16, and the distance fields are computed and uploaded once. Each logo is
  // rendered into the corner of this offscreen canvas and copied to its own.
  var renderer = null;
  function getRenderer() {
    if (renderer) return renderer;
    var canvas = document.createElement('canvas');
    var gl = canvas.getContext('webgl2', { alpha: true, premultipliedAlpha: true, antialias: false });
    if (!gl) throw new Error('WebGL 2 is not available');
    var prog = gl.createProgram();
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VS));
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FS));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
    gl.useProgram(prog);
    texture(gl, 0, LOGO_TEX.w, LOGO_TEX.h, logoField());
    texture(gl, 1, FIG_TEX.w, FIG_TEX.h, figureField());
    var u = {};
    ['uLogo', 'uFig', 'uRes', 'uHalf', 'uAng', 'uSide', 'uDist', 'uCol0', 'uCol1', 'uCol2'].forEach(function (n) {
      u[n] = gl.getUniformLocation(prog, n);
    });
    gl.uniform1i(u.uLogo, 0);
    gl.uniform1i(u.uFig, 1);
    LOGO.forEach(function (p, i) { gl.uniform3fv(u['uCol' + i], hexToVec(p.color)); });
    renderer = { canvas: canvas, gl: gl, u: u };
    return renderer;
  }

  var reduceMotion = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : { matches: false };

  class LogoSpin extends HTMLElement {
    static get observedAttributes() { return ['motion', 'period', 'angle', 'perspective', 'label']; }

    constructor() {
      super();
      var root = this.attachShadow({ mode: 'open' });
      root.innerHTML = '<style>:host{display:inline-block;width:200px;aspect-ratio:' + (2 * BOX[0] + 24) + '/' + (2 * BOX[1] + 24) +
        ';vertical-align:middle;position:relative}canvas,svg{display:block;width:100%;height:100%}svg.front{position:absolute;inset:0;pointer-events:none}</style>';
      this._time = 0;
      this._last = 0;
      this._visible = true;
      this._raf = 0;
      this._frame = this._frame.bind(this);
    }

    connectedCallback() {
      this.setAttribute('role', 'img');
      this._applyLabel();
      if (!this._r && !this._fallback) this._init();
      var self = this;
      if ('IntersectionObserver' in window) {
        this._io = new IntersectionObserver(function (e) {
          self._visible = e[e.length - 1].isIntersecting;
          self._schedule();
        });
        this._io.observe(this);
      }
      if ('ResizeObserver' in window) {
        this._ro = new ResizeObserver(function () { self._resize(); self._schedule(true); });
        this._ro.observe(this);
      }
      this._onMotion = function () { self._schedule(true); };
      if (reduceMotion.addEventListener) reduceMotion.addEventListener('change', this._onMotion);
      this._schedule(true);
    }

    disconnectedCallback() {
      cancelAnimationFrame(this._raf);
      this._raf = 0;
      if (this._io) this._io.disconnect();
      if (this._ro) this._ro.disconnect();
      if (reduceMotion.removeEventListener) reduceMotion.removeEventListener('change', this._onMotion);
    }

    attributeChangedCallback(name) {
      if (name === 'label') this._applyLabel();
      if (name === 'motion') this._time = 0;
      this._schedule(true);
    }

    _applyLabel() {
      this.setAttribute('aria-label', this.getAttribute('label') || 'Lietuva 2.0 logotipas');
    }

    _init() {
      try {
        this._r = getRenderer();
        var canvas = document.createElement('canvas');
        this._ctx = canvas.getContext('2d');
        this.shadowRoot.appendChild(canvas);
        this._canvas = canvas;
        // The exact front is shown as SVG, crisper than the ray-marched edge.
        var template = document.createElement('template');
        template.innerHTML = svgFallback().replace('viewBox="28 44 604 474"', 'class="front" viewBox="18 34 624 494"');
        this.shadowRoot.appendChild(template.content);
        this._front = this.shadowRoot.querySelector('.front');
        this._resize();
      } catch (err) {
        if (window.console) console.warn('lt2-logo-spin: showing the flat logo.', err);
        this._fallback = true;
        var fallback = document.createElement('template');
        fallback.innerHTML = svgFallback();
        this.shadowRoot.appendChild(fallback.content);
      }
    }

    _resize() {
      if (!this._canvas) return;
      var dpr = Math.min(window.devicePixelRatio || 1, 2);
      var w = Math.max(1, Math.round(this.clientWidth * dpr)), h = Math.max(1, Math.round(this.clientHeight * dpr));
      if (this._canvas.width !== w || this._canvas.height !== h) {
        this._canvas.width = w;
        this._canvas.height = h;
      }
    }

    _timeline() {
      return TIMELINES[this.getAttribute('motion')] || TIMELINES.spin;
    }

    _animated() {
      return !this._fallback && !this.hasAttribute('angle') && !reduceMotion.matches;
    }

    // force: draw one frame even when not animating (attributes or size changed).
    _schedule(force) {
      if (this._raf || !this.isConnected) return;
      if (this._animated() && this._visible) {
        this._last = 0;
        this._raf = requestAnimationFrame(this._frame);
      } else if (force && !this._fallback) {
        this._raf = requestAnimationFrame(this._frame);
      }
    }

    _frame(now) {
      this._raf = 0;
      var tl = this._timeline(), animated = this._animated();
      if (animated) {
        if (this._last) this._time += Math.min(now - this._last, 100) / 1000;
        this._last = now;
      }
      var period = parseFloat(this.getAttribute('period'));
      var len = loopLength(tl), scale = period > 0 ? len / period : 1;
      var deg = this.hasAttribute('angle') ? parseFloat(this.getAttribute('angle')) || 0
        : animated ? angleAt(tl, (this._time * scale) % len) : 0;
      this.currentAngle = deg;
      this._draw(deg * Math.PI / 180);
      if (animated && this._visible) this._raf = requestAnimationFrame(this._frame);
    }

    _draw(ang) {
      if (!this._r) return;
      var gl = this._r.gl, u = this._r.u, shared = this._r.canvas;
      if (gl.isContextLost()) return;
      var w = this._canvas.width, h = this._canvas.height;
      if (shared.width < w || shared.height < h) {
        shared.width = Math.max(shared.width, w);
        shared.height = Math.max(shared.height, h);
      }
      var persp = this.getAttribute('perspective');
      var dist = persp === null || persp === '' ? 0 : Math.max(0, parseFloat(persp) || 0);
      var hx = BOX[0] + 12, hy = BOX[1] + 12;
      if (w / h > hx / hy) hx = hy * w / h; else hy = hx * h / w;
      // Man on 0–180°, woman on 180–360°. The switch is instant, at the exact
      // back and front, where the depth cannot show. (A gradual blend would
      // leave rows with no material mid-way where the two outlines don't
      // overlap, and the logo would lose a strip.)
      var degrees = ((ang * 180 / Math.PI) % 360 + 360) % 360;
      var front = Math.min(degrees, 360 - degrees);
      this._front.style.opacity = front < 0.01 ? '1' : '0';
      this._canvas.style.opacity = front < 0.01 ? '0' : '1';
      gl.viewport(0, 0, w, h);
      gl.uniform2f(u.uRes, w, h);
      gl.uniform2f(u.uHalf, hx, hy);
      gl.uniform1f(u.uAng, ang);
      gl.uniform1f(u.uSide, degrees >= 180 ? 1 : 0);
      gl.uniform1f(u.uDist, dist);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
      // The viewport is the bottom-left corner in GL, the bottom rows of the image.
      this._ctx.clearRect(0, 0, w, h);
      this._ctx.drawImage(shared, 0, shared.height - h, w, h, 0, 0, w, h);
    }
  }

  customElements.define('lt2-logo-spin', LogoSpin);
})();
