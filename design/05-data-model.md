# 5. Data model

Schemas match the modules. Only the tables that carry design decisions are described; every table is on the read-only
role's list of readable or withheld tables (5.5).

## 5.1 `parliament`

| Table | Key columns | Notes |
|---|---|---|
| `term`, `session`, `sitting` | Seimas identifiers as keys | `kadencijos_id = 10` is 2024–2028. |
| `person` | `id`, `seimas_person_id` (unique), `first_name`, `last_name`, `slug`, `gender` | `asmens_id` is stable across terms, so one person has one profile for life. The slug (`/p/jonas-petraitis`) is set once and never changed. |
| `mandate` | `person_id`, `term_id`, `from`, `to`, `nominating_party`, `elected_how` (list or constituency), `constituency`, `terms_served` | `to` not null means the mandate ended; the profile becomes "former MP". |
| `body` | `id`, `kind` (faction, committee, commission, group, other), `seimas_id`, `term_id`, `name`, `slug` | |
| `membership` | `person_id`, `body_id`, `role`, `from`, `to` | With dates, so "faction at the time of the vote" can be answered. |
| `contact` | `person_id`, `kind`, `value` | Official email and phone from open data: public data about a public office. |
| `bill` | `id`, `number` (`XVP-0412`), `title`, `e_seimas_url` | Created from agenda items. |
| `agenda_item` | `id` (Seimas item id), `sitting_id`, `number`, `bill_id`, `stage`, `title` | Items of sittings that have started (open data). |
| `planned_item` | `bill_number`, `planned_date`, `stage`, `source` (`agenda` or `moderator`), `source_url`, `first_seen`, `last_seen`, `gone_at`, `confirmed_by`, `note` | Upcoming agenda, read from the lrs.lt pages; moves often. A row from the page is replaced when the date or stage moves (the old row gets `gone_at`); a moderator's row takes precedence over the page's for the same bill until a moderator changes it. |
| `agenda_snapshot` | `url`, `sha256`, `body`, `fetched_at`, `failure`, `rows`, `synthetic` | Every agenda page as fetched, kept raw, then parsed. |
| `vote` | `id` (Seimas vote id), `agenda_item_id`, `bill_id`, `held_at`, `wording`, `type`, `kind`, `stage`, totals (`for`, `against`, `abstain`, `voted`, `seats`), `official_note`, `is_consensus`, `has_result`, a moderator's correction (`kind_override`, `stage_override`, `overridden_at`, `overridden_by`, `override_statement`) | `kind`: stage result, amendment, procedural, other. `kind` and `stage` are the classification in force, the correction when there is one, otherwise the rules' (7.2); the import never touches the correction. Consensus votes have no ballots. |
| `ballot` | `vote_id`, `person_id`, `faction_id`, `faction_label`, `choice` | About 141 rows per vote; a few million rows per term. `faction_label` is the faction as the result states it. |
| `faction_position` | `vote_id`, `faction_label`, `faction_id`, `position` or "free", counts | Derived: the option chosen by at least two thirds of the faction's members who voted. Only for factions: the mixed group and MPs outside a faction have no row. |
| `mp_stat` | `person_id`, `term_id`, votes held, votes taken part in, votes with a faction position, with-faction count, free-vote count | Derived, recomputed after each import that changed data. |
| `import_run`, `raw_document`, `fetch_state` | source address, request, fetch times, hash, compressed body, `synthetic`; per request the stored response, when it was parsed and by which parser version | Every distinct response is kept, so parsing can be re-run and any number traced to its source; a newer parser applies stored responses again. `synthetic` marks what came from the stand-in. |

## 5.2 `identity`

ASP.NET Core Identity tables (`user`, `role`, claims, logins, tokens, `user_passkey`), `data_protection_key`, plus:

