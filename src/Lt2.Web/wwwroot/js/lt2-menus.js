/*
 * Design 8.16.1, the action row's "⋯" (component Lt2.Web.Client/Components/MoreMenu.razor), and design 8.3's moderator's
 * "Kiti veiksmai" (Lt2.Web.Client/Appeals/ModerationMenu.razor), placed the same way. Without this script the menu is
 * the browser's own popover: a press on "⋯" opens it in the middle of the screen, a press elsewhere or Escape closes it.
 * This script only places an open menu beside its "⋯": under it, its right edge at the trigger's right edge, above it when
 * there is no room below, always inside the screen with a margin; placed again on scroll and resize while open, and closed
 * when "⋯" has left the screen. Listeners sit on the document, so a menu an island renders later works with nothing to wire.
 * Positions are set through the CSSOM (element.style), which the Content-Security-Policy (style-src 'self') allows.
 */
(function () {
  'use strict';

  if (typeof HTMLElement === 'undefined' || !HTMLElement.prototype.hasOwnProperty('popover')) return;

  var MARGIN = 8;
  var GAP = 4;

  // The button that opens the menu ("⋯", or a moderator's "Kiti veiksmai", design 8.3); its items also name the menu, to close it.
  function triggerOf(menu) {
    return menu.id ? document.querySelector('[popovertarget="' + CSS.escape(menu.id) + '"]:is(.act__more, .menu__trigger)') : null;
  }

  function place(menu) {
    var trigger = triggerOf(menu);
    if (!trigger || !menu.matches(':popover-open')) return;
    var r = trigger.getBoundingClientRect();
    var vw = document.documentElement.clientWidth;
    var vh = window.innerHeight;
    if (r.bottom < 0 || r.top > vh) {
      menu.hidePopover();
      return;
    }
    menu.classList.add('menu--placed');
    var w = menu.offsetWidth;
    var h = menu.offsetHeight;
    // "⋯" ends its row, so its menu ends at its right edge; "Kiti veiksmai" (menu--start) starts its menu at its left edge.
    var wanted = menu.classList.contains('menu--start') ? r.left : r.right - w;
    var left = Math.max(MARGIN, Math.min(wanted, vw - w - MARGIN));
    var top = r.bottom + GAP;
    if (top + h > vh - MARGIN && r.top - GAP - h >= MARGIN) top = r.top - GAP - h;
    top = Math.max(MARGIN, Math.min(top, vh - h - MARGIN));
    menu.style.left = left + 'px';
    menu.style.top = top + 'px';
  }

  // "toggle" does not bubble: listen while it travels down.
  document.addEventListener('toggle', function (event) {
    var menu = event.target;
    if (menu instanceof Element && menu.classList.contains('menu') && event.newState === 'open') place(menu);
  }, true);

  var frame = 0;
  function placeOpen() {
    if (frame) return;
    frame = requestAnimationFrame(function () {
      frame = 0;
      document.querySelectorAll('.menu:popover-open').forEach(place);
    });
  }

  window.addEventListener('scroll', placeOpen, { capture: true, passive: true });
  window.addEventListener('resize', placeOpen);
})();
