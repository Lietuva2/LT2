# 6. Security and privacy

## 6.1 What we protect

1. **Who took which side.** A position on a bill is a political opinion: special-category data under GDPR Art. 9. Leaking
   it is the worst realistic incident.
2. **The integrity of the numbers.** MPs and journalists must be able to trust that 4 280 means 4 280 distinct verified
   accounts.
3. **The sending channel.** A system that can email every MP is a target: if it is abused once, the summaries get
   filtered and the product stops working.
4. **Politician accounts.** A hijacked MP account publishes in the MP's name.

## 6.3 Authorization

Policy-based, with resource handlers where ownership matters.

| Role or claim | Can |
|---|---|
| Visitor | Read all public pages and public API. |
| Member (confirmed email; invited while registration needs an invitation, 6.2) | Take a side, write and endorse reasons, report content; comment under arguments, statements and responses and mark a comment "Sutinku" (8.23). |
| Verified MP / office member (`politician_link`) | Publish and edit that MP's response; manage that MP's email preferences; comment as the MP under that MP's own response, in a passkey session (8.23.2). |
| Moderator | Review bills' records and reasons, merge, hide (reported comments too, 8.23.8), correct planned dates, hold a send, approve an urgent send, restrict an account from writing and lift that. |
| Administrator | Roles, resetting an account, locking and unlocking an account (8.3), ending an MP's link, runtime settings, invitation codes, usage statistics, the MP sending screens, and the jobs dashboard ("Foniniai darbai", `/administravimas/darbai`: the recurring jobs, their runs and failures, "Trigger now"); every moderator action, restricting an account from writing and lifting that included. Cannot read individual positions through any screen. |

No screen and no API returns another person's position. Group numbers (the supporters of the MP's party on a side, in a
summary) are published only for groups of at least the runtime setting `Digest:PartyGroupMinimum` (default 10, 0 = no
floor; 8.4).

## 6.4 Web hardening

- TLS only, HSTS with preload, HTTP/3.
- Content Security Policy: `default-src 'self'`; `script-src 'self' 'wasm-unsafe-eval'` (required by WebAssembly), no
  inline scripts or styles; `frame-ancestors 'none'`; `Referrer-Policy: no-referrer`, `Permissions-Policy`,
  `X-Content-Type-Options`, `X-Frame-Options: DENY`, same-origin `Cross-Origin-Opener-Policy` and
  `Cross-Origin-Resource-Policy` (cross-origin for `/img` only, 8.22.5). One exception, under `/administravimas/darbai`
  only (the Hangfire dashboard, 6.3): `style-src-attr 'unsafe-inline'`, because its own markup colours a job's state
  with a `style` attribute, and `script-src 'self'` without `'wasm-unsafe-eval'`. Still no inline script and no
  `<style>` element; its scripts, styles and fonts are served from our host.
