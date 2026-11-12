/*
 * LT2 theme switch: system, light, dark.
 *
 * Load it in <head> without "defer", after the stylesheets, so a stored choice is applied before the page is painted.
 * Put a <button class="mark" type="button" data-theme-switch hidden></button> where the switch should be. The button
 * stays hidden without JavaScript, where the page simply follows the system setting, so no page shows a dead control.
 *
 * The choice is kept in localStorage under "lt2-theme" ("light" or "dark"; nothing stored means "system"). It is a
 * display preference, not personal data, and is never sent to the server.
 *
 * Fires "lt2-theme" on document after every change, for anything that draws with colours read from the page.
 */
(function () {
  'use strict';

  var KEY = 'lt2-theme';
  var MODES = ['system', 'light', 'dark'];
  var LABELS = {
    lt: { system: 'Tema: automatinė', light: 'Tema: šviesi', dark: 'Tema: tamsi' },
    en: { system: 'Theme: system', light: 'Theme: light', dark: 'Theme: dark' }
  };
  var root = document.documentElement;

  function stored() {
    try {
      var value = window.localStorage.getItem(KEY);
      return value === 'light' || value === 'dark' ? value : 'system';
    } catch (e) {
      return 'system';
    }
  }

  function store(mode) {
    try {
      if (mode === 'system') window.localStorage.removeItem(KEY);
      else window.localStorage.setItem(KEY, mode);
    } catch (e) {
      /* Private window or blocked storage: the choice lasts for this page only. */
    }
  }

  function apply(mode) {
    if (mode === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', mode);
  }

  var mode = stored();
  apply(mode);

  function label() {
    var labels = LABELS[(root.lang || 'lt').slice(0, 2)] || LABELS.lt;
    return labels[mode];
  }

  function ready() {
    var buttons = document.querySelectorAll('[data-theme-switch]');
    function show() {
      for (var i = 0; i < buttons.length; i++) buttons[i].textContent = label();
    }
    for (var i = 0; i < buttons.length; i++) {
      buttons[i].hidden = false;
      buttons[i].addEventListener('click', function () {
        mode = MODES[(MODES.indexOf(mode) + 1) % MODES.length];
        store(mode);
        apply(mode);
        show();
        document.dispatchEvent(new CustomEvent('lt2-theme', { detail: mode }));
      });
    }
    show();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready);
  else ready();
})();
