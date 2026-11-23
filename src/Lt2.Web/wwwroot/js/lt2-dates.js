/*
 * The calendar button beside a typed date field (component Lt2.Web.Client/Components/DatePick.razor; owner, 2026-11-23).
 * The field stays a text field in yyyy-MM-dd; the button opens the browser's own date picker and writes the picked day back
 * into the text field as yyyy-MM-dd. No calendar of our own, nothing stored, nothing in the address.
 *
 * A button is rendered hidden: without this script, or in a browser without HTMLInputElement.showPicker(), it would be a dead
 * control, so it stays hidden there (as the theme switch does). Buttons an island or an enhanced navigation renders later
 * are shown by an observer; the one click listener sits on the document, as in lt2-hints.js, so nothing is wired per button.
 *
 * On a press a hidden <input type="date"> is made inside the same wrapper (.date-row), laid exactly over the text field
 * (opacity 0, no pointer events, out of the tab order and the accessibility tree), so the browser's popup opens at the
 * field. Its value is always ISO (yyyy-MM-dd), so the result does not depend on the browser's language or region. When a day
 * is picked, the text field gets it, "input" and "change" fire on the text field (bubbling, so Blazor's @bind and any
 * validation see it) and the focus goes to the text field. Positions are set through the CSSOM (element.style), which the
 * Content-Security-Policy (style-src 'self') allows; nothing inline is written into the markup.
 */
(function () {
  'use strict';

  if (typeof HTMLInputElement === 'undefined' || typeof HTMLInputElement.prototype.showPicker !== 'function') return;

  var ISO = /^\d{4}-\d{2}-\d{2}$/;

  function reveal() {
    var buttons = document.querySelectorAll('[data-date-pick][hidden]');
    for (var i = 0; i < buttons.length; i++) buttons[i].hidden = false;
  }

  function nativeOf(wrapper, id) {
    for (var i = 0; i < wrapper.children.length; i++) {
      var child = wrapper.children[i];
      if (child.tagName === 'INPUT' && child.classList.contains('date-pick__native') && child.getAttribute('data-date-for') === id) return child;
    }
    var made = document.createElement('input');
    made.type = 'date';
    made.className = 'date-pick__native';
    made.tabIndex = -1;
    made.setAttribute('aria-hidden', 'true');
    made.setAttribute('data-date-for', id);
    made.setAttribute('autocomplete', 'off');
    made.addEventListener('change', function () {
      var text = document.getElementById(id);
      if (!text || !made.value) return;
      text.value = made.value;
      text.dispatchEvent(new Event('input', { bubbles: true }));
      text.dispatchEvent(new Event('change', { bubbles: true }));
      text.focus();
    });
    wrapper.appendChild(made);
    return made;
  }

  document.addEventListener('click', function (event) {
    var button = event.target instanceof Element ? event.target.closest('[data-date-pick]') : null;
    if (!button || button.disabled) return;
    var id = button.getAttribute('data-date-pick');
    var text = id ? document.getElementById(id) : null;
    var wrapper = button.parentElement;
    if (!text || !wrapper || text.disabled || text.readOnly) return;
    var native = nativeOf(wrapper, id);
    native.style.left = text.offsetLeft + 'px';
    native.style.top = text.offsetTop + 'px';
    native.style.width = text.offsetWidth + 'px';
    native.style.height = text.offsetHeight + 'px';
    native.value = ISO.test(text.value.trim()) ? text.value.trim() : '';
    try {
      native.showPicker();
    } catch (e) {
      /* No user activation, or the browser refuses: the typed field still works. */
    }
  });

  // A hidden attribute put back by an enhanced navigation's patch, or a button rendered later by an island: show it again.
  var queued = false;
  function revealSoon() {
    if (queued) return;
    queued = true;
    requestAnimationFrame(function () {
      queued = false;
      reveal();
    });
  }

  function start() {
    reveal();
    new MutationObserver(revealSoon).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden'] });
  }

  if (document.body) start();
  else document.addEventListener('DOMContentLoaded', start);
})();
