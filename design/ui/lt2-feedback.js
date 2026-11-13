// Feedback balloon (design 8.10): Escape closes an open panel and returns focus to its button. The panel opens and
// closes without this script (it is a <details> element).
document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape") return;
  for (const panel of document.querySelectorAll("details.feedback[open]")) {
    panel.open = false;
    panel.querySelector("summary")?.focus();
  }
});