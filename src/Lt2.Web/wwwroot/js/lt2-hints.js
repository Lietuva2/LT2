/*
 * Design 8.15, "A hint": the short answer without leaving the page (component Lt2.Web.Client/Components/Hint.razor).
 * Without this script a hint is the browser's own popover: a press on the trigger opens it in the middle of the screen,
 * a press elsewhere or Escape closes it. This script adds:
 *  - opening on hover (a mouse only) and on keyboard focus, after a short delay;
 *  - a press or a tap opens it at once and keeps it open; a second press on the trigger closes it;
 *  - closing when the pointer and the focus have left both the trigger and the panel (a pressed one stays open until
 *    Escape, a press elsewhere, a second press or the focus moving elsewhere), with a short grace so the pointer can
 *    travel into the panel and use its link;
 *  - Escape closes it and gives the focus back to the trigger when the focus was in the hint;
 *  - the place beside the trigger: under it, above it when there is no room below, always inside the screen with a
 *    margin; placed again on scroll and resize while open, and closed when its trigger has left the screen.
 * Listeners sit on the document, so a hint rendered later (Blazor's enhanced navigation, an island replacing its markup)
 * works with nothing to wire. Positions are set through the CSSOM (element.style), which the Content-Security-Policy
 * (style-src 'self') allows; nothing inline is written into the markup.
 */
