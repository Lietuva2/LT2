/*
 * Design 8.15: a re-render must not close what the person opened. A <details class="more"> works without script, so a
 * person may open one on the prerendered page before its island has started; when WebAssembly starts, Blazor replaces the
 * prerendered markup and the new <details> comes up closed. This remembers which disclosures the person opened or closed
 * (by id, or by position among the page's disclosures when one has no id) and gives the replacement the same state.
 * It never opens anything the person did not open, and does nothing on a page without disclosures.
 */
(function () {
  'use strict';

  var opened = new Set();

  function all() {
    return document.querySelectorAll('details.more');
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
    if (!(details instanceof HTMLDetailsElement) || !details.classList.contains('more')) return;
    if (details.open) opened.add(keyOf(details));
    else opened.delete(keyOf(details));
  }, true);

  function hasDisclosure(node) {
    return node.nodeType === 1 && (node.matches('details.more') || node.querySelector('details.more') !== null);
  }

  new MutationObserver(function (records) {
    if (opened.size === 0) return;
    var added = false;
    for (var i = 0; i < records.length && !added; i++) {
      for (var j = 0; j < records[i].addedNodes.length; j++) {
        if (hasDisclosure(records[i].addedNodes[j])) {
          added = true;
          break;
        }
      }
    }
    if (!added) return;
    var list = all();
    for (var k = 0; k < list.length; k++) {
      if (!list[k].open && opened.has(keyOf(list[k]))) list[k].open = true;
    }
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
