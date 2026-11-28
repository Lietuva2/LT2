/*
 * Following a link shows that the next page is on its way, and the page goes to its top only once it has arrived (the
 * owner, 2026-11-28: "when I scroll down the list and select a bill, the page first scrolls to the top and then loads a
 * details page; if that takes a moment, it gives no clue the page is loading"). Blazor's enhanced navigation fetches the
 * next page and patches it in, but on a link to another page it scrolls to the top at the moment of the press, before
 * the request has even gone out, and shows nothing while it waits. This script, for every enhanced load (a link, a
 * form with data-enhance, Back and Forward):
 *   - shows the bar at the top of the screen (#nav-progress, App.razor) once the wait passes SHOW_DELAY, so a fast page
 *     shows nothing;
 *   - on a link to another page, puts the reader back where they stood at once, in the same event, before anything is
 *     painted, and goes to the top when Blazor says the page has been patched in (the link's fragment, if it has one, is
 *     Blazor's to scroll to). A link within the same page, a form and Back or Forward keep Blazor's own scrolling
 *     (lt2-return.js holds the place on Back and Forward).
 * Nothing is stored. Without Blazor (no script, a full page load) nothing here runs.
 */
(function () {
  'use strict';

  if (!window.Blazor || typeof window.Blazor.addEventListener !== 'function') return;

  var SHOW_DELAY = 150;
  // A load that never reports its end (a failed request) does not leave the bar on for ever.
  var GIVE_UP = 20000;

  var showTimer = 0;
  var giveUpTimer = 0;
  var toTop = false;
  var press = null;

  function bar() {
    return document.getElementById('nav-progress');
  }

  function pageOf(href) {
    var url = new URL(href, document.baseURI);
    return url.origin + url.pathname + url.search;
  }

  var shown = pageOf(window.location.href);

  function start() {
    shown = pageOf(window.location.href);
    window.clearTimeout(showTimer);
    window.clearTimeout(giveUpTimer);
    showTimer = window.setTimeout(function () {
      var el = bar();
      if (el) el.classList.add('nav-progress--on');
    }, SHOW_DELAY);
    giveUpTimer = window.setTimeout(stop, GIVE_UP);
  }

  function stop() {
    window.clearTimeout(showTimer);
    window.clearTimeout(giveUpTimer);
    var el = bar();
    if (el) el.classList.remove('nav-progress--on');
  }

  // As Blazor decides (NavigationEnhancement.ts): an element inside data-enhance-nav="false" is left to the browser.
  function enhancedLink(target) {
    if (!(target instanceof Element)) return null;
    var link = target.closest('a[href]');
    if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')) return null;
    var off = link.closest('[data-enhance-nav]');
    if (off && off.getAttribute('data-enhance-nav') === 'false') return null;
    return link;
  }

  // Capture on the window runs before Blazor's listener on the document; the bubbling listener on the window runs after it.
  window.addEventListener('click', function () {
    press = { href: window.location.href, y: window.scrollY };
  }, true);

  window.addEventListener('click', function (event) {
    var before = press;
    press = null;
    if (!before || !event.defaultPrevented || window.location.href === before.href) return;
    var link = enhancedLink(event.target);
    if (!link || new URL(link.href, document.baseURI).href !== window.location.href) return;
    var from = new URL(before.href);
    var to = new URL(window.location.href);
    if (from.pathname === to.pathname && from.search === to.search) return; // a fragment on the same page
    start();
    if (from.pathname !== to.pathname) {
      toTop = to.hash.length <= 1;
      if (Math.abs(window.scrollY - before.y) > 1) window.scrollTo(0, before.y);
    }
  });

  window.addEventListener('submit', function (event) {
    var form = event.target;
    if (!event.defaultPrevented || !(form instanceof HTMLFormElement) || !form.hasAttribute('data-enhance')) return;
    if (form.getAttribute('data-enhance') === 'false') return;
    toTop = false;
    start();
  });

  window.addEventListener('popstate', function () {
    if (pageOf(window.location.href) === shown) return; // a fragment on the same page
    toTop = false;
    start();
  });

  window.Blazor.addEventListener('enhancedload', function () {
    shown = pageOf(window.location.href);
    stop();
    if (toTop) {
      toTop = false;
      window.scrollTo(0, 0);
    }
  });
})();
