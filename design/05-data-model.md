# 5. Data model

Schemas match the modules. Only the tables that carry design decisions are listed.

## 5.1 `parliament`

| Table | Key columns | Notes |
|---|---|---|
| `term`, `session`, `sitting` | Seimas identifiers as keys | `kadencijos_id = 10` is 2024–2028. |
| `person` | `id`, `seimas_person_id` (unique), names, `slug`, `gender`, photo reference, biography link | `asmens_id` is stable across terms, so one person has one profile for life. |
| `mandate` | `person_id`, `term_id`, `from`, `to`, nominating party, how elected (list or constituency), constituency | `to` not null means the mandate ended; the profile becomes "former MP". |
| `body` | `id`, `kind` (faction, committee, commission, group), `seimas_id`, `term_id`, name | |
| `membership` | `person_id`, `body_id`, `role`, `from`, `to` | With dates, so "faction at the time of the vote" can be answered. |
| `contact` | `person_id`, `kind`, `value` | Official email and phone from open data: public data about a public office. |
| `bill` | `id`, `number` (`XVP-0412`), official title, registered date, proposer, e-seimas link, status, `lead_committee_id` | Created from agenda items; enriched from e-seimas. |
| `agenda_item` | `sitting_id`, Seimas item id, `bill_id`, stage, title | Items of sittings that have started (open data). |
| `planned_item` | `bill_id`, planned date, stage, source link, `first_seen`, `last_seen`, `gone_at`, `confirmed_by` | Upcoming agenda, scraped from lrs.lt; moves often. A moderator can correct or confirm a row. |
| `vote` | Seimas vote id, `agenda_item_id`, `bill_id`, time, wording, `kind`, stage, totals, official note, `is_consensus` | `kind`: stage result, amendment, procedural, other. Consensus votes have no ballots. |
| `ballot` | `vote_id`, `person_id`, `faction_id`, `choice` | About 141 rows per vote; a few million rows per term. |
| `faction_position` | `vote_id`, `faction_id`, position or "free", counts | Derived: the option chosen by at least two thirds of the faction's members who voted. Only for factions: the mixed group and MPs outside a faction have no row. |
| `mp_stat` | `person_id`, `term_id`, votes held, votes taken part in, with-faction count, free-vote count | Derived, recomputed after each import. |
| `import_run`, `raw_document` | source address, fetch time, hash, compressed body | Every response is kept, so parsing can be re-run and any number traced to its source. |

## 5.2 `identity`

ASP.NET Core Identity tables (users, roles, claims, tokens, passkeys), plus:

