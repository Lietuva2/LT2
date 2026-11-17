# Building the interface

Read before changing a page, a component or an island. The rules behind it are design 8.15 (explanatory text), 8.16 (surfaces) and 8.21 (navigation).

- **Island controls stay disabled until the island is interactive** (`InteractiveBase<TText>.Locked`, or `!RendererInfo.IsInteractive`),
  fields included, and the page carries `<NeedsScript />`; a button that only shows a popover ("⋯", a hint's trigger) works
  without script and stays enabled. `tests/Lt2.E2E/IslandsBeforeStartTests.cs` walks the pages with the
  WebAssembly runtime blocked; add a new island page there.
- **One way to do each thing in the interface.** An interactive component inherits `InteractiveBase<TText>`
  (`Lt2.Web.Client/Components`; `TText` is its resource marker): `RunAsync` for every action (one at a time, failures
  become the message), `Succeeded`/`Show` for an answer, `Valid` for the shared validator, `IsSignedInAsync`, and
  `ErrorClass`/`Invalid`/`DescribedBy` with `<FieldError For="input-id">` for field errors. A text with placeholders is
  `L.Format(key, …)`; numbers and dates are `Display` (`Number`, `Day`, `DayTime`, `Today(TimeProvider)`), on the
  server pages too; the moderators' contact address is `Configuration.ModeratorContact()`. A link to a part of the
  same page carries the page's path (`/metodika#sekimas`, never a bare `#sekimas`): the pages carry `<base href="/">`, so
  a bare fragment leads to the front page; `NavigationTests` (integration) walks the served pages and fails on one. Under every member's text (argument, statement, MP's response, comment) the
  actions are one action row (design 8.16.1, the designer's proposal of 2026-11-17): `<div class="act">` holding the toggles
  ("Man svarbu", `CommentThread`'s "Aptarimas" summary, "Sutinku" as `.act__item`) and `<MoreMenu>` ("⋯", the browser's
  popover, placed by `js/lt2-menus.js`) whose `button.menu__item`s close it (`popovertargetaction="hide"`) and open their form
  below the row as `div.act__panel`; `CommentThread` renders `details.discuss` (summary only) plus `.discuss__panel`, which
  the row lays below itself. Icons are `<Icon>` (inline SVG, currentColor). A link to copy is
  `<CopyLink>`; "Atkurti" is `<RestoreForm>`; a queued argument is `<QueueReasonItem>`, a queued statement
  `<StatementQueueItem>`; the positions consent before a first side is `<PositionConsentForm>`; the bill page's islands
  share the caller's answers through `MyBillState`, the front page's through `MyAgendaState` (its `Changed` event tells
  the others after a member's action). **An action never reloads the page (owner, 2026-11-17):** on a bill's page the island
  that acted calls `MyBillState.Announce` (`byModerator` for a moderator's action) and the others read their part again
  (`Changed`); what is new to the reader (design 8.11.8) is `MyBillState.News`/`NewSince`, set once per page by `WhatsNew`,
  with "Rodyti tik naujus" as `OnlyNew`/`Keeps` and `NewsChanged` telling the islands; the counts, the threshold's progress and the share line are islands on `LiveRecordBase` (`SideFigures`,
  `ThresholdProgress`, `ShareLine`) that take the bill's live answer; where the server's part of a page changes (a moderator's
  action, an organisation's profile, an MP's response) the island calls `Navigation.Refresh()`, which reads the page again in
  place and gives the islands new parameters (`FollowButton` takes its count from them again). A full load
  (`forceLoad: true`) is only for a changed session (signing in or out, a passkey or staff session started in place by a
  `<PasskeyStep>` without `Confirmed`, a take-over, the terms, deleting the account). A list whose items
  hold state or re-sort is keyed (`@key`), and an element id is unique on the page (a component shown twice takes an
  `IdPrefix`).
- **Surfaces (design 8.16).** The ground carries what frames a page: title, lead, the way back, notices (`.note`), an
  introduction, a plain row of links. What a person comes to read or act on lies on a `.sheet`, one unit per sheet (a
  list, a table, a part of a bill's page, a form, a queue item); a sheet is never inside a sheet. A form or panel that
  opens inside a sheet is `.sheet--quiet`, and a quiet sheet never stands on the ground. A component used both inside
  and outside a sheet takes a `Panel` parameter and drops its own sheet inside one (`QueueReasonItem`,
  `StatementQueueItem`). Filters and sort links of a list sit on the ground directly above the list's sheet, on every
  list page.


**Explanatory text (design 8.15, owner 2026-11-15).** A screen shows its title, at most one short paragraph that says
what it is, the fields with their labels, and the action; every other explanation is one click away: collapsed on the
page, or on the methodology page (`/metodika`) or the moderation rules, reached by a short link to the part that says
it (the part must exist; give it an `id`). What always stays inline: what becomes public and under whose name, what
cannot be undone, a consent and what it covers, the invented-data notice, "not a representative poll" beside the
numbers, that summaries are a trial and nothing is sent, status and error messages, labels, a field's hint needed to
fill it (format, limit), and what the design requires beside data (a description's label and date, the 30-day count,
a moderator's statement). **The one collapsing pattern** is the browser's own
`<details class="more stack stack--tight"><summary class="linkish small">…</summary>…</details>`: no script, so it
opens on server-rendered pages and in an island before it starts; an island never sets `open` from code (`js/lt2-disclosures.js`,
loaded on every page, gives a disclosure the person opened before the island started the same state once the island
has replaced the markup; it remembers for the page being read only and forgets when the path changes, and `js/lt2-return.js`
keeps the reader's place on Back and Forward while enhanced navigation patches the page in, in memory only; owner,
2026-11-17); the summary
names what opens ("Kaip tai veikia"; a bare "Plačiau" only when it is the only one on the screen).
**New screens follow the rule from the start.** `design/text-audit-2026-11-15.md` is the screen-by-screen record.

**A hint (design 8.15): the short answer without leaving the page, the first place for a term or a number.** A term or a
number whose answer fits one or two sentences gets `<Hint Id="…" Text="Kas yra riba?" Answer="…" More="/metodika#…" />`
(`Lt2.Web.Client/Components/Hint.razor`; on server pages write it `<Lt2.Web.Client.Components.Hint …/>`): a dotted-underlined
`<button>` and the browser's own popover, so a press opens it without JavaScript; `js/lt2-hints.js` (every page, listeners
on the document) adds hover and keyboard focus after a delay, the place beside the word inside the screen, Escape back to
the trigger. The answer says what the thing is and does first, then the number; a setting is said as what holds now
("Dabar – 100"), and a bill's own value when it has one. `More` ("Plačiau") only where there is more to say. A longer
explanation, a list of steps or a form stays a `details.more`; how the site works in general stays on `/metodika`; a link
that would leave the page only to explain a term is a hint instead. In a list, one hint beside the list (near its note
line), never one per row. An island never opens a hint from code; a hint is phrasing content and may stand in a paragraph.
