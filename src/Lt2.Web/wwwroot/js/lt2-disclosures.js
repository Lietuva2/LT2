/*
 * Design 8.15: a re-render must not close what the person opened. A <details class="more"> (and an action row's "Aptarimas",
 * <details class="discuss">, design 8.16.1) works without script, so a
 * person may open one on the prerendered page before its island has started; when WebAssembly starts, Blazor replaces the
 * prerendered markup and the new <details> comes up closed. This remembers which disclosures the person opened or closed
 * (by id, or by position among the page's disclosures when one has no id) and gives the replacement the same state.
 * It never opens anything the person did not open, and does nothing on a page without disclosures.
 */
(function () {
  'use strict';

  var opened = new Set();
  var SELECTOR = 'details.more, details.discuss';

  function all() {
    return document.querySelectorAll(SELECTOR);
  }

  function keyOf(details) {
    // The page's path is part of the key, so a page reached by enhanced navigation starts with nothing opened.
    var page = window.location.pathname + '|';
    if (details.id) return page + 'id:' + details.id;
    return page + 'at:' + Array.prototype.indexOf.call(all(), details);
  }

  // "toggle" does not bubble: listen while it travels down.
  document.addEventListener('toggle', function (event) {
    var details = event.target;
    if (!(details instanceof HTMLDetailsElement) || !details.matches(SELECTOR)) return;
    if (details.open) opened.add(keyOf(details));
    else opened.delete(keyOf(details));
  }, true);

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
      if (list[i].open && list[i].id) opened.add(window.location.pathname + '|id:' + list[i].id);
    }
  }

  new MutationObserver(function (records) {
    var added = false;
    for (var i = 0; i < records.length; i++) {
      for (var r = 0; r < records[i].removedNodes.length; r++) {
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
