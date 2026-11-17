# 10. Quality

- Unit tests for every counted rule; integration tests for every endpoint against real PostgreSQL; import tests against
  recorded Seimas data including the awkward cases (consensus votes, mismatch notes, an MP changing faction mid-term, a
  mandate ending).
- End-to-end tests of the main paths at 1280 px and 390 px, as the pages in `../docs/` are checked today.
- Accessibility: WCAG 2.2 AA as the target; public pages work without JavaScript.
- Load test before launch at the size of a campaign shared widely: thousands of people joining one appeal within an
  hour, mostly on phones.
- Lithuanian copy read by a native speaker before anything is shown externally (`AGENTS.md`).

