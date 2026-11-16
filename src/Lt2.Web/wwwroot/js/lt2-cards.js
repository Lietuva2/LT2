/*
 * Design 8.25.4, the name beside a text: for a member with a public profile the name is a link to /nariai/{id} (it works
 * without this script; on touch a press follows it). With a mouse over it, or keyboard focus on it, a small card shows the
 * name and the first lines of "Apie mane", after a short delay, beside the name, inside the screen. Escape closes it. The
 * card's text is carried once per person per page, in a <template id="card-{id}"> the link names in data-card; nothing is
 * fetched. Listeners sit on the document, as in lt2-hints.js, so links an island renders later work too. Positions are set
 * through the CSSOM, which the Content-Security-Policy allows.
 */
(function () {
  'use strict';

  if (typeof HTMLElement === 'undefined' || !HTMLElement.prototype.hasOwnProperty('popover')) return;

  var OPEN_DELAY = 350;
  var CLOSE_DELAY = 250;
  var MARGIN = 8;
  var GAP = 6;
  var panel = null;
  var current = null;
  var openTimer = 0;
  var closeTimer = 0;

  function linkOf(node) {
    return node instanceof Element ? node.closest('a[data-card]') : null;
  }

  function ensurePanel() {
    if (panel) return panel;
    panel = document.createElement('div');
    panel.className = 'card-pop';
    panel.setAttribute('popover', 'manual');
    panel.setAttribute('role', 'tooltip');
    panel.id = 'lt2-card';
    panel.addEventListener('mouseenter', function () { clearTimeout(closeTimer); });
    panel.addEventListener('mouseleave', scheduleClose);
    document.body.appendChild(panel);
    return panel;
  }

  function place(link) {
    var r = link.getBoundingClientRect();
    var vw = document.documentElement.clientWidth;
    var vh = document.documentElement.clientHeight;
    var p = panel.getBoundingClientRect();
    var left = Math.min(Math.max(MARGIN, r.left), vw - p.width - MARGIN);
    var top = r.bottom + GAP;
    if (top + p.height > vh - MARGIN && r.top - GAP - p.height > MARGIN) top = r.top - GAP - p.height;
    panel.style.left = Math.max(MARGIN, left) + 'px';
    panel.style.top = Math.max(MARGIN, top) + 'px';
  }

  function open(link) {
    var template = document.getElementById(link.getAttribute('data-card'));
    if (!(template instanceof HTMLTemplateElement)) return;
    ensurePanel();
    panel.replaceChildren(template.content.cloneNode(true));
    current = link;
    link.setAttribute('aria-describedby', panel.id);
    if (!panel.matches(':popover-open')) panel.showPopover();
    place(link);
  }

  function close() {
    clearTimeout(openTimer);
    clearTimeout(closeTimer);
    if (current) current.removeAttribute('aria-describedby');
    current = null;
    if (panel && panel.matches(':popover-open')) panel.hidePopover();
  }

  function scheduleOpen(link) {
    clearTimeout(closeTimer);
    clearTimeout(openTimer);
    openTimer = setTimeout(function () { open(link); }, OPEN_DELAY);
  }

  function scheduleClose() {
    clearTimeout(openTimer);
    clearTimeout(closeTimer);
    closeTimer = setTimeout(close, CLOSE_DELAY);
  }

  document.addEventListener('pointerover', function (e) {
    if (e.pointerType !== 'mouse') return;
    var link = linkOf(e.target);
    if (link) scheduleOpen(link);
  });

  document.addEventListener('pointerout', function (e) {
    if (e.pointerType !== 'mouse') return;
    if (linkOf(e.target)) scheduleClose();
  });

  document.addEventListener('focusin', function (e) {
    var link = linkOf(e.target);
    if (link) scheduleOpen(link); else if (current) close();
  });

  document.addEventListener('focusout', function (e) {
    if (linkOf(e.target)) scheduleClose();
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && current) close();
  });

  window.addEventListener('scroll', function () { if (current) close(); }, { passive: true });
  window.addEventListener('resize', function () { if (current) close(); });
})();
