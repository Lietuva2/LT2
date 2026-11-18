// The menu's two disclosures (design 8.21): the account item and "Projektai" (owner, 2026-11-17: a dropdown with its three
// tabs). Both are <details>, so they open without this script. With it: when the person opens the account item, the name and
// the links their account has (the acceptance of changed terms, staff screens or the staff passkey, an MP's projects) are
// read from GET /api/v1/me and shown; the page as served holds nothing about the person. Each closes on Escape (focus back to
// its toggle), on a press outside it and when focus leaves it.
// Design 8.5.1 (owner, 2026-11-17): on a member's page the account's own GET /api/v1/me is read once per page load and, when
// it offers to take over an MP's profile (mpOffer), the hidden notice under the header is shown with the MP's name; the answer
// is kept in this page's memory only and applied again after Blazor's enhanced navigation. Nothing is stored. The same answer
// shows a staff account the box of a prepared summary to MPs (digestReview, design 8.19.2).
// Listeners sit on the document, so it keeps working when Blazor's enhanced navigation replaces the header.
(() => {
  const ACCOUNT = "details[data-account-menu]";
  const MENU = "details[data-account-menu], details[data-nav-menu]";

  const readMe = async () => {
    const response = await fetch("/api/v1/me", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
    return response.ok ? response.json() : null;
  };

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
      const me = await readMe();
      if (!me || !details.isConnected) return;
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
      show(details, "usage", administrator);
      show(details, "jobs", administrator);
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

  // The MP's name of the account's offer, once read; undefined until then, null when there is none.
  let offer;

  const applyOffer = () => {
    const notice = document.querySelector("[data-mp-offer-notice]");
    if (!notice) return;
    const skip = (notice.getAttribute("data-skip") || "").split(" ");
    const text = notice.querySelector("[data-mp-offer-text]");
    const on = typeof offer === "string" && !skip.includes(location.pathname.replace(/\/$/, "")) && text !== null;
    if (on) text.textContent = (text.getAttribute("data-format") || "").replace("{0}", offer);
    notice.hidden = !on;
  };

  // Design 8.19.2 (owner, 2026-11-17): the prepared summary to MPs for a staff account (digestReview), once read; null when none.
  let review;

  // "yyyy-MM-dd HH:mm" in Vilnius time, as the site writes dates in numbers.
  const vilnius = (iso) => {
    const parts = {};
    for (const p of new Intl.DateTimeFormat("en-GB", {
      timeZone: "Europe/Vilnius", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23",
    }).formatToParts(new Date(iso))) parts[p.type] = p.value;
    return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}`;
  };

  // A Lithuanian count with its noun: forms "one|few|many", each with {0} for the number.
  const counted = (n, forms) => {
    const [one, few, many] = (forms || "{0}|{0}|{0}").split("|");
    const units = n % 10, tens = n % 100;
    return ((tens >= 11 && tens <= 19) || units === 0 ? many : units === 1 ? one : few).replace("{0}", String(n));
  };

  const applyReview = () => {
    const notice = document.querySelector("[data-digest-review-notice]");
    if (!notice) return;
    const text = notice.querySelector("[data-digest-review-text]");
    const skip = (notice.getAttribute("data-skip") || "").split(" ");
    const on = review && text !== null && !skip.includes(location.pathname.replace(/\/$/, ""));
    if (on) {
      text.textContent = (text.getAttribute("data-format") || "")
        .replace("{0}", vilnius(review.dueAt))
        .replace("{1}", counted(review.bills, text.getAttribute("data-bills")))
        .replace("{2}", counted(review.reasons, text.getAttribute("data-reasons")));
    }
    notice.hidden = !on;
  };

  const readNotices = async () => {
    // Only on a member's page: the notices are served to signed-in sessions only, so a visitor's browser asks nothing.
    if (!document.querySelector("[data-mp-offer-notice]")) return;
    try {
      const me = await readMe();
      const mp = me?.mpOffer;
      offer = mp ? `${mp.firstName} ${mp.lastName}` : null;
      review = me?.digestReview ?? null;
    } catch (e) {
      offer = null;
      review = null;
    }
    applyOffer();
    applyReview();
  };

  readNotices();
  if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
    window.Blazor.addEventListener("enhancedload", () => (offer === undefined ? readNotices() : (applyOffer(), applyReview())));
  }

  // "toggle" does not bubble: listen in the capture phase.
  document.addEventListener("toggle", (event) => {
    const details = event.target;
    if (details instanceof HTMLDetailsElement && details.matches(ACCOUNT) && details.open) fill(details);
  }, true);

  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    for (const details of document.querySelectorAll(MENU)) {
      if (details.open) close(details, details.contains(document.activeElement));
    }
  });

  document.addEventListener("pointerdown", (event) => {
    for (const details of document.querySelectorAll(MENU)) {
      if (details.open && (!(event.target instanceof Node) || !details.contains(event.target))) close(details, false);
    }
  });

  document.addEventListener("focusout", (event) => {
    const details = event.target instanceof Element ? event.target.closest(MENU) : null;
    if (details?.open && event.relatedTarget instanceof Node && !details.contains(event.relatedTarget)) close(details, false);
  });
})();