| Table | Notes |
|---|---|
| `profile` | Display name, created date, normalised email (for the one-account-per-address rule), `verification_level` (`email` at the start; `phone` and the EU wallet can be added), invitation used. While the account is pending (6.2) it holds only the normalised address and the invitation code that was entered. [changed while building (8.14.14, stage B): never an organisation's code, which the browser keeps until completion; `invitation_level` holds the level of the code the registration was completed with, and for an organisation's code it is all that is kept: `invitation_id` and `invitation_code` stay empty.] [built 2026-11-16 (8.25): `about`, the optional public text "Apie mane" (up to `Texts:MaxLength`), with `about_changed_at`, `about_hidden_at` and `about_hidden_statement`; `public_profile_id` (a random short id while the member's public profile is on, null otherwise) and `public_since`; retired ids kept in `retired_profile_id` without an account; reports of a profile in `profile_report` (8.25.6).] |
| `invitation` | Code (and its look-up form without dashes or spaces), kind (`team` or `personal`), who issued it (for a personal code: its member), uses left and expiry (team codes), completed registrations (a counter that never drops), level, switched off (when, by an administrator), replaced (when). Registration is by invitation during the closed trial. A use is counted when the registration is completed (6.2), not when it is started. [changed while building (8.14.14, stage B): the kind `organisation`, one current code per organisation, with `organisation_id` (the Appeals module's id) and no account; `organisation_off` (`blocked` or `closed`) follows the organisation, separate from an administrator's switch-off.] |
| `invitation_daily` | Per code and Vilnius day: registrations completed. For the administrators' chart; no per-person history. |
| `feedback` | Kind (`bug`, `idea`, `unclear`, `other` or none), text (up to 2 000 characters), page path and browser (each only if the member left them in), status (`new`, `seen`, `done`), internal note, replies, the day it came, and the account **only if** the member ticked "Galite man atsakyti". An item without that tick has no account, a random id and only the day, so nothing leads back to its author. Kept 12 months (a setting); linked items go with the account (8.10). |
| `sign_in_code` | Account, hashed code, expiry, failed attempts, used time, and the known device it was issued to (empty for a code any browser may use). |
| `known_device` | Account, hashed identifier of the browser's cookie, first and last seen. Built before registration opens (M2b). [changed while building (8.20.2): also `label`, the browser and system as the sessions list says them ("Edge, Windows"), so the person can tell the rows apart; the hash is HMAC-SHA-256 under a key of its own, the secret `device_hash_key`.] |
| `role_activation` | Account, role, hashed one-time code, who issued it, expiry, used time. Using it marks the passkey of that session as the staff passkey. |
| `party_support` | `user_id`, `party_id`. Private and optional; used only for the summaries' party group numbers, from the size `Digest:PartyGroupMinimum` (8.4; owner 2026-11-17, default 10, 0 = no floor), and shown on the person's public profile only by their own box and consent `party-public` (8.25.9, owner 2026-11-17). |
| `consent` | Kind, text version, given and withdrawn times. Positions need explicit consent (GDPR Art. 9). [built 2026-11-16 (8.25.1): the kind `public-profile`, given by switching "Viešas profilis" on and withdrawn by switching it off; it covers the page, its indexing, being followed and the shown follower number] |
| `politician_link` | `user_id`, the Seimas's person number, part (the MP, or an office member), how and when verified, who added an office member, when and by whom it ended. One MP per account; one account as the MP per person (8.5.2, 8.5.3). |
| `politician_token` | The hashed one-time token of a take-over link or of an office member's invitation, for which person, by whom, expiry, used time (8.5.2, 8.5.3). |

## 5.3 `appeals`

| Table | Key columns | Notes |
|---|---|---|
| `bill_record` | `id`, `bill_number`, description, `description_source`, `description_url`, `description_at`, status, opened, published and closed times, `closing_vote_id`, frozen counts, `sending` (automatic or held), `threshold`, `threshold_set_by` (default or moderator), `threshold_note`, `first_summarised_at`, `initiator_user_id` (old rows only) | The bill's record (8.12.1): the Appeals module's own record of what people did on one bill, created when first needed (the first side, argument or description, or a moderator's action), never proposed or approved. One per bill: a unique partial index over the statuses open, closed and rejected (hidden by a moderator, 8.12.3). Status `superseded` marks only rows from before 2026-11-14 that are no longer a bill's record (proposals closed when the proposals went, older hidden appeals). The description is empty until a moderator writes one or the summary job makes one; `description_source` (`initiator`, `moderator`, `ai-draft`, `ai`, 8.13) is null without one. [changed while building (8.13): `description_url` is the official text it was made from when known (set by the summary job, kept when a moderator confirms or corrects that text, cleared on removal) and `description_at` when its text was made or last changed.] [changed while building (8.13, the owner's
last block): a moderator who writes or corrects a description sets `description_url` from the form's "Oficialaus teksto
adresas" (https on `lrs.lt` or under it, at most 500 characters), filled in advance with the current one; empty clears it.] `threshold` is null for the default of all bills, or a moderator's number for this bill (8.12.2). `first_summarised_at` is set by the first summary really sent to MPs, not by a dry run (8.4). `initiator_user_id` is set only on records from before 2026-11-14 and only names the author beside a description they wrote. [changed while building: `return_note` and the description's `screening_flags` were dropped with the proposals] |
| `position` | (`record_id`, `user_id`) primary key, `side` (for or against), created and changed times | One row per person per bill. Becomes the direct LT2 vote in stage 3. |
| `daily_stat` | `record_id`, date, counts per side, counts for accounts older than 30 days, share of new accounts | For the trend chart and surge detection. **No per-person history of opinion changes is stored.** |
| `reason` | `id`, `record_id`, `side`, `author_user_id`, text (`text`, any length), status, `merged_into_id`, `first_public_at` (when it first became public: the follow line of 8.25.5 and the member's page read it; built 2026-11-16) | Status: pending, published, hidden, merged. `side` is the side the argument supports, chosen by its author, whatever the author's own position (8.2). How long a text may be is the members' texts setting `Texts:MaxLength` (5.6), checked where an argument is written; the column sets no limit (**owner, 2026-11-16**; migration `ReasonTextAnyLength`). |
| `endorsement` | (`reason_id`, `user_id`) | "Man svarbu" on a reason of either side, never on a reason you wrote (owner, 2026-11-17); it counts only while the giver's `position.side` is the reason's side, read by a join when counted, never stored (owner, 2026-11-18, 8.2); how many per bill (counted or not) is a setting, no limit by default (8.2). No constraint ties it to a side. |
| `mp_response` | bill number, the Seimas's person number, stance (for, against, undecided), text (`text`, any length; its length is the members' texts setting `Texts:MaxLength`, 5.6, checked where it is written; **owner, 2026-11-16**, migration `MemberTextsAnyLength`), status (published, hidden, withdrawn), published, edited and withdrawn times, `written_by_user_id` | One per MP per bill; versions are kept in `revision` and are public (8.5.5). Withheld from the read-only role. [changed while building: also `screening_flags` and `seen_at` (the queue), `hidden_at` and `hide_statement` (the statement shown in its place); the one-per-bill index holds whatever the status, so a response written again after a withdrawal is the same row; a version is kept as JSON in `revision` (`entity_kind` `response`, field `version`) with no person.] |
| `report` | target, reporter, ground, status | Notice-and-action under the Digital Services Act. [proposed: the target `comment` (8.23). A member's public profile and its "Apie mane" are reported to Identity, which owns them: `identity.profile_report`, built 2026-11-16 (8.25.6)] |
| `comment` | `id`, `target_kind`, `target_id`, `bill_number`, `speaks_as` (person, organisation, mp) with the organisation or the MP, `author_user_id`, text (up to `Texts:MaxLength`), status (published, hidden), created and hidden times | [built 2026-11-16, 8.23.11] A remark in "Aptarimas" under an argument, a statement or a response: a member's, or an organisation's or MP's under its own text. No editing and no history; deleted for good when deleted or with its target (8.23.7); a person's own go with the account. Withheld from the read-only role. |
| `comment_agreement` | (`comment_id`, `user_id`), created time | [built 2026-11-16, 8.23.10] "Sutinku" on a comment, public with the names of those who agreed (owner, 2026-11-16); tied to no side, so no positions consent. Deleted with the comment and the account. Readable by the read-only role, as `reason` is. |
| `moderation_action` | moderator, target, action, statement of reasons, time | The statement is sent to the author. |
| `writing_restriction` | `id`, `user_id`, `ends_at` (null = until lifted), statement, set by and when, lifted by, when and why | A moderator's restriction on writing (8.3). Lives with the writing rules in this module; deleted with the account; never shown to anyone but the person and staff. |
| `revision` | entity, field, old and new value, who, when | Edit history for descriptions, reasons, responses. "Who" is empty for a summary the job made (8.13); a description a person ever wrote, corrected or removed is recognised by a row with a person, and the job never writes it again. |
| `summary_attempt` | `bill_number` (key), tried time, outcome (`made`, `unavailable`, `insufficient`, `failed`, `overtaken`), whether the provider was asked, `retry_after` | [changed while building (8.13)] The summary job's last try per bill, also for a bill without a record; never a text and no person. Serves the daily number (tries that asked the provider since Vilnius midnight) and the wait before the next try. |

| `ai_draft_request` | `id`, moderator, `bill_number`, requested time | [changed while building] That a moderator asked for an AI draft (8.11.4): never a text. For the hourly limit and to accept a description as started from a draft (8.12.3). Exported and deleted with the moderator's account. |
| `organisation` | `id`, `slug`, `name`, `name_key`, `description`, `website`, `legal_code`, `declared_lobbyist`, `registered_by_user_id`, `status`, confirmation and block columns, `seen_at` | [changed while building (8.14.14, stage A)] An organisation's public profile; the columns are listed in 8.14.10. Who registered it is never public and is emptied with the account. Hidden from `lt2_readonly`. |
| `organisation_representative` | `id`, `organisation_id`, `user_id`, `granted_by_user_id`, `granted_at`, `ended_at` | [changed while building] Who acts for an organisation; seen only by its other representatives. Deleted with the account; the last representative's deletion closes the organisation. Hidden from `lt2_readonly`. |
| `organisation_invite` | `id`, `organisation_id`, `token_hash`, `created_by_user_id`, `created_at`, `expires_at`, `used_at` | [changed while building] A representative's one-time link (seven days). Hidden from `lt2_readonly`. |
| `organisation_code` | `id`, `organisation_id`, `domain`, `code_hash`, `created_at`, `expires_at`, `failed_attempts`, `consumed_at`, `requested_by_user_id` | [changed while building] Confirmation by email domain, under the sign-in codes' rules; never the mailbox. Hidden from `lt2_readonly`. |
| `organisation_statement` | `id`, `organisation_id`, `bill_number`, `stance`, `text` (`text`, any length; its length is `Texts:MaxLength`, 5.6, checked where it is written; **owner, 2026-11-16**, migration `MemberTextsAnyLength`), `link`, `goal`, `status`, `screening_flags`, `seen_at`, `published_at`, `edited_at`, `withdrawn_at`, `written_by_user_id` | [changed while building] One per organisation per bill (8.14.3); never counted. Earlier versions in `revision` without a person. `written_by_user_id` is never public and is emptied with the account. Hidden from `lt2_readonly`. [changed while building (stage B): `first_public_at`, when it first became public, for the follow emails.] |

## 5.4 `notify`

| Table | Notes |
|---|---|
| `outbox` | Kind, `jsonb` payload, due time, attempts, error. Written in the same transaction as the change; dispatched with `FOR UPDATE SKIP LOCKED`. |
| `digest_run` | Kind (weekly, before-vote, urgent, introduction), scheduled time, status, who approved. |
| `digest_item` | Per run and bill's record (`record_id`): counts per side, counts for accounts older than 30 days, top reasons (text and "Man svarbu" count, without the author's name), planned date and stage. A frozen snapshot: what MPs were told. |
| `digest_delivery` | Per run and MP: sent, skipped by preference, suppressed, bounced; the MP-specific numbers. |
| `mp_email_pref` | Per MP: frequency (default, weekly only, before votes only, stopped), confirmed extra recipient. |
| `suppression` | Hashed addresses that bounced or asked to stop. |
| `mp_mail_token`, `introduction_letter` | The MP's link to the preferences page and the introduction letter's versions (8.19.4, 8.19.5, 8.19.7). The statuses of `digest_run` and `digest_delivery` for real sending are in 8.19.1 and 8.19.3. |
| `email_message` | Template, subject, provider message id, status. Bodies of digests are reproducible from the snapshot; other bodies are not stored. |
| `follow` | (`user_id`, `kind`, `key`) primary key, created time. `kind`: bill (key: the number) or mp (key: the Seimas person id). Who follows is shown to nobody but the person; exported and deleted with the account (8.11.2). How many follow each thing is public: a number counted from this table when an answer is made, nothing per follower (8.11.2, owner 2026-11-16; built 2026-11-16). [changed while building (8.14.14, stage B): or `organisation` (key: the organisation's id); `kind` up to 16 characters.] [built 2026-11-16 (8.25.5): or `member` (key: the public profile id), only while that member's profile is public; ended when it is switched off.] [built 2026-11-17 (8.11.8): `last_seen_at`, a bill's follow only: when the person last opened the bill's page, from the follow's own time.] |
| `notice_setting` | Per account: which follow emails are on (`planned`, `voted`); no row means both on. [changed while building (8.14.14, stage B): and `statements` (a followed organisation's statement became public); no row means all three on.] [changed while building (8.20.1): and `responses`; no row means all four on.] [built 2026-11-16 (8.23.9): and `comments`, an argument's author told of new comments under it; no row means on.] [built 2026-11-16 (8.24): and `frequency` (`immediate`, `daily`, `weekly`), the member's email rhythm, with `frequency_since`, `previous_frequency` and `previous_since`; no row means `immediate`.] [built 2026-11-16 (8.25.5): and `members`, a followed member published something; no row means all on.] |
| `bill_event` | (`bill_number`, `kind`, `key`) primary key, first seen, sent time, outcome. `kind`: planned (key: the date) or voted (key: the vote id). What followers are told about, recorded once (8.11.2). No person in it. [changed while building: an `outcome` column (`waiting`, `sent`, `skipped`, `superseded`) tells an event never to be sent from one waiting for the next day] [changed while building (8.14.14, stage B): `kind` `statement` (key: the statement's id) records, under its bill, that a followed organisation's statement became public; no person and no text in it] [built 2026-11-16 (8.24): `daily_at`, `weekly_at`, `early_at`, the marks of the gathered emails beside `outcome` and `sent_at` (the runs' emails); `kind` `response` per MP's answer (key: the person number and when the stance became public)] [built 2026-11-16 (8.25.5): `kind` `member` (key: the text's id) records, under its bill, that a followed member's argument or own-voice comment became public; no person and no text in it] [built 2026-11-16 (8.23.9, 8.23.14): `kind` `comment` (key: the argument's id and the run's moment) records that a run found comments under an argument; no person] [built 2026-11-16 (8.24): an index on (`kind`, `first_seen`); rows older than 30 days are deleted by the daily housekeeping, a planned date only once past (the designer's proposal, 12)] [built 2026-11-16 (8.24): `notify.rhythm_pass` (`pass`, `period`, `made_at`) says the daily email of a day or the weekly of a week was made; no person] |
| `member_mail_token` | [built 2026-11-17 (8.24.1)] Per account: the stop link's SHA-256 (unique), its generation and when it was made; the token itself is derived under the mail key and never stored. Deleted with the account; not exported; withheld from `lt2_readonly`. |

## 5.5 `audit`

`event`: time, actor, role, action, target, details, hashed IP. The application role may only insert.

## 5.6 Settings

`config.setting`: key, `jsonb` value, who changed it and when. Holds every number an administrator may need to tune
without a deploy: the summary rules in 8.4 (the threshold of all bills, `Digest:ThresholdDefault`, read whenever it is used, and the minimum no threshold goes below, 8.12.2; the weekly and before-vote times, the ceiling
per MP, the share of change that counts as "changed notably", `Digest:NotableChange`, and the smallest party group whose
number a summary shows, `Digest:PartyGroupMinimum`, 10, 0 = no floor, **owner 2026-11-17**), the reasons and "Man svarbu"
marks a person may give per bill in 8.2 (`Appeals:ReasonsPerPerson`, `Appeals:EndorsementsPerPerson`, 0 = no limit,
the default), the longest text members write for others to read (`Texts:MaxLength`, characters, 2 000; 0 = no limit but
the technical ceiling of 20 000; **owner, 2026-11-16**): arguments (8.2), organisations' statements (8.14.3) and MPs'
responses (8.5.5), and comments and "Apie mane" when they are built; not a bill's description (a moderator writes it and
it is reviewed anyway), a moderator's statement, a report, feedback, a display name, an organisation's name and
description, or a statement's link and goal, which keep their own limits; on the settings screen in its own group
"Narių tekstai"; the surge thresholds in 6.6,
the code limits and the life of a pending account in 6.2, whether registration needs an invitation, uses per personal invitation code (`Registration:PersonalCodeUses`, 0 = no limit, the default), uses per organisation's invitation code (`Registration:OrganisationCodeUses`, 0 = no limit, the default; 8.14.14 stage B, on the settings screen under registration), how long feedback is kept (`Feedback:KeepMonths`, 12), how many organisations an account may
register (`Organisations:PerAccount`, 3; closed ones do not count, 8.14.14; on the settings screen under
"Organizacijos"), the Claude model of the description draft and of the AI summaries (`Ai:DraftModel`, `claude-sonnet-5-5`, 8.11.4, 8.13), how many AI summaries the job may ask for a day (`Ai:SummariesPerDay`, 30; 0 = off, 8.13), [built 2026-11-16, 8.23.4 and 8.23.5] whether comments are on for arguments, statements and responses (`Comments:OnReasons`, `Comments:OnStatements`, `Comments:OnResponses`) and how many comments a person may write under one text (`Comments:PerPersonPerTarget`, 0 = no limit), on the settings screen under "Komentarai". Each
change is written to `audit`, and the current values of the rules that affect what MPs receive are shown on the public
methodology page.

The site's own address is not one of them **[decided by the owner on 2026-11-14]**: `Site:Address` is deployment
configuration, which compose builds for the web and the worker from `LT2_SITE_ADDRESS` and the https port (the address
Caddy serves), so no administrator can point emails at another site. It must be empty or an https address of a host
and nothing more; a host refuses to start otherwise. Links in every email and in the summary's text start with it;
when it is empty they stay the site's paths.

The administrator's settings screen groups them under headings (registration and sign-in; appeals; summaries; surge
note; feedback), with a separate "Saugumo ribos" group for the ones rarely changed (codes per hour and per day, days a
pending account is kept), which says so. Each setting shows a plain Lithuanian label, one sentence on what it does and
when to change it, its unit, the default, and the key in small type. The minimum threshold's 50 is a placeholder until
the owner sets a number, and the screen says so.

## 5.7 `usage`

The site's own usage statistics (8.27). `sketch`: per period (`day`, `month`, `year`, by its first Vilnius day) and
audience (`visitor`, `member`), the random salt and the HyperLogLog registers of a period under way; deleted when the
period ends; withheld from `lt2_readonly`. While it lasts, a candidate visitor or account can be tested against it
(8.27.2). `total`: per period, distinct visitors and members (approximate) and page views of visitors, members and
robots. `page`: views per Vilnius day and route. `referrer`: visitors per Vilnius day and referring host. These three
name no person. Only `lt2_app` reads and writes the schema.

Database roles: `lt2_migrator` owns the schemas and runs migrations; `lt2_app` has data access only; `lt2_worker` the same
plus the Hangfire schema [built 2026-11-18: whose tables `lt2_app` also reads and writes, never creates, for the
administrators' dashboard (3, 6.3); the migrator grants that with the superuser's connection, since the worker owns them]; `lt2_readonly` for ad-hoc analysis has no access to `identity` or `appeals.position`. [changed
while building (8.14.14): nor to the five organisation tables, which hold an account beside an organisation.]

