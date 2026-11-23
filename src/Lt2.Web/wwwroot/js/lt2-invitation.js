/*
 * Design 8.11.5, 6.8, 8.22.1: a link with an invitation code carries it after the '#' (a share line's link
 * /seimas/XVP-0412#kodas=…, an invitation link /prisijungti#kodas=…), which the browser never sends. On whatever page such a
 * link opens, this takes the code into the tab's session storage under the one key the inventory lists for it ('lt2.invitation-code',
 * BrowserKeys.InvitationCode) and removes it from the address bar, so the address copied from there or shared again
 * carries no code. The registration form (SignInFlow) fills itself in from it and forgets it once used. Anything else after
 * the '#' (a page part, 'tikslas') stays. Storage may be refused (private windows, settings): then the code is simply not kept.
 */
(function () {
  'use strict';

  var KEY = 'lt2.invitation-code';
  var PARAMETER = 'kodas';

  function take() {
    var hash = window.location.hash;
    if (!hash || hash.length < 2 || hash.indexOf(PARAMETER + '=') < 0) return;

    var code = null;
    var rest = [];
    hash.substring(1).split('&').forEach(function (pair) {
      var equals = pair.indexOf('=');
      if (equals > 0 && pair.substring(0, equals) === PARAMETER) {
        try {
          code = decodeURIComponent(pair.substring(equals + 1).replace(/\+/g, ' ')).trim();
        } catch (e) {
          code = null;
        }
      } else if (pair.length > 0) {
        rest.push(pair);
      }
    });

    if (code) {
      try {
        window.sessionStorage.setItem(KEY, code);
      } catch (e) {
        // Not kept: the registration form asks for the code instead.
      }
    }

    var address = window.location.pathname + window.location.search + (rest.length > 0 ? '#' + rest.join('&') : '');
    try {
      window.history.replaceState(window.history.state, '', address);
    } catch (e) {
      // A browser that refuses keeps the address as it is, which is harmless: the code is never sent.
    }
  }

  take();
  window.addEventListener('hashchange', take);
})();
