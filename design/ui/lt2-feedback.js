// Feedback balloon (design 8.10). The panel opens and closes without this script (it is a <details> element whose
// button shows a close mark while open). With the script: the panel's own close button appears, Escape closes it, focus
// moves into the panel when the person opens it and back to the button when it closes. Listeners sit on the document,
// and a MutationObserver shows the close button again, so it keeps working when Blazor's enhanced navigation replaces
// the page's content.
(() => {
  let openedByPerson = null;

  const close = (details) => {
    details.open = false;
    details.querySelector("summary")?.focus();
  };

  const showCloseButtons = () => {
    for (const button of document.querySelectorAll("details.feedback [data-feedback-close][hidden]")) {
      button.hidden = false;
    }
  };

  showCloseButtons();
  document.addEventListener("DOMContentLoaded", showCloseButtons);
  new MutationObserver(showCloseButtons).observe(document.documentElement, { childList: true, subtree: true });

  const noteOpening = (event) => {
    const summary = event.target instanceof Element ? event.target.closest("details.feedback > summary") : null;
    if (summary && !summary.parentElement.open) openedByPerson = summary.parentElement;
  };
  document.addEventListener("click", noteOpening, true);
  document.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") noteOpening(event);
  }, true);

  document.addEventListener("click", (event) => {
    const button = event.target instanceof Element ? event.target.closest("[data-feedback-close]") : null;
    const details = button?.closest("details.feedback");
    if (details) close(details);
  });

  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    for (const details of document.querySelectorAll("details.feedback[open]")) close(details);
  });

  // "toggle" does not bubble: listen in the capture phase. Only an opening by the person moves the focus (not a page
  // that arrives with the panel open after a post): to the first field, else the heading.
  document.addEventListener("toggle", (event) => {
    const details = event.target;
    if (!(details instanceof HTMLDetailsElement) || !details.classList.contains("feedback")) return;
    if (details.open) {
      if (openedByPerson !== details) return;
      openedByPerson = null;
      const target = details.querySelector(".feedback__panel textarea, .feedback__panel input:not([type=hidden])")
        ?? details.querySelector(".feedback__panel h2");
      target?.focus();
    } else if (details.contains(document.activeElement) && document.activeElement !== details.querySelector("summary")) {
      details.querySelector("summary")?.focus();
    }
  }, true);
})();
