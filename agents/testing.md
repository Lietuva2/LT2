# Writing tests

Read before writing or changing a test. The commands are in [AGENTS.md](../AGENTS.md).

- **Use the shared steps; add to them rather than copy.** Integration tests: `tests/Lt2.IntegrationTests/Infrastructure/`
  (`AppealSteps`, `OrganisationSteps`, `MpSteps` and `MpResponseSteps` (take a profile over, office members, code sessions,
  responses, an adoption vote, the responses part without its islands), `CommentSteps` (design 8.23: comments, "Sutinku", a bill with an argument and a statement to comment on), `MpSendingSteps` (design 8.19: `ResetMpMailAsync`,
  `IntroduceAsync`, settings after a fresh passkey confirmation, the MP mail in the outbox and its links' tokens),
  `ReasonsToComeSteps` with `VilniusToday` and `MondayInWeeks`, `SettingsSteps`,
  `StaffMembers` with `ConfirmFreshAsync` (a fresh passkey confirmation right before a sensitive action, design 6.2: a
  setting, a role, an urgent summary), `LoggingFactory`). End-to-end tests: `tests/Lt2.E2E/E2ESteps.cs` (registration from any link, ticking the
  age and the terms of use, `Mailpit`, screenshots, API calls from the page, closing a test's organisation, `SqlAsync` against
  the stack's database); a staff fixture's `ConfirmFreshAsync` before an API call that needs a fresh confirmation.
- **Mail in end-to-end tests.** Read a message the test has just caused by count, not as whatever is newest: count the
  query first, act, then `Mailpit.NewAsync(query, before)` (a sign-in code: `Mailpit.NewCodeAsync(email, before)` with
  `Mailpit.CodeQuery(email)`). An earlier code stays in Mailpit after it is used, and an MP's official address gets the
  summaries and settings mail of other tests in the same run.
- **Terms of use in tests (design 8.22.2).** Every member made through the shared steps accepts the terms (`TestBrowser`'s
  completion sends `AcceptsTerms`); an account older than them is made by deleting its `terms` consent (integration
  `TermsTests`) or setting an earlier `text_version` (E2E `TermsTests`). A new API write route needs nothing: the sweep in
  integration `TermsTests` sends every mapped write as such an account and expects `terms.required`; put a route on
  `TermsExemptions.Routes` only when the design says the write must work before acceptance.
- **Navigation in end-to-end tests (design 8.21).** "Mano paskyra" and "Atsijungti" are inside the account item: open it with
  `#account-toggle` first; the four tabs are `#project-tabs`; the theme switch is in the footer. The account page's own
  "Atsijungti" (`main`) is unchanged.
- **Staff:** `ModeratorAsync()` / `AdministratorAsync()` of `ApiTest` give the run's shared staff members. Only a test about
  the staff account itself (passkeys, sessions, a reset, a per-moderator limit) makes its own with `StaffMembers.ActiveAsync`.
- **Settings:** change one through the application that reads it (each keeps a short cache) with `SetSettingAsync`, and
  put it back in a `finally` with `ResetSettingAsync` (the design's default) or `WithSettingAsync` (the value before);
  never with a number written into the test.
- **A check that something is absent must be able to fail.** Assert the status of every set-up step and of every read a
  privacy sweep makes; compare an answer's exact set of fields rather than searching a payload that holds random ids for a
  short string; prove the swept answer held what it is about.
- **Every test is independent (owner, 2026-11-16).** It passes alone, with its class alone and in any order of the
  suite; it relies on nothing an earlier test left and leaves nothing a later one could trip on. Run the suites in another
  order to check: `LT2_TEST_ORDER` = `forward`, `reverse`, `random` or `random:SEED` (`tests/Shared/TestOrder.cs`, compiled
  into every test project; xUnit's own order is stable per build and folder, and its `--seed` does not change it), most
  simply through `python tools/run_tests.py unit|architecture|integration|e2e --order reverse` (also `--each-class`, every
  class alone in its own process on the stack as the others left it; `--repeat N`; `--class NAME`). The script keeps every
  failure's full message (TRX report), the order the classes ran in and, for end-to-end runs, what tests could leave on the
  stack before and after the run (`tests/Lt2.E2E/leftovers.sql`): the two must read the same.
- **Shared data:** the integration tests share one database and run one at a time; a test frees the bill it uses
  (`FreeBillAsync`) and keeps to its own summary weeks (`MondayInWeeks`). A test that sends MP mail starts and ends with
  `ResetMpMailAsync` and puts `Digest:Sending` back: the letter, the MPs' choices, the links and the suppression list are
  shared by the run. A shared staff member (`ModeratorAsync()`, `AdministratorAsync()`) comes with a fresh passkey
  confirmation, so a setting can be saved whatever ran before. An MP's official address is shared by every test that uses
  the MP: a test reads only the notices sent after it started (`MpNoticesAsync(address, before)`). The shared factory's
  per-address request limits are out of reach (`Lt2Factory`), so no test depends on how many requests came before it.
- **End-to-end tests clean up after themselves; nothing outside the repository prepares or repairs the stack.** The
  classes share one stack and run one at a time. Every class derives from `E2ETest`: after each test, passed or failed,
  the accounts it registered through the shared steps (`E2ESteps.RegisterHereAsync`, `PasskeyTests.RegisterAsync`) are
  deleted through the product (`DELETE /api/v1/me`, from the session the browser ended with, else by a code sign-in;
  `TestAccounts`), and first what the product keeps on purpose and a test must not leave: moderators' actions and reports on
  their arguments, the organisations they registered (`E2EData`); then what the product made because of the test and keeps with nothing left on it: a bill's record opened during the test, and the follow job's planned, voted and answer records about bills nobody follows any more (`E2EData.ForgetSinceAsync`; its comment records stay, being where its next window starts, and `leftovers.sql` does not count them). A staff fixture forgets its moderators' actions and deletes
  its account from its staff passkey session when the class is over. The run makes its own invitation through the migrator
  and deletes it at the end (`E2ERun`, an assembly fixture; `LT2_E2E_INVITATION` names an existing code instead, for a
  stack whose migrator cannot be run), and waits for the web host before the first test; on a stack made afresh it writes
  the demonstration data many tests read (`--demo-appeal`, once the worker's first import is in), which the leftovers read
  before that run therefore lack. A test that changes a
  demonstration bill brings it to a known state before and puts it back in a `finally` (`E2EData.RestoreDescriptionAsync`;
  a summary it made, `DeleteSummaryAsync`); something the product keeps unlinked from the account (feedback without a reply
  address) carries a marker and is removed by it. Every name a run gives an organisation carries "(E2E)" and every
  description "(E2E bandymas)". `MpSendingTests` deletes everything of MP mail it made (runs, deliveries, choices, links,
  suppression rows, letters). When a test fails, the last screen of every browser it used and of the class's staff pages
  is kept in `output/failures/` beside the test assembly.
- **Settings in end-to-end tests** are changed and put back through the settings screen
  (`AdministratorFixture.ChangeSettingAsync`, `RestoreSettingAsync` in a `finally`), never by SQL: the site drops its cached
  copy (30 s) only for a change made through it, so a SQL change leaves the next test to the cache's timing. The worker
  keeps its own copy for 30 s either way; a test that needs the worker to see a value waits for the result (an email),
  longer than that.
- **Days are Vilnius calendar days.** "Yesterday" in a test is noon of the Vilnius day before, never `now - 24 hours` (two
  days back on the morning after the clocks go forward); a job that decides by the day is run at a time the test chose
  from the day it started on, not at the real time.
- **The bot check (design 6.4).** Every TestBrowser.PostAsync of a `RegisterRequest` or `RequestCodeRequest` with `BotCheck` null
  fetches a challenge and solves it first (`BotCheckSteps`, the same work as the page's script; the factory sets `Security:BotCheckKey`);
  a test of the refusal sends `BotCheck = ""` or a raw JSON string, and one that needs the check off uses `WithSettingAsync("BotCheck:Enabled", false)`.
  A request built by hand (`HttpRequestMessage`) takes `await browser.SolveAsync()`; end-to-end steps that post to the API take
  `E2ESteps.BotCheckAsync(client)`. The pages' own requests carry theirs.
- **Known browsers (design 8.20.2).** A `TestBrowser` that completed a registration or signed in to a member is known to that
  account (the test factory sets `Security:DeviceHashKey`): its later code requests use the known browser's allowance and
  codes, not the shared ones. A test about the shared limits asks from a new browser (`NewBrowser()`, `CodeSessionAsync`).
- **The "Seimo narių atsakymai" email (design 8.20.1)** comes at the follow job's next run under "Iš karto" (design 8.24):
  `ResponseNoticeTests` moves the bill's `announced_at` to yesterday and runs the job at 01:00 of the day it started on; the
  end-to-end step in `MpResponseTests` waits for the next run and removes the bill's `bill_event` rows of kind `response`.
- **The email rhythm (design 8.24):** a test reads what a person was told with `NoticesAsync`/`EmailsAsync` (alone or inside a
  gathered email) or `InboxAsync` (`ReasonsToComeSteps`), runs the job at times it chose in a week of its own
  (`EmailRhythmTests`, `MondayInWeeks(21)`), and moves a choice's moment by SQL (`frequency_since`) when it must come after
  events the test placed in that week.
- **Waits:** no fixed sleeps. In end-to-end tests a step after a navigation or a sign-in waits up to `E2ESteps.Slow`
  (60 s); Playwright's default of 5 s failed when an integration run shared the machine.
- **Resource keys:** `ResourceKeyTests` (unit) reads `src` and fails when a literal key given to a localizer (`T["Key"]`,
  `T.Format("Key", …)`, `cond ? "A" : "B"`) is not in the `.resx` of its marker class, or when a key-like literal goes to
  something it cannot tie to a resource file (teach it the construct); keys built at runtime are not checked. A key whose
  copy is not written yet goes on its `CopyMissing` list with the date it was reported, never as invented Lithuanian.

## Looking at every email

An email's look is checked by eye, on all of them, whenever a template or `EmailLayout` changes (owner, 2026-11-20).

1. `migrator --demo-emails` (README: refuses unless `LT2_DEMO_DATA` and `LT2_MAIL_CAUGHT` are true) queues one email of every
   outbox kind and variant from invented data, to `NAME@pavyzdys.test`; the worker sends them and Mailpit lists them by name.
   `DemoEmails` (Notifications) builds them with the same payload types and `OutboxMessage.Create` as the code that sends each
   kind. `DemoEmailTests` fails until a new kind in `EmailStreams` has a demonstration, renders every one through the real
   renderers, and checks the first things an eye finds: no address of over 40 characters printed in the HTML part (the pages a
   letter says to type are the one exception), every address the HTML links to also in the plain part, no placeholder or
   resource key left in the text. The same test writes the rendered emails to `output/email-samples/demo/` without a stack.
2. Read each message from Mailpit's API (`/api/v1/messages`, then `/api/v1/message/{ID}` for `HTML` and `Text`), save the HTML,
   and take a headless Chromium screenshot (`chrome --headless=new --hide-scrollbars --window-size=800,1600
   --screenshot=x.png file:///x.html`); for a phone, show the file in an iframe 390 px wide. A page of four iframes side by side
   shows four emails in one picture. Look at every one: order, a sign-off, an empty line, a link printed bare, characters.
3. Fix in the renderer or the template, run again, look again; each kind gets a line in a review file until all are good.
