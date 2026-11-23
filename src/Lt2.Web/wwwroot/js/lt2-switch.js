/*
 * A segmented switch made of radio inputs shows its panels with CSS alone (design 8.12.1, the bill page's "Lietuva 2.0
 * naudotojai | Seimas"). A panel that has to fetch something when it is first shown (the Seimas panel's seat map) is an
 * island that asks this file to tell it when its radio becomes checked: lt2Switch.watch(id, target) calls
 * target.Selected() when the radio with that id is checked now (it may have been chosen before the island started, or
 * restored by the browser) and every time it is checked afterwards; the island decides whether to fetch. The listener sits
 * on the document, as in lt2-hints.js, so it still works after enhanced navigation replaced the markup around the radio;
 * lt2Switch.unwatch(id) removes it when the island goes. Nothing is stored and nothing is sent anywhere.
 */
(function () {
  'use strict';

  var listeners = {};

  function notify(target) {
    // The island may be gone already (the page was left): its reference then rejects, which is not an error.
    target.invokeMethodAsync('Selected').catch(function () {});
  }

  function unwatch(id) {
    if (!listeners[id]) return;
    document.removeEventListener('change', listeners[id]);
    delete listeners[id];
  }

  function watch(id, target) {
    unwatch(id);
    listeners[id] = function (event) {
      var radio = event.target;
      if (radio && radio.id === id && radio.checked) notify(target);
    };
    document.addEventListener('change', listeners[id]);
    var radio = document.getElementById(id);
    if (radio && radio.checked) notify(target);
  }

  window.lt2Switch = { watch: watch, unwatch: unwatch };
})();
