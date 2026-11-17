# LT2 system design

Detailed design for building Lietuva 2.0 as a production system. Written 2026-11-12 from the concept pages and
`discovery/design-notes.md` on the `docs` branch and the owner's answers the same day. English, for the team; nothing
here is published.

This is the maintained copy. Paths such as `../docs/` and `design-notes.md`, and the rules and fictional example data
this document cites from the documents-era `AGENTS.md`, are on the `docs` branch, where the concept website and the
discovery notes live.

**Status: agreed with the owner on 2026-11-12** as the basis for implementation; decisions made that day are marked
**[decided]**. Section 12 lists what is still to be chosen before the stage that needs it. All names, bills and numbers in
examples are the fictional ones from `AGENTS.md`.


## Where each part is

One file per section; a section number such as "8.12" or "6.2" in the text or in code comments is the file
that starts with it. Read the files a change touches, not the whole design.

| File | Section |
|---|---|
| [01-scope.md](01-scope.md) | What changes from the concept, the problem stage 2 solves, what is kept and trimmed |
| [02-architecture.md](02-architecture.md) | Modular monolith, modules and how they talk, the outbox |
| [03-technology.md](03-technology.md) | Technology choices; why Blazor and which render modes |
| [04-solution-layout.md](04-solution-layout.md) | Projects and folders as intended |
| [05-data-model.md](05-data-model.md) | Schemas: parliament, identity, appeals, notify, audit, settings, usage |
| [06-security.md](06-security.md) | What we protect, authorization, web hardening, data protection, integrity, supply chain, the rules and decisions of the 2026-11-15 review |
| [06.2-sign-in.md](06.2-sign-in.md) | Sign-in and accounts: codes, passkeys, staff, registration, invitations, sessions |
| [07-mp-profiles.md](07-mp-profiles.md) | Stage 1: Seimas open-data sources, import, what a profile shows, its API |
| [08.01-life-of-an-appeal.md](08.01-life-of-an-appeal.md) | Life of an appeal |
| [08.02-rules.md](08.02-rules.md) | Rules: positions, arguments, "Man svarbu", text length and layout, limits |
| [08.03-moderation.md](08.03-moderation.md) | Moderation: queue, hiding, restrictions, statements |
| [08.04-summaries.md](08.04-summaries.md) | Summary emails to MPs: content, timing, threshold, eligibility |
| [08.05-mps-on-the-platform.md](08.05-mps-on-the-platform.md) | MPs on the platform: official address, take-over, office, responses |
| [08.06-what-participants-hear.md](08.06-what-participants-hear.md) | What participants hear |
| [08.07-public-record.md](08.07-public-record.md) | Public copies of summaries; the front page |
| [08.08-api.md](08.08-api.md) | Stage 2 API |
| [08.09-done-when.md](08.09-done-when.md) | Stage 2: done when |
| [08.10-feedback.md](08.10-feedback.md) | Feedback from members |
| [08.11-reasons-to-come.md](08.11-reasons-to-come.md) | The agenda, front page, following, AI draft, share line, what is new since the last visit |
| [08.12-one-object.md](08.12-one-object.md) | Every bill is open: the bill's record, the bill's page, threshold, after the final vote |
| [08.13-description.md](08.13-description.md) | The description ("Apie ką projektas") and the AI summary job |
| [08.14-organisations.md](08.14-organisations.md) | Organisations: registration, confirmation, representatives, statements |
| [08.15-explanatory-text.md](08.15-explanatory-text.md) | Explanatory text on screens; hints and disclosures |
| [08.16-surfaces.md](08.16-surfaces.md) | Surfaces (sheet and ground), the action row, joined buttons |
| [08.17-ballots.md](08.17-ballots.md) | The list of MPs' ballots on a vote |
| [08.18-vote-history.md](08.18-vote-history.md) | The history of the Seimas's votes |
| [08.19-sending.md](08.19-sending.md) | Sending the summaries: reviewed a day ahead, the email and office copies, MP's link, introduction letter, suppression |
| [08.20-answers-and-known-browsers.md](08.20-answers-and-known-browsers.md) | The followers' email about MPs' answers; known browsers |
| [08.21-navigation.md](08.21-navigation.md) | The menu, the footer, the account item and account screens |
| [08.22-terms-and-storage.md](08.22-terms-and-storage.md) | Terms of use, the privacy notice, what the browser stores, link previews (Open Graph) |
| [08.23-comments.md](08.23-comments.md) | Comments ("Aptarimas") and "Sutinku" |
| [08.24-email-rhythm.md](08.24-email-rhythm.md) | How often a member's emails come; the stop link; the gathered email |
| [08.25-public-profiles.md](08.25-public-profiles.md) | Public profiles, "Apie mane", following a member, the party on the profile |
| [08.26-analytics.md](08.26-analytics.md) | Analytics ("Statistika") |
| [08.27-usage.md](08.27-usage.md) | Usage statistics: the team's own counting |
| [09-hosting.md](09-hosting.md) | Hosting and operations: layout, environments, delivery, monitoring, connections |
| [10-quality.md](10-quality.md) | Quality |
| [11-milestones.md](11-milestones.md) | Milestones |
| [12-decisions.md](12-decisions.md) | The log of what the owner decided, by date (section 12) |
| [12-open.md](12-open.md) | What is still open: proposals awaiting the owner, decisions needed, risks (section 12) |
| [appendix-a-summary-email.md](appendix-a-summary-email.md) | Sample summary email |
| [appendix-b-terms.md](appendix-b-terms.md) | Terms: English and Lithuanian |

Also here: [ui/](ui/README.md), the interface prototype (tokens, sample pages), and
[text-audit-2026-11-15.md](text-audit-2026-11-15.md), the screen-by-screen record of explanatory text (8.15).
