/*
 * Back and Forward keep the reader's place (the owner, 2026-11-17: "following a link from the bill's page and pressing Back
 * sometimes lands scrolled to the bottom"). Blazor's enhanced navigation does not load the page again on Back: it fetches it
 * and patches the page that is there (the one just left) into it. The browser restores the scroll position at once, on the
 * old content, and while the patch moves nodes around, the browser's scroll anchoring follows whichever node it had picked,
 * which may end up far down the bill's page. This keeps, per address, where the reader last stood (in memory only: nothing
 * is stored in the browser, design 8.22.1), and on Back or Forward holds that place while the page is patched: scroll
 * anchoring off, and back to the place whenever anything moves it. Once Blazor says the page has loaded it goes there once
 * more and gives scroll anchoring back, so islands that grow afterwards keep the reader's place as usual. A person who
 * scrolls meanwhile wins. Without Blazor (no script, a full page load) the browser's own restoring is untouched.
 */
(function () {
  'use strict';

  var places = new Map();
  var holding = null;

  function remember() {
    if (holding === null) places.set(window.location.href, window.scrollY);
    else hold();
  }

  function hold() {
    if (holding === null) return;
    if (Math.abs(window.scrollY - holding) > 1) window.scrollTo(0, holding);
  }

  function release() {
    if (holding === null) return;
    hold();
    holding = null;
    document.documentElement.style.removeProperty('overflow-anchor');
    remember();
  }

  window.addEventListener('scroll', remember, { passive: true });

  window.addEventListener('popstate', function () {
    var place = places.get(window.location.href);
    if (place === undefined) return;
    holding = place;
    document.documentElement.style.setProperty('overflow-anchor', 'none');
    hold();
    // A patch that never reports its end (a failed request, a full load) does not hold the page for ever.
    window.setTimeout(release, 4000);
  });

  // The reader's own scrolling ends the hold at once.
  ['wheel', 'touchstart', 'keydown', 'pointerdown'].forEach(function (name) {
    window.addEventListener(name, function () {
      if (holding === null) return;
      holding = null;
      document.documentElement.style.removeProperty('overflow-anchor');
    }, { passive: true, capture: true });
  });

  new MutationObserver(hold).observe(document.documentElement, { childList: true, subtree: true });

  // Loaded after blazor.web.js (App.razor), as lt2-theme-blazor.js is.
  if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
    window.Blazor.addEventListener('enhancedload', function () {
      // One frame more for the layout the patch left.
      window.requestAnimationFrame(release);
    });
  }
})();