- The Hangfire dashboard answers only an administrator in a staff-passkey session (the endpoint requires the policy, and
  Hangfire's own authorization filter evaluates it again); everyone else is sent to sign in or away, never shown it. Its
  forms (trigger, retry, delete) pass the `Origin` check and ASP.NET Core antiforgery with the token Hangfire puts in the
  page, and each is audited as `jobs.dashboard-action` without details (its poll of the counters is a read and is not).
  Its pages are `no-store` and `noindex`. Jobs take only a cancellation token as an argument, so Hangfire stores no
  person's data with them; a failed run shows the exception's type, message and stack.
- Every state-changing API call needs the antiforgery token header and passes an `Origin` check; CORS is off.
- Rate limits: ASP.NET Core policies per IP for code requests, registration, sign-in, one-time codes and the token
  links of MP mail and members' stop links (those also per token, 8.19.7), and per account for writing reasons, writing
  comments (their own policy, 8.23.4), reports, registering an organisation and taking part (sides, follows, marks, the
  display name). **Not built.** A second layer per IP in Caddy.
- **Bot check on registration and code requests** (6.2): a self-hosted proof of work in the ALTCHA protocol, written here and
  on the browser's own `crypto.subtle`, no package and no third-party script, nothing kept in the browser (no cookie, no
  storage key, so nothing for 8.22.1's list) and nothing for the person to do. `GET /api/v1/bot-check/challenge` (no
  session, `no-store`, `RateLimits:BotChallenge`: 60 per 15 minutes per address, 429 beyond; 204 while the check is off)
  answers `{algorithm: "SHA-256", challenge, maxnumber, salt, signature}`: the salt is random hex with its expiry
  (`?expires=`, Unix seconds), `challenge` = SHA-256(salt + a secret number from 0 to `maxnumber`), `signature` = HMAC-SHA-256
  of the challenge under the server's key, the secret `bot_check_key` (`Security:BotCheckKey`, web only; without it a web
  instance signs with a random key of its own, which holds on that instance until it restarts, and logs a warning at start).
  The server keeps nothing for a challenge it hands out. The browser finds the number and sends the five fields and the number
  as base64 of JSON in the request body's `botCheck`, never in an address; `RegisterHandler` and `RequestCodeHandler`
  (module services, so for every caller) check it before anything else (`BotCheckRules.Check`, pure): well formed and at most
  1 024 characters, the signature the server's, not expired, the number solves it. **Used once:** the signature, with the
  challenge's expiry, is inserted into `identity.bot_check_use` (a primary key: the first insert wins, in any web instance) and
  housekeeping deletes it after the expiry. Every failure is the same 400 `bot-check.failed` (the reason only in a log line
  without the solution). `BotCheck:Enabled` (on), `BotCheck:MaxNumber` and `BotCheck:ChallengeMinutes` are settings (5.6).
  **Difficulty:** `MaxNumber` 20 000 is 10 000 tries on average and 20 000 at worst; the browser asks `crypto.subtle` for
  256 hashes at a time: measured with the same batching on a desktop (Node's WebCrypto) about 90 ms on average and 0.2 s at worst;
  a mid-range phone is estimated three to five times slower (0.3 to 0.5 s on average, not measured on one), all while the person types. It is a cost to a script, not a barrier: its work
  grows with the setting, and the per-address limits above stay as they are. A request from a signed-in account (changing the
  email address, 6.2) and passkey sign-in carry none. The Content Security Policy needs no change (own script, own origin).
- All input validated by the shared validators on the server; user text is stored as plain text and encoded on output,
  no HTML or Markdown from users. Paragraphs, `https://` links and lists are read from the plain text by fixed rules
  (8.2, "A text's layout"), each piece encoded; nothing a person types is parsed as markup.
- Outbound requests go only to a fixed allowlist of hosts (Seimas, email, AI, storage). The AI host (`api.anthropic.com`,
  8.11.4) is on the web host's list only while the provider is configured, and on the worker's (the summary job, 8.13)
  on the same condition.

## 6.5 Data protection

- **Explicit consent** before the first position or the first "Man svarbu" (a mark is about a political question; 8.2),
  with the consent text versioned and stored. Withdrawing deletes the positions and the marks. The form says that the
  person's side on bills and which arguments they mark "Man svarbu" are their political opinion, kept only with this
  consent and shown to nobody, and that withdrawing deletes both; the box is "Sutinku, kad Lietuva 2.0 saugotų mano
  pozicijas dėl įstatymų projektų ir „Man svarbu“ žymas taip, kaip aprašyta." (version 2026-11-18). The account page's
  part is "Sutikimas saugoti pozicijas ir žymas", shown for any positions consent not withdrawn, whatever text it was
  given to; its withdrawal is one step and ends every version, deleting what they covered. A consent to an older text is
  not the consent in force: the form is asked again, in place, before the next new side or mark; meanwhile nothing is
  deleted, the person's sides and marks stay stored and counted, and taking a side back or removing a mark works. The
  account page's part then has one line more, "Sutikote su ankstesne šio sutikimo teksto versija. Perskaitykite
  galiojančią ir patvirtinkite.", with "Skaityti galiojantį tekstą", which opens the same form in place ("Galiojantis
  sutikimo tekstas", the text, the box, "Sutinku"); accepting records the version in force and the line goes.
- **Minimisation.** No personal code, address or document number. No phone number is collected. A wallet-based level (6.2, not built)
  would keep only a pairwise pseudonymous identifier, never a personal code. No per-person history of changed positions.
- **Deletion and export.** A person can download everything they gave us and delete the account; positions are removed
  and counts fall. Frozen digest snapshots keep their totals: they are aggregates and a record of what was sent.
- **Encryption.** ASP.NET Core data-protection keys are stored in PostgreSQL, wrapped with a key supplied as a secret.
  **Not built.** Disk encryption on the VPS volume, TLS to the database inside the Docker network, encrypted backups.
- **Logs** carry request ids, never emails or sides; IPs are hashed with a rotating key and kept 30 days.
- **Usage statistics** (8.27): a visitor's address and browser, or a member's account, are hashed with a random salt of
  the day, month and year into an aggregate sketch; while the period lasts the salt and the sketch are stored and a
  candidate can be tested against them (8.27.2); when it ends both are deleted and only counts remain.
- **Secrets** live outside the repository and outside images: encrypted with SOPS and age in a private operations
  repository, mounted as files at deploy, rotated on a schedule and after any staff change.
- **Before launch:** a data protection impact assessment (required for large-scale processing of special-category data), a
  privacy notice, a processing record, processor agreements with the email, AI (Anthropic, for the description draft and
  the AI summaries only: the bill's number, title and official text, no personal data; 8.11.4, 8.13) and storage
  providers, and a lawyer's check of whether a permit from the State Data Protection Inspectorate is needed.

## 6.6 Integrity of the numbers

- One account per normalised email address (6.2); one position per account per bill. This is weaker than a verified identity,
  which is why the 30-day count is shown wherever a bill's numbers are read in full and why every number is described
  as a signal.
- A bill's counts on its page, in the summaries (page and email), on the moderators' summary screen and on the
  statistics page come with the count for **accounts older than 30 days**. A list of bills (the front page, the agenda,
  `/projektai`, an MP's bills) shows one number a side and leads to the bill's page, where the count is (owner,
  2026-11-21).
- **Surge detection:** when a large share of recent positions on a bill come from accounts created in the last days
  (default: at least 30 % of the last 72 hours' positions and at least 50 accounts; the settings `Surge:MinimumShare`,
  `Surge:WindowHours`, `Surge:MinimumAccounts`), the bill's page and the summary say so in neutral words, and a moderator
  is asked whether to hold the send.
- Counts in a summary are frozen in `digest_item` and published at a public address, so anyone can check what MPs were
  told (section 8.7).

## 6.7 Supply chain and delivery

Central package versions, NuGet audit in CI (warnings are errors). Containers run on chiselled .NET images with a
read-only file system and dropped capabilities.

- **Lock files.** Every project has a `packages.lock.json` (`RestorePackagesWithLockFile` in `Directory.Build.props`); CI
  and the Dockerfile restore in locked mode, so a package version that is not in the lock file fails the build. After a
  package change, `dotnet restore --force-evaluate` rewrites the files, which are committed. The files hold the package
  versions the SDK brings with it, so the Dockerfiles' SDK image is pinned to the version in `global.json`, and the two
  change together. The image build's first restore (project files only) is not locked; the restore in its publish step is.
- **Dependabot** (`.github/dependabot.yml`): weekly, for NuGet (minor and patch together, a major on its own), the base
  images in `deploy/` and the workflow actions, with few pull requests open at once.
- **CodeQL** (`.github/workflows/codeql.yml`): GitHub's analysis of the C# code on every push to `master`, every pull
  request and weekly, with a manual `dotnet build`. Every action in the workflows is pinned to a full commit SHA with its
  version in a comment; Dependabot proposes the updates.
- **`/.well-known/security.txt`** (RFC 9116), served only when the deployment sets `Site:SecurityContact`
  (`LT2_SECURITY_CONTACT`, a `mailto:` or https address; another value stops the start): Contact, Expires (made on each
  request, 180 days ahead, UTC), Preferred-Languages `lt, en`, Canonical (the site address and the path); `text/plain`,
  cached for an hour. Without the setting the path answers 404.

**Not built.** Secret scanning (a repository setting the owner switches on; the repository holds no secrets), a container
image scan, an SBOM per release, images signed and pulled by digest. An external penetration test before the first real
summary is sent.

## 6.8 Rules held throughout

- **Nothing held back becomes public through history.** A text that pre-screening held and no moderator published was
  never public, and no history shows it: an organisation statement's "earlier versions" (8.14.14) are only the
  versions that were public.
- **A flagged change is looked at.** When an organisation changes its description and pre-screening flags the new
  text, the organisation returns to the moderators' queue, as it does for a new name or website (8.14.14).
- **A report is closed only by an action that answers it.** Keeping, hiding, restoring, publishing, describing and
  confirming answer a report on their target; setting a threshold or a planned date, holding and releasing do not.
- **A finished bill is finished for everyone at the same moment.** Once the bill's final vote is recorded, taking part
  (a side, an argument, "Man svarbu", a withdrawal) is refused, as an organisation's statement is, whether or not the
  hourly job has closed the record yet.
- **Writes to one bill's record are serialised.** A moderator's description and the summary job's text cannot
  overwrite each other, and a description a person wrote is never written over by the job (8.13).
- **A reset ends sessions.** When an administrator resets a staff account or a role is activated, the sessions that
  were signed in with the removed passkeys end in the same step (6.2).
- **One current code.** A member has at most one personal invitation code that works, enforced by the database, as
  an organisation has (6.2, 8.14.6).
- **Limits hold under parallel requests.** Sign-in codes per address, "one urgent summary a week" and the summary
  ceilings are counted under a lock (6.2, 8.4).
- **Rules are checked where the data is written.** A rule checked only by the HTTP validators does not hold for
  callers inside the server: the explicit consent for party support, the display name's length and a moderation
  statement's presence and length are checked by the module that stores them.
- **The outbox forgets.** Deleting an account, and purging an unfinished registration, delete the outbox rows
  addressed to that person that were not sent. Rows that failed are deleted after 30 days. An error stored or logged
  for a failed email holds the error's type and the server's status code, never its text, which can quote the address.
- **Codes go first.** The dispatcher sends the `codes` stream (sign-in and confirmation codes and the other account-security
  emails, 8.19.3) before any other waiting email, and a stream at its ceiling or paused is left out of the batch, so a
  large batch of follow emails cannot delay a code past its lifetime (the streams also have their own sender address and
  SMTP account; this is the order within the queue).
- **Redirects stay inside the allowlist.** An outbound request does not follow a redirect to a host that is not on
  the allowlist (6.4).
- **Codes and tokens are not sent in addresses.** A link the site makes carries its secret after the `#`: the
  invitation code of a registration link (`/prisijungti#kodas=…`) and of a share line's link (`/seimas/XVP-0412#kodas=…`,
  8.11.5), the page to come back to of a sign-in link (`#tikslas=…`), and the token of a representative's invitation,
  an MP's take-over link and an office member's invitation. The browser never sends that part, so no proxy and no
  application log can hold it, on a shared server included (9.2). The page reads it in the browser and passes it to the
  API in a request body, never in a path or a query. A code in the query (`?kodas=…`) is not read; the proxy's filter
  that drops `kodas` from the logged address stays as a safety net (6.2). Two exceptions carry their token in the path,
  because the one-click stop of RFC 8058 that mail programs send has nowhere else to put it: an MP's mail links (8.19.4)
  and a member's stop link in every email of the rhythm (8.24.1). Logs and the proxy keep their paths with the token
  replaced.
- **A way in without the link: a typeable one-time code.** Three one-time links carry their secret as a code a person
  can type, because clicking links in email is not safe: an MP's take-over link (8.5.2), an office member's invitation
  (8.5.3) and an organisation representative's invitation (8.14.14). The code is the link's one secret, not a second
  one: twelve characters from the invitation codes' alphabet (no 0, 1, I, L, O, U), shown in three groups of four
  (`ABCD-EFGH-JKMN`, 30¹² ≈ 2⁵⁹), typed in any case with or without spaces and dashes, stored only as a hash of its one
  form, used once, and valid as long as the link (24 hours, seven days, seven days). It stands after the link's `#` and,
  where the link is emailed, in the email under the link with the page to open ("Nenorite spausti nuorodos laiške?
  Naršyklėje patys atverkite {page} ir įveskite šį kodą:"); where a representative copies the link, the code is shown
  beside it. The page opened without a `#` asks for the code ("Kodas" or "Kvietimo kodas"), checks only its shape in the
  browser, puts it after the `#` of its own address and goes on as from the link. An unknown, malformed, used or expired
  code gets the one answer the link gets. **Attempts are limited per client address** (`RateLimits:OneTimeCode`, 30 in 15
  minutes, on every request that presents such a code, a registration with an office invitation included), **not per
  code:** a wrong code finds no invitation, so there is nothing to count against, and the code's 59 bits with that limit
  put guessing out of reach (at the limit, one address would need on the order of 10¹¹ years for an even chance against
  one code). One helper makes, shapes and hashes every such code (`OneTimeCode`, SharedKernel, also compiled into the
  browser's assembly).
- **Forwarded addresses are believed only from the proxy itself.** On a shared Docker network the proxy's
  neighbours are not the proxy: the application believes the forwarding headers only from the proxy's own address,
  named in its configuration (`ReverseProxy:Address`), not from a whole network.
- **A signed-in page is not stored.** HTML pages answered to a signed-in session carry `Cache-Control: no-store`,
  as `/api` does: such a page can hold the person's own side or invitation code (6.4). The site's own headers leave a
  visitor's page alone (`SecurityHeaders`: it is the same for everyone and may be kept), but a page with an island
  issues an antiforgery token, which ASP.NET Core marks `no-cache, no-store`, so in practice visitors' pages are not
  stored either; making them cacheable would be its own change.
- **A page that parses to nothing changes nothing.** When the agenda watcher finds sitting dates but no bill at all
  while planned items exist, it keeps the items, records the run as failed and alerts (8.3, 8.4).

## 6.9 Rules for closed bills, staff accounts and settings

- **Reports after the final vote (8.3, 8.12).** A report about an argument or a description on a closed bill reaches
  the moderators' queue like any other. On a closed record a moderator can hide and restore an argument, correct or
  remove the description, and answer the report with "Palikti", each with a statement, audited, the author told, as
  on an open one. The whole record of a closed bill can be hidden too, and a close a moderator made (a bill rejected or
  withdrawn, 8.1 step 6) can be undone. The frozen counts and the summaries already made do not change. Everything else
  on a closed record stays refused.
- **Party numbers (8.2).** Each party's number is shown when that party has at least `Digest:PartyGroupMinimum` people
  (default 10, 0 = no floor; 8.4). A side's party number is also withheld when the rest of that side is a non-empty
  group under the same minimum, so the rest cannot be read by subtraction from the side's total shown beside it
  (`GroupRules.PublishableBeside`); with the setting at 0 or 1 that check withholds nothing. That a smaller group can
  sometimes be worked out by subtraction in other ways is accepted.
- **A statement still waiting when the bill closes (8.14.14).** It can no longer be published: it was not public when
  the bill closed, and what is public then is frozen. The queue still shows it, with "Palikti" and hiding only.
- **A moderator's or administrator's actions outlive the account (6.5).** When a staff member's account is deleted,
  the records of what they did as staff (moderation actions, statements, restrictions, confirmations) keep the
  account's id, for accountability; the id then leads to no person, name or address. The methodology page and a staff
  account's delete confirmation say so. Where a screen names who hid something, a deleted account reads "paslėpė
  moderatorius". A moderator's AI draft requests (a rate-limit table, each also audited) go with the account (8.11.4).
- **The read-only database role sees only what is listed (5.5).** `lt2_readonly` is granted the tables on a list
  (`DatabaseRoles.ReadableTables`), never "every table but". A new table is invisible to it until someone adds it, and a
  test fails when a table is on neither the granted list nor the withheld list (`DatabaseRoles.WithheldTables`), so each
  new table gets a decision.
- **Deleting a staff account needs the staff passkey (6.2).** An account that holds an activated staff role can be
  deleted only from a session signed in with its staff passkey; a session from an email code is told so. A role granted
  but never activated, or an account an administrator has reset, has no staff passkey to sign in with and is deleted
  like an ordinary account. Ordinary accounts and the data export are unaffected.
- **Locking an account asks for the passkey again and is the administrators' (8.3).** Like changing roles it needs a
  staff-passkey session with a fresh confirmation, and never a moderator's; a staff account is locked only after its
  roles are inactive (reset), and no administrator locks their own account.
- **Changing a setting asks for the passkey again (6.2).** Saving a runtime setting needs the same fresh passkey
  confirmation as changing roles. Switching an invitation code off or on does not.
- **Feedback handling is audited (8.10).** A change of a feedback item's status or note leaves an audit entry with
  the item, the old and new status and who did it, without the note's or the feedback's text.
- **Signing in from a bill brings the person back to it (8.11.5).** The sign-in links on a bill's page (taking a
  side, following, reporting) carry the page as `tikslas`, after the `#` like every such link (6.8), so signing in or
  registering ends on that bill, not on the account page.