| Table | Notes |
|---|---|
| `profile` | `display_name`, created and registered times, normalised email (for the one-account-per-address rule), `verification_level` (`none` while pending, then `email`), `staff_passkey_id`. While the account is pending (6.2) it holds only the normalised address and the invitation code that was entered (`invitation_code`), never an organisation's code, which the browser keeps until completion; `invitation_id` is the invitation counted on completion, and `invitation_level` the level of the code the registration was completed with. For an organisation's code the level is all that is kept: `invitation_id` and `invitation_code` stay empty. `about`, the optional public text "Apie mane" (up to `Texts:MaxLength`), with `about_changed_at`, `about_hidden_at` and `about_hidden_statement`; `public_profile_id` (a random short id while the member's public profile is on, null otherwise) and `public_since`; `locked_at` (set while an administrator's full lock stands, 8.3: no sign-in, no public profile, no follow emails) with `locked_statement` (the administrator's statement, for the list of locked accounts; also in the audit log and the email; cleared when unlocked). |
| `retired_profile_id` | An id of a profile switched off or of a deleted account: never issued again, its address answers 410; no account is kept with it (8.25.1). |
| `profile_report` | A member reported a public profile ("Apie mane" or impersonation): target and reporter accounts, ground, text, status (`open`, `closed`). Kept by Identity, which owns the profile (8.25.6). |
| `invitation` | `code` and its look-up form without dashes or spaces (`lookup`), `kind` (`team`, `personal` or `organisation`), who issued it (for a personal code: its member; never set for an organisation's code), uses left and expiry (team codes), completed registrations (`used_count`, a counter that never drops), `level`, switched off (when, by an administrator), replaced (when). For an organisation's code, one current code per organisation: `organisation_id` (the Appeals module's id) and `organisation_off` (`blocked` or `closed`), which follows the organisation, separate from an administrator's switch-off. Registration is by invitation while `Registration:RequireInvitation` is on. A use is counted when the registration is completed (6.2), not when it is started. |
| `invitation_daily` | Per code and Vilnius day: registrations completed. For the administrators' chart; no per-person history. |
| `feedback` | Kind (`bug`, `idea`, `unclear`, `other` or none), text (up to 2 000 characters), page path and browser (each only if the member left them in), status (`new`, `seen`, `done`), internal note, replies, the day it came, and the account **only if** the member ticked "Galite man atsakyti". An item without that tick has no account, a random id and only the day, so nothing leads back to its author. Kept `Feedback:KeepMonths` months; linked items go with the account (8.10). |
| `sign_in_code` | Account, hashed code, expiry, failed attempts, when it was used or replaced (`consumed_at`), and the known browser it was issued to (`known_device_id`; empty for a code any browser may use). |
| `bot_check_use` | A proof-of-work challenge that has been used (6.4): the signature (HMAC of the challenge, random, tied to no address, browser or account) and the challenge Account, `identifier_hash` (HMAC-SHA-256 of the browser cookie's identifier under its own key, the secret `device_hash_key`), `label` (the browser and system as the sessions list says them, "Edge, Windows"), first and last seen. |
| `session` | One signed-in browser: account, the protected authentication ticket, created, last seen and expiry times, `method`, a coarse `device` description, and for a passkey session the passkey and when it was last confirmed. The cookie holds only the session's id. |
| `role_activation` | Account, role, hashed one-time code, who issued it, expiry, used time. Using it marks the passkey of that session as the staff passkey. |
| `party_support` | `user_id`, `party` (the party as named by the MPs' nominating party in the open data), `given_at`. Private and optional; used only for the summaries' party group numbers, from the size `Digest:PartyGroupMinimum` (8.4), and shown on the person's public profile only by their own box and consent `party-public` (8.25.9). |
| `consent` | Kind, text version, given and withdrawn times. Kinds: `age-18-or-older`, `terms` (8.22.2), `positions` (positions need explicit consent, GDPR Art. 9), `public-profile` (given by switching "Viešas profilis" on and withdrawn by switching it off; it covers the page, its indexing, being followed and the shown follower number, 8.25.1) and `party-public`. |
| `politician_link` | `user_id`, the Seimas's person number, `part` (`mp` or `office`), how and when verified (`verified_by`: `takeover-link` or `office-invitation`), who added an office member, when, by whom and why it ended. One MP per account; one account as the MP per person (8.5.2, 8.5.3). |
| `politician_token` | The hashed one-time token of a take-over link or of an office member's invitation (`kind`), for which person, by whom, expiry, used time (8.5.2, 8.5.3). |

## 5.3 `appeals`

| Table | Key columns | Notes |
|---|---|---|
| `bill_record` | `id`, `bill_number`, `description`, `description_source`, `plain_title`, `description_url`, `description_at`, `status`, `opened_at`, `published_at`, `closed_at`, `closing_vote_id`, `closed_reason`, `closed_decided_on`, `closed_source_url`, `last_automatic_rejection_vote_id`, frozen counts (`closed_for`, `closed_against` and each for accounts older than 30 days), `sending` (`automatic` or `held`), `threshold`, `threshold_set_by` (`default` or `moderator`), `threshold_note`, `first_summarised_at`, `last_activity_at` | The bill's record (8.12.1): the Appeals module's own record of what people did on one bill, created when first needed (the first side, argument or description, or a moderator's action), never proposed or approved. One per bill: a unique partial index over the statuses `open`, `closed` and `rejected` (hidden by a moderator, 8.12.3). The description is empty until a moderator writes one or the summary job makes one; `description_source` (`moderator`, `ai-draft` or `ai`, 8.13) is null without one. `plain_title` is the readable title, kept with the description under the same source and history (8.13.1). `description_url` is the official text it was made from when known: set by the summary job, kept when a moderator confirms or corrects that text, cleared on removal; a moderator who writes or corrects a description sets it from the form's "Oficialaus teksto adresas" (https on `lrs.lt` or under it, at most 500 characters), filled in advance with the current one; empty clears it. `description_at` is when its text was made or last changed. `threshold` is null for the default of all bills, or a moderator's number for this bill (8.12.2). `first_summarised_at` is set by the first summary really sent to MPs, not by a dry run (8.4). `last_activity_at` is the bill's latest activity (8.12.5), one time per bill, nothing per person. A closed record says why in `closed_reason` (8.1 step 6): `vote` (the closing job, at the final vote `closing_vote_id`), or `rejected` or `withdrawn` (the closing job or a moderator recorded a rejection, or a moderator recorded a withdrawal; `closing_vote_id` null), with `closed_decided_on`, the day of that decision, and `closed_source_url`, the source on lrs.lt (the rule of `description_url`, required); all three are null while open, and reopening clears them with `closed_at` and the frozen counts. `last_automatic_rejection_vote_id` is the explicit rejection vote last handled: set when the closing job closes on it, and by a moderator's reopen to the rejection the open data holds then (whoever had closed the record); it stays on reopen, so the same vote cannot close the record again. The reason `rejected` is not the status `rejected` (hidden). |
| `position` | (`record_id`, `user_id`) primary key, `side` (for or against), created and changed times, `member_since` | One row per person per bill; a changed side overwrites it. `member_since` is when the person's registration was completed, copied from Identity, so the count for accounts older than 30 days (6.6) is read from this table alone. |
| `daily_stat` | `record_id`, `day`, counts per side, counts for accounts older than 30 days, share of new accounts, `computed_at` | For the trend chart and surge detection; a row computed after its day ended is final. **No per-person history of opinion changes is stored.** |
| `reason` | `id`, `record_id`, `side`, `author_user_id`, `text` (any length), `status`, `merged_into_id`, `screening_flags`, `seen_at`, `created_at`, `edited_at`, `first_public_at` | Status: pending (flagged by pre-screening, waits for a moderator), published, hidden, merged. `side` is the side the argument supports, chosen by its author, whatever the author's own position (8.2). How long a text may be is the members' texts setting `Texts:MaxLength` (5.6), checked where an argument is written; the column sets no limit. `seen_at`: when a moderator last looked at it; only seen arguments may appear in a summary (8.3). `edited_at`: its author's last edit (8.2), no earlier text kept. `first_public_at`: when it first became public (the follow line of 8.25.5 and the member's page read it). |
| `endorsement` | (`reason_id`, `user_id`), `record_id`, created time | "Man svarbu" on a reason of either side, never on a reason you wrote; it counts only while the giver's `position.side` is the reason's side, read by a join when counted, never stored (8.2). How many per bill (counted or not) is `Appeals:EndorsementsPerPerson`, no limit by default (8.2). No constraint ties it to a side. |
| `mp_response` | `bill_number`, `seimas_person_id`, `stance` (for, against, undecided), `text` (any length; its length is `Texts:MaxLength`, 5.6, checked where it is written), `status` (published, hidden, withdrawn), published, edited, announced and withdrawn times, `written_by_user_id`, `screening_flags` and `seen_at` (the queue), `hidden_at` and `hide_statement` (the statement shown in its place) | One per MP per bill: the unique index holds whatever the status, so a response written again after a withdrawal is the same row. Earlier versions are kept in `revision` (`entity_kind` `response`, field `version`, as JSON, with no person) and are public (8.5.5). Withheld from the read-only role. |
| `report` | `target_kind` (`record`, `reason`, `organisation`, `statement`, `response`, `comment`), `target_id`, `record_id`, reporter, ground, text, status (`open`, `closed`) | Notice-and-action under the Digital Services Act. A member's public profile and its "Apie mane" are reported to Identity, which owns them: `identity.profile_report` (8.25.6). |
| `flag` | `record_id`, `kind` (`date-wrong` or `sooner`), `link`, `note`, `proposed_date`, reporter, status | A participant says the planned date is wrong or the bill is voted sooner, with a link to the source (8.4); a moderator corrects the date or keeps it, and the flag closes. One open flag per person per bill. |
| `comment` | `id`, `target_kind` (`reason`, `statement`, `response`), `target_id`, `bill_number`, `speaks_as` (`person`, `organisation`, `mp`) with `organisation_id` or `seimas_person_id`, `author_user_id`, `text` (up to `Texts:MaxLength`), `status` (published, hidden), created, edited and hidden times | A remark in "Aptarimas" under an argument, a statement or a response (8.23.11): a member's, or an organisation's or MP's under its own text. Its author may edit it until someone agrees (8.23.3); no earlier text is kept. Deleted for good when deleted or with its target (8.23.7); a person's own go with the account, and the writer behind an organisation's or MP's comment is emptied with the account. Withheld from the read-only role. |
| `comment_agreement` | (`comment_id`, `user_id`), created time | "Sutinku" on a comment (8.23.10), public with the names of those who agreed; tied to no side, so no positions consent. Deleted with the comment and the account. Readable by the read-only role, as `reason` is. |
| `moderation_action` | moderator, target, action, statement of reasons, time | The statement is sent to the author. |
| `writing_restriction` | `id`, `user_id`, `ends_at` (null = until lifted), statement, set by and when, lifted by, when and why | A moderator's restriction on writing (8.3). Lives with the writing rules in this module; deleted with the account; never shown to anyone but the person and staff. |
| `revision` | entity kind and id, field, old and new value, who, when | Edit history of bills' descriptions and thresholds, MPs' responses and organisations' statements (as versions), and organisations' names and websites. A description's entry is the rows of one time: the text, the readable title, the official text's address, a confirmation (the source `ai` → `ai-draft`) and the public statement (8.13). Never used for positions. "Who" is empty for a summary the job made (8.13); a description a person ever wrote, corrected or removed is recognised by a row with a person, and the job never writes it again. |
| `bill_card` | `bill_number` (key), `title`, `plain_title`, `title_folded`, `plain_title_folded`, `search_folded`, `search_vector`, `search_vector_folded` | The search copy of the bill catalogue (8.12.5, "How bills are searched"): the official title refreshed from the Parliament module after every import and at the worker's start, the readable title written with the record's (8.13.1). `title_folded` is the number and the official title, `plain_title_folded` the readable title, each in lower case without Lithuanian letters and with single spaces (`SearchText.Fold`, written with the row); `search_folded` is generated from both (a trigram index, `pg_trgm`, created in the `appeals` schema); `search_vector` is generated from the number and both titles as they stand with the `lithuanian` configuration (a GIN index); `search_vector_folded` the same from the folded texts, so a word typed without Lithuanian letters finds its other forms (a GIN index). A refresh adds the missing cards, brings the official titles current and removes a card whose bill is gone, never touching `plain_title`; a search that finds fewer cards than bills makes the refresh itself. Public data. |
| `summary_attempt` | `bill_number` (key), `tried_at`, `outcome` (`made`, `unavailable`, `insufficient`, `failed`, `overtaken`), `asked_provider`, `retry_after` | The summary job's last try per bill (8.13), also for a bill without a record; never a text and no person. Serves the daily number (tries that asked the provider since Vilnius midnight) and the wait before the next try. |
| `ai_draft_request` | `id`, `moderator_user_id`, `bill_number`, `requested_at` | That a moderator asked for an AI draft (8.11.4): never a text. For the hourly limit and to accept a description as started from a draft (8.12.3). Exported and deleted with the moderator's account. |
| `organisation` | `id`, `slug`, `name`, `name_key`, `description`, `website`, `legal_code`, `declared_lobbyist`, `registered_by_user_id`, `status`, confirmation and block columns, `kept_out_of_summaries_at`, `kept_out_by_user_id`, `kept_out_statement` (8.14.9), `screening_flags`, `seen_at` | An organisation's public profile; the columns are listed in 8.14.10. Who registered it is never public and is emptied with the account. Withheld from `lt2_readonly`. |
| `organisation_representative` | `id`, `organisation_id`, `user_id`, `granted_by_user_id`, `granted_at`, `ended_at` | Who acts for an organisation; seen only by its other representatives. Deleted with the account; the last representative's deletion closes the organisation. Withheld from `lt2_readonly`. |
| `organisation_invite` | `id`, `organisation_id`, `token_hash`, `created_by_user_id`, `created_at`, `expires_at`, `used_at` | A representative's one-time link (seven days). Withheld from `lt2_readonly`. |
| `organisation_code` | `id`, `organisation_id`, `domain`, `code_hash`, `created_at`, `expires_at`, `failed_attempts`, `consumed_at`, `requested_by_user_id` | Confirmation by email domain, under the sign-in codes' rules; never the mailbox. Withheld from `lt2_readonly`. |
| `organisation_statement` | `id`, `organisation_id`, `bill_number`, `stance`, `text` (any length; its length is `Texts:MaxLength`, 5.6, checked where it is written), `link`, `goal`, `status`, `screening_flags`, `seen_at`, `published_at`, `edited_at`, `withdrawn_at`, `first_public_at`, `written_by_user_id` | One per organisation per bill among the statuses pending, published and hidden (8.14.3); never counted. Earlier versions in `revision` without a person. `first_public_at`: when it first became public, for the follow emails. `written_by_user_id` is never public and is emptied with the account. Withheld from `lt2_readonly`. |

## 5.4 `notify`

| Table | Notes |
|---|---|
| `outbox` | Kind, `jsonb` payload, due time, attempts, last attempt, error, failed time. Written in the same transaction as the change; dispatched with `FOR UPDATE SKIP LOCKED`. |
| `stream_send` | One row per email sent: its stream (`codes`, `notices`, `mp`) and the time; no address, no kind. Counted over 24 hours for the streams' daily ceilings (8.19.3), purged after two days. |
| `digest_run` | Kind (`weekly`, `before-vote`, `urgent`, `introduction`), the day it goes and the sitting it is for, created and due times, status (`prepared`, `dry-run`, `sent`, `cancelled`, `dropped`), who approved or declined it and when, the statement when declined, how many MPs, when it went, the introduction letter's version. |
| `digest_choice` | Per run: an argument chosen for a prepared summary, its side and place, whether it came in for a hidden one, and when it left (8.19.2). No author. |
| `digest_item` | Per run and bill's record (`record_id`): bill number and title, stage, planned date, counts per side, counts for accounts older than 30 days, the surge note's share and window, top reasons (text and "Man svarbu" count, without the author's name), the published arguments per side (`for_reason_count`, `against_reason_count`), the organisations named (`organisations`, JSON: stance, organisation id, slug, name, the statement linked; never its text; 8.14.4). A frozen snapshot: what MPs were told; null columns in an older one show nothing. |
| `digest_delivery` | Per run, MP and recipient (`official`, `extra`, or `office` with the office member's account): status (`dry-run`, `queued`, `sent`, `failed`, `bounced`, `skipped-ceiling`, `skipped-preference`, `suppressed`, `introduced`) and the MP-specific party numbers. Holds no address. |
| `mp_email_pref` | Per MP: frequency (`default`, `weekly`, `before-vote`, `stopped`), extra recipient's address and when it was confirmed, the links' generations. |
| `office_copy` | Per office member and MP: the member's own choice of copies of the MP's summaries and the hash of the member's own stop link (8.19.3). Deleted when the office link ends or the account goes. |
| `suppression` | Keyed hashes of addresses that bounced, complained or were added by an administrator (with the statement). |
| `mp_mail_token`, `introduction_letter` | The MP's link tokens (`preferences`, `extra-confirm`, `extra-stop`) and the introduction letter's versions (8.19.4, 8.19.5, 8.19.7). The statuses of `digest_run` and `digest_delivery` for real sending are in 8.19.1 and 8.19.3. |
| `follow` | (`user_id`, `kind`, `key`) primary key, created time, `last_seen_at`. `kind` (up to 16 characters): `bill` (key: the number), `mp` (key: the Seimas person id), `organisation` (key: the organisation's id), or `member` (key: the public profile id, only while that member's profile is public; ended when it is switched off, 8.25.5). Who follows is shown to nobody but the person; exported and deleted with the account (8.11.2). How many follow each thing is public: a number counted from this table when an answer is made, nothing per follower (8.11.2). `last_seen_at`, a bill's follow only: when the person last opened the bill's page, from the follow's own time (8.11.8). |
| `notice_setting` | Per account: which follow emails are on (`planned`, `voted`, `statements` (a followed organisation's statement became public), `responses` (8.20.1), `comments` (an argument's author told of new comments under it, 8.23.9), `members` (a followed member published something, 8.25.5)), and `frequency` (`immediate`, `daily`, `weekly`), the member's email rhythm (8.24), with `frequency_since`, `previous_frequency` and `previous_since`. No row means all on and `immediate`. |
| `bill_event` | (`bill_number`, `kind`, `key`) primary key, first seen, sent time, `outcome` (`waiting`, `sent`, `skipped`, `superseded`), and `daily_at`, `weekly_at`, `early_at`, the marks of the gathered emails (8.24). What followers are told about, recorded once (8.11.2). `kind`: `planned` (key: the date), `voted` (key: the vote id), `statement` (key: the statement's id: a followed organisation's statement became public), `response` (key: the person number and when the stance became public), `member` (key: the text's id: a followed member's argument or own-voice comment became public), `comment` (key: the argument's id and the run's moment: a run found comments under an argument, 8.23.9, 8.23.14), `closed` (key: the moment the record closed because the bill was rejected or withdrawn, 8.1 step 6). No person and no text in it. An index on (`kind`, `first_seen`); rows older than 30 days are deleted by the daily housekeeping, a planned date only once past. |
| `rhythm_pass` | (`pass`, `period`), `made_at`: the daily email of a day or the weekly of a week was made (8.24). No person. |
| `member_mail_token` | Per account: the stop link's SHA-256 (unique), its generation and when it was made; the token itself is derived under the mail key and never stored (8.24.1). Deleted with the account; not exported; withheld from `lt2_readonly`. |

**Not built.** An `email_message` table (template, subject, provider message id, status): no email is recorded once
sent; bodies of summaries are reproducible from the snapshot.

## 5.5 `audit`

`event`: time, actor, role, action, target, details, hashed IP. The application role may only insert.

Database roles: `lt2_migrator` owns the schemas and runs migrations; `lt2_app` has data access only; `lt2_worker` the
same, without `usage`, plus the Hangfire schema `hangfire`, whose tables `lt2_app` also reads and writes, never creates,
for the administrators' dashboard (3, 6.3); the migrator grants that with the superuser's connection, since the worker
owns them. `lt2_readonly`, for ad-hoc analysis, reads exactly the tables listed as readable (`DatabaseRoles.ReadableTables`:
the public parliament data, the bills' records with their arguments, reports, flags, moderation actions, history, the
daily counts, `comment_agreement`, the settings and the usage counts) and nothing else; positions, endorsements, every
`identity` table, the organisation tables, MPs' responses, comments, writing restrictions, `notify`, the audit log and
the usage sketches are withheld (`DatabaseRoles.WithheldTables`). A test fails until a new table is on one of the two
lists.

## 5.6 Settings

`config.setting`: key, `jsonb` value, who changed it and when. Holds every number an administrator may need to tune
without a deploy:

- **Registration and sign-in:** whether registration needs an invitation (`Registration:RequireInvitation`, on), whether
  an MP's official address may register without one (`Registration:OfficialAddressesWithoutInvitation`, off, 8.5.1),
  uses per personal invitation code (`Registration:PersonalCodeUses`) and per organisation's code
  (`Registration:OrganisationCodeUses`), both 0 = no limit, the default.
- **Appeals:** the threshold of all bills (`Digest:ThresholdDefault`, 100, read whenever it is used) and the minimum no
  threshold goes below (`Digest:ThresholdMinimum`, 50; 8.12.2); the reasons and "Man svarbu" marks a person may give per
  bill (`Appeals:ReasonsPerPerson`, `Appeals:EndorsementsPerPerson`, 0 = no limit, the default; 8.2); the fewest words of
  an argument (`Appeals:MinReasonWords`, 3; 0 = off).
- **Members' texts** ("Narių tekstai"): the longest text members write for others to read (`Texts:MaxLength`,
  characters, 2 000; 0 = no limit but the technical ceiling of 20 000): arguments (8.2), organisations' statements
  (8.14.3), MPs' responses (8.5.5), comments and "Apie mane". Not a bill's description (a moderator writes it and it is
  reviewed anyway), a moderator's statement, a report, feedback, a display name, an organisation's name and description,
  or a statement's link and goal, which keep their own limits.
- **Organisations** ("Organizacijos"): how many organisations an account may register (`Organisations:PerAccount`, 3;
  closed ones do not count, 8.14.14).
- **Comments** ("Komentarai"): whether comments are on for arguments, statements and responses (`Comments:OnReasons`,
  `Comments:OnStatements`, `Comments:OnResponses`, all on) and how many comments a person may write under one text
  (`Comments:PerPersonPerTarget`, 0 = no limit; 8.23.4, 8.23.5).
- **AI** ("Dirbtinis intelektas"): the Claude model of the description draft and of the AI summaries (`Ai:DraftModel`,
  `claude-sonnet-5-5`, 8.11.4, 8.13) and how many AI summaries the job may ask for a day (`Ai:SummariesPerDay`, 30;
  0 = off, 8.13).
- **Summaries** (8.4): whether they really go to MPs (`Digest:Sending`, `off` or `automatic`, 8.19.1), the hours they
  are prepared before their time (`Digest:ReviewHours`, 24), the weekly and before-vote times (`Digest:Weekly`, 07:00;
  `Digest:BeforeVote`, 12:00; each may be `off`), the ceiling per MP a week (`Digest:MaxPerWeek`, 2), the share of
  change that counts as "changed notably" (`Digest:NotableChange`, 10 %), and the smallest party group whose number a
  summary shows (`Digest:PartyGroupMinimum`, 10; 0 = no floor).
- **Follow emails:** the morning time of the day's email of MPs' answers (`Follows:ResponsesAt`, 08:00; 8.20.1).
- **Surge note** (6.6): `Surge:MinimumShare` (30 %), `Surge:MinimumAccounts` (50), `Surge:WindowHours` (72).
- **Feedback:** how long it is kept (`Feedback:KeepMonths`, 12).
- **Security limits** ("Saugumo ribos"): sign-in codes per hour and per day (`SignIn:CodesPerHour`, 5;
  `SignIn:CodesPerDay`, 10), the days a pending account is kept (`Registration:PendingAccountDays`, 7; 6.2) and the bot
  check (6.4): whether it is on (`BotCheck:Enabled`, on), its difficulty, the largest secret number of a challenge
  (`BotCheck:MaxNumber`, 20 000, from 1 000 to 1 000 000) and the minutes a challenge may be used in
  (`BotCheck:ChallengeMinutes`, 10).

Each change is written to `audit`, and the current values of the rules that affect what MPs receive are shown on the
public methodology page (`/metodika`).

The site's own address is not one of them: `Site:Address` is deployment configuration, which compose builds for the web
and the worker from `LT2_SITE_ADDRESS` and the https port (the address Caddy serves), so no administrator can point
emails at another site. It must be empty or an https address of a host and nothing more; a host refuses to start
otherwise. Links in every email and in the summary's text start with it; when it is empty they stay the site's paths.

The administrator's settings screen groups them under the headings above, in the order registration and sign-in;
appeals; members' texts; organisations; comments; AI; summaries; follow emails; surge note; feedback; and "Saugumo
ribos" last, for the ones rarely changed, which says so. A setting the screen does not place goes under "Kiti". Each
setting shows a plain Lithuanian label, one sentence on what it does and when to change it, its unit, the default, and
the key in small type. The screen says that the minimum threshold's 50 is a placeholder.

## 5.7 `usage`

The site's own usage statistics (8.27). `sketch`: per period (`day`, `month`, `year`, by its first Vilnius day) and
audience (`visitor`, `member`), the random salt and the HyperLogLog registers of a period under way; deleted when the
period ends; withheld from `lt2_readonly`. While it lasts, a candidate visitor or account can be tested against it
(8.27.2). `total`: per period, distinct visitors and members (approximate) and page views of visitors, members and
robots (`crawler_views`). `page`: views per Vilnius day and path. `referrer`: visitors per Vilnius day and referring
host. These three name no person. Only `lt2_app` reads and writes the schema.
