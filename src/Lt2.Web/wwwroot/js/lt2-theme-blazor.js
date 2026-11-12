/*
 * Companion of lt2-theme.js (which is copied unchanged from design/ui/). Blazor's enhanced navigation patches the page
 * from the server's HTML, which puts "hidden" back on the theme switch and can drop data-theme from <html>. After every
 * enhanced page load this shows the switch again and re-applies the stored choice. The switch itself is kept across
 * navigation (data-permanent), so its click handler and label stay those of lt2-theme.js.
 */
(function () {
  'use strict';

  function again() {
    var mode = null;
    try {
      mode = window.localStorage.getItem('lt2-theme');
    } catch (e) {
      /* Blocked storage: the system setting applies. */
    }
    if (mode === 'light' || mode === 'dark') document.documentElement.setAttribute('data-theme', mode);
    else document.documentElement.removeAttribute('data-theme');
    var buttons = document.querySelectorAll('[data-theme-switch]');
    for (var i = 0; i < buttons.length; i++) buttons[i].hidden = false;
  }

  if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
    window.Blazor.addEventListener('enhancedload', again);
  }
})();
