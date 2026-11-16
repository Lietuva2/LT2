// The account item in the menu (design 8.21). It is a <details> disclosure, so "Mano paskyra" and "Atsijungti" open without
// this script. With it: when the person opens the item, the name and the links their account has (the acceptance of changed
// terms, staff screens or the staff passkey, an MP's projects) are read from GET /api/v1/me and shown; the page as served holds nothing about the
// person. It closes on Escape (focus back to "Paskyra"), on a press outside it and when focus leaves it. Nothing is stored.
// Listeners sit on the document, so it keeps working when Blazor's enhanced navigation replaces the header.
(() => {
  const MENU = "details[data-account-menu]";

  const close = (details, focusToggle) => {
    details.open = false;
    if (focusToggle) details.querySelector("summary")?.focus();
  };

  const show = (details, name, on) => {
    const item = details.querySelector(`[data-account-link="${name}"]`);
    if (item) item.hidden = !on;
  };

  const fill = async (details) => {
    try {
      const response = await fetch("/api/v1/me", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
      if (!response.ok || !details.isConnected) return;
      const me = await response.json();
      const name = details.querySelector("[data-account-name]");
      const text = me.displayName || me.email;
      if (name && text) {
        name.textContent = text;
        name.hidden = false;
      }

      const staff = Array.isArray(me.activeStaffRoles) ? me.activeStaffRoles : [];
      const administrator = staff.includes("administrator");
      show(details, "terms", me.termsRequired === true);
      show(details, "moderation", administrator || staff.includes("moderator"));
      show(details, "settings", administrator);
      show(details, "invitations", administrator);
      show(details, "mp", me.mp !== null && me.mp !== undefined);

      // Design 6.2, 8.21: a staff account whose session cannot act as staff (signed in by code, or with another passkey) is
      // offered its staff passkey instead, coming back to this page afterwards (StaffSessionPage: "grizti" after the "#").
      const staffKey = me.staffAccount === true && staff.length === 0;
      show(details, "staff-key", staffKey);
      const keyLink = details.querySelector('[data-account-link="staff-key"] a');
      if (staffKey && keyLink) {
        keyLink.href = "/prisijungti/raktas#grizti=" + encodeURIComponent(location.pathname + location.search);
      }
    } catch (e) {
      /* Offline or refused: the item keeps "Mano paskyra" and "Atsijungti". */
    }
  };

  // "toggle" does not bubble: listen in the capture phase.
  document.addEventListener("toggle", (event) => {
    const details = event.target;
    if (details instanceof HTMLDetailsElement && details.matches(MENU) && details.open) fill(details);
  }, true);

  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    for (const details of document.querySelectorAll(`${MENU}[open]`)) close(details, details.contains(document.activeElement));
  });

  document.addEventListener("pointerdown", (event) => {
    for (const details of document.querySelectorAll(`${MENU}[open]`)) {
      if (!(event.target instanceof Node) || !details.contains(event.target)) close(details, false);
    }
  });

  document.addEventListener("focusout", (event) => {
    const details = event.target instanceof Element ? event.target.closest(MENU) : null;
    if (details?.open && event.relatedTarget instanceof Node && !details.contains(event.relatedTarget)) close(details, false);
  });
})();
