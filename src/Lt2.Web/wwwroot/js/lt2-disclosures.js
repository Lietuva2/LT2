/*
 * Design 8.15: a re-render must not close what the person opened. A <details class="more"> (and an action row's "Aptarimas",
 * <details class="discuss">, design 8.16.1) works without script, so a
 * person may open one on the prerendered page before its island has started; when WebAssembly starts, Blazor replaces the
 * prerendered markup and the new <details> comes up closed. This remembers which disclosures the person opened or closed
 * (by id, or by position among the page's disclosures when one has no id) and gives the replacement the same state.
 * It never opens anything the person did not open, and does nothing on a page without disclosures. The memory is of the
 * page being read only: it is emptied whenever the page's path changes (a link followed, Back or Forward by Blazor's
 * enhanced navigation), so coming back to a page opens nothing of an earlier visit (owner, 2026-11-17: Back to a bill's page
 * reopened an "Aptarimas" while the page was patched, which moved the page under the reader).
 */
(function () {
  'use strict';

  var opened = new Set();
  var page = window.location.pathname;
  var SELECTOR = 'details.more, details.discuss';

  function all() {
    return document.querySelectorAll(SELECTOR);
  }

  // Another page than the one remembered for: nothing of the old one counts.
  function samePage() {
    if (window.location.pathname === page) return;
    page = window.location.pathname;
    opened.clear();
  }

  function keyOf(details) {
    if (details.id) return 'id:' + details.id;
    return 'at:' + Array.prototype.indexOf.call(all(), details);
  }

  // "toggle" does not bubble: listen while it travels down.
  document.addEventListener('toggle', function (event) {
    var details = event.target;
    if (!(details instanceof HTMLDetailsElement) || !details.matches(SELECTOR)) return;
    samePage();
    if (details.open) opened.add(keyOf(details));
    else opened.delete(keyOf(details));
  }, true);

  window.addEventListener('popstate', samePage);

  function hasDisclosure(node) {
    return node.nodeType === 1 && (node.matches(SELECTOR) || node.querySelector(SELECTOR) !== null);
  }

  // A disclosure taken out of the page while open counts as opened, whether or not its "toggle" was seen: the event is
  // queued, and when the island starts in that instant it fires on an element that is no longer in the document. Only
  // one with an id can be recognised once it is out of the page.
  function rememberRemoved(node) {
    if (node.nodeType !== 1) return;
    var list = node.matches(SELECTOR) ? [node] : node.querySelectorAll(SELECTOR);
    for (var i = 0; i < list.length; i++) {
      if (list[i].open && list[i].id) opened.add('id:' + list[i].id);
    }
  }

  new MutationObserver(function (records) {
    // A page patched in by enhanced navigation is a new page: what is taken out of the old one is not remembered.
    var moved = window.location.pathname !== page;
    samePage();
    var added = false;
    for (var i = 0; i < records.length; i++) {
      for (var r = 0; r < records[i].removedNodes.length && !moved; r++) {
        rememberRemoved(records[i].removedNodes[r]);
      }
      for (var j = 0; j < records[i].addedNodes.length && !added; j++) {
        if (hasDisclosure(records[i].addedNodes[j])) added = true;
      }
    }
    if (!added || opened.size === 0) return;
    var list = all();
    for (var k = 0; k < list.length; k++) {
      if (!list[k].open && opened.has(keyOf(list[k]))) list[k].open = true;
    }
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