(function () {
  'use strict';

  if (typeof HTMLElement === 'undefined' || !HTMLElement.prototype.hasOwnProperty('popover')) return;

  var OPEN_DELAY = 350;
  var CLOSE_DELAY = 300;
  var MARGIN = 8;
  var GAP = 6;

  var states = new WeakMap();

  function stateOf(hint) {
    var s = states.get(hint);
    if (!s) {
      s = { pinned: false, pointer: false, noFocusOpen: false, atPress: null, openTimer: 0, closeTimer: 0 };
      states.set(hint, s);
    }
    return s;
  }

  function hintOf(node) {
    return node instanceof Element ? node.closest('.hint') : null;
  }

  function triggerOf(hint) { return hint.querySelector('.hint__trigger'); }

  function panelOf(hint) { return hint.querySelector('.hint__panel'); }

  function isOpen(panel) { return !!panel && panel.matches(':popover-open'); }

  function clearTimers(s) {
    clearTimeout(s.openTimer);
    clearTimeout(s.closeTimer);
    s.openTimer = 0;
    s.closeTimer = 0;
  }

  function place(hint) {
    var trigger = triggerOf(hint);
    var panel = panelOf(hint);
    if (!trigger || !isOpen(panel)) return;
    var r = trigger.getBoundingClientRect();
    var vw = document.documentElement.clientWidth;
    var vh = window.innerHeight;
    if (r.bottom < 0 || r.top > vh || r.right < 0 || r.left > vw) {
      close(hint);
      return;
    }
    panel.classList.add('hint__panel--placed');
    var w = panel.offsetWidth;
    var h = panel.offsetHeight;
    var left = Math.max(MARGIN, Math.min(r.left, vw - w - MARGIN));
    var top = r.bottom + GAP;
    if (top + h > vh - MARGIN && r.top - GAP - h >= MARGIN) top = r.top - GAP - h;
    top = Math.max(MARGIN, Math.min(top, vh - h - MARGIN));
    panel.style.left = left + 'px';
    panel.style.top = top + 'px';
  }

  function open(hint) {
    var panel = panelOf(hint);
    if (!panel || !hint.isConnected || isOpen(panel)) return;
    try {
      // The trigger as the source: a press on it is then not a press elsewhere (browsers without the option ignore it).
      panel.showPopover({ source: triggerOf(hint) });
    } catch (e) {
      return;
    }
    place(hint);
  }

  function close(hint) {
    var s = stateOf(hint);
    clearTimers(s);
    s.pinned = false;
    var panel = panelOf(hint);
    if (isOpen(panel)) panel.hidePopover();
  }

  function focusInside(hint) {
    return hint.contains(document.activeElement);
  }

  function closeLater(hint) {
    var s = stateOf(hint);
    clearTimeout(s.closeTimer);
    s.closeTimer = setTimeout(function () {
      s.closeTimer = 0;
      if (!s.pinned && !s.pointer && !focusInside(hint)) close(hint);
    }, CLOSE_DELAY);
  }

  function openLater(hint) {
    var s = stateOf(hint);
    clearTimeout(s.closeTimer);
    if (isOpen(panelOf(hint)) || s.openTimer) return;
    s.openTimer = setTimeout(function () {
      s.openTimer = 0;
      if (s.pointer || focusInside(hint)) open(hint);
    }, OPEN_DELAY);
  }

  // What the hint was when the press began: a browser that does not know the trigger as the panel's source closes the
  // panel at the press itself (a press elsewhere), before the click arrives.
  document.addEventListener('pointerdown', function (event) {
    var trigger = event.target instanceof Element ? event.target.closest('.hint__trigger') : null;
    var hint = trigger ? hintOf(trigger) : null;
    if (!hint) return;
    var s = stateOf(hint);
    s.atPress = isOpen(panelOf(hint)) ? (s.pinned ? 'pinned' : 'shown') : 'closed';
  }, true);

  // A press or a tap (and Enter or Space on the trigger): open at once and keep open; on an open one that was only shown
  // by hover or focus, keep it open; on one already kept open, close it. The browser's own popovertarget toggle is
  // cancelled, so the place is set before the panel is first painted.
  document.addEventListener('click', function (event) {
    var trigger = event.target instanceof Element ? event.target.closest('.hint__trigger') : null;
    var hint = trigger ? hintOf(trigger) : null;
    if (!hint) return;
    event.preventDefault();
    var s = stateOf(hint);
    clearTimers(s);
    // A click from a pointer has a detail of 1 or more; Enter or Space on the trigger has 0 and no press before it.
    var was = (event.detail > 0 && s.atPress) || (isOpen(panelOf(hint)) ? (s.pinned ? 'pinned' : 'shown') : 'closed');
    s.atPress = null;
    if (was === 'pinned') {
      close(hint);
      return;
    }
    open(hint);
    s.pinned = isOpen(panelOf(hint));
  });

  document.addEventListener('pointerover', function (event) {
    if (event.pointerType !== 'mouse') return;
    var hint = hintOf(event.target);
    if (!hint) return;
    var s = stateOf(hint);
    if (s.pointer) return;
    s.pointer = true;
    openLater(hint);
  });

  document.addEventListener('pointerout', function (event) {
    if (event.pointerType !== 'mouse') return;
    var hint = hintOf(event.target);
    if (!hint || (event.relatedTarget instanceof Node && hint.contains(event.relatedTarget))) return;
    var s = stateOf(hint);
    s.pointer = false;
    clearTimeout(s.openTimer);
    s.openTimer = 0;
    if (!s.pinned) closeLater(hint);
  });

  document.addEventListener('focusin', function (event) {
    var hint = hintOf(event.target);
    if (!hint) return;
    var s = stateOf(hint);
    clearTimeout(s.closeTimer);
    // Only a keyboard focus opens it (a press focuses the button too, and opens it by itself).
    if (event.target === triggerOf(hint) && !s.noFocusOpen && event.target.matches(':focus-visible')) openLater(hint);
  });

  document.addEventListener('focusout', function (event) {
    var hint = hintOf(event.target);
    if (!hint) return;
    var next = event.relatedTarget;
    if (next instanceof Node && hint.contains(next)) return;
    var s = stateOf(hint);
    s.noFocusOpen = false;
    clearTimeout(s.openTimer);
    s.openTimer = 0;
    // The focus going nowhere (another window, a press on plain text) leaves it as it is; the press closes it itself.
    if (!(next instanceof Element)) return;
    if (s.pinned) close(hint);
    else if (!s.pointer) closeLater(hint);
  });

  // Escape: close the open hint (only it, not the feedback panel too) and give the focus back to its trigger.
  document.addEventListener('keydown', function (event) {
    if (event.key !== 'Escape') return;
    var panel = document.querySelector('.hint__panel:popover-open');
    var hint = panel ? hintOf(panel) : null;
    if (!hint) return;
    var giveBack = focusInside(hint);
    close(hint);
    if (giveBack) {
      stateOf(hint).noFocusOpen = true;
      triggerOf(hint).focus();
    }
    event.preventDefault();
    event.stopPropagation();
  }, true);

  // "toggle" does not bubble: listen while it travels down. A panel closed by the browser (a press elsewhere, another
  // hint opening) forgets it was kept open; one opened by the browser without this script's help is placed.
  document.addEventListener('toggle', function (event) {
    var panel = event.target;
    if (!(panel instanceof Element) || !panel.classList.contains('hint__panel')) return;
    var hint = hintOf(panel);
    if (!hint) return;
    if (event.newState === 'closed') {
      var s = stateOf(hint);
      clearTimers(s);
      s.pinned = false;
    } else {
      place(hint);
    }
  }, true);

  var frame = 0;
  function placeOpen() {
    if (frame) return;
    frame = requestAnimationFrame(function () {
      frame = 0;
      var panel = document.querySelector('.hint__panel:popover-open');
      var hint = panel ? hintOf(panel) : null;
      if (hint) place(hint);
    });
  }

  window.addEventListener('scroll', placeOpen, { capture: true, passive: true });
  window.addEventListener('resize', placeOpen);
})();
