# LT2 system design

What Lietuva 2.0 is and does, one file per section. English, for the team; nothing here is published. All names, bills
and numbers in examples are the fictional ones from `AGENTS.md`.

- A section says what the product does today, as the code has it. What is designed and not built yet stands under a
  line that starts **Not built.** and is listed in [12-open.md](12-open.md).
- The owner's decisions that hold are in [12-decisions.md](12-decisions.md), by section, each with its date.
- What is still open is in [12-open.md](12-open.md): what is designed and not built, where the code does not do what a
  rule says, and what waits for the owner, a native speaker or the lawyer.
- History is in git, not here: a section carries no dates and no account of what changed. When a change makes a
  section untrue, the section is rewritten to what is true.

## Where each part is

One file per section; a section number such as "8.12" or "6.2" in the text or in code comments is the file
that starts with it. Read the files a change touches, not the whole design.

| File | Section |
|---|---|
| [01-scope.md](01-scope.md) | What the product is, the problem it solves, what it does and deliberately does not do |
| [02-architecture.md](02-architecture.md) | Modular monolith, modules and how they talk, the outbox |
| [03-technology.md](03-technology.md) | Technology choices; why Blazor and which render modes |
| [04-solution-layout.md](04-solution-layout.md) | The solution's projects and folders |
| [05-data-model.md](05-data-model.md) | Schemas: parliament, identity, appeals, notify, audit, settings, usage |
| [06-security.md](06-security.md) | What we protect, authorization, web hardening, data protection, integrity of the numbers, supply chain and delivery, rules held throughout (6.8), rules for closed bills, staff accounts and settings (6.9) |
| [06.2-sign-in.md](06.2-sign-in.md) | Sign-in and accounts: codes, passkeys, staff, registration, invitations, sessions |
| [07-mp-profiles.md](07-mp-profiles.md) | MP profiles: Seimas open-data sources, import, what a profile shows, its API |
| [08.01-life-of-an-appeal.md](08.01-life-of-an-appeal.md) | Life of an appeal |
| [08.02-rules.md](08.02-rules.md) | Rules: positions, arguments, "Man svarbu", text length and layout, limits |
| [08.03-moderation.md](08.03-moderation.md) | Moderation: queue, actions, hiding and restoring, writing restrictions, reports |
| [08.04-summaries.md](08.04-summaries.md) | Summary emails to MPs: who, when, threshold, planned dates, content, deliverability |
| [08.05-mps-on-the-platform.md](08.05-mps-on-the-platform.md) | MPs on the platform: official address, take-over, office, responses |
| [08.06-what-participants-hear.md](08.06-what-participants-hear.md) | What participants hear |
| [08.07-public-record.md](08.07-public-record.md) | Public copies of summaries; the front page |
| [08.08-api.md](08.08-api.md) | The API's routes and who may call each |
| [08.10-feedback.md](08.10-feedback.md) | Feedback from members |
| [08.11-reasons-to-come.md](08.11-reasons-to-come.md) | The agenda, following, the AI draft, the share line, what is new since the last visit |
| [08.12-one-object.md](08.12-one-object.md) | Every bill is open: what a bill offers, threshold, moderation after the fact, lists |
| [08.13-description.md](08.13-description.md) | The description ("Apie ką projektas"), the AI summary job, the readable title and members asking for a description |
| [08.14-organisations.md](08.14-organisations.md) | Organisations: what one is, statements, goals, bringing members, keeping it honest, how it works |
| [08.15-explanatory-text.md](08.15-explanatory-text.md) | Explanatory text on screens; hints and disclosures |
| [08.16-surfaces.md](08.16-surfaces.md) | Surfaces (sheet and ground), the action row, version histories, one green for the sides and the bubbles, one "show more" under every long list, a list's heading that stays in view |
| [08.17-ballots.md](08.17-ballots.md) | The list of MPs' ballots on a vote |
| [08.18-vote-history.md](08.18-vote-history.md) | The history of the Seimas's votes |
| [08.19-sending.md](08.19-sending.md) | Sending the summaries: three states, reviewed a day ahead, the email and office copies, MP's link, introduction letter, suppression |
| [08.20-answers-and-known-browsers.md](08.20-answers-and-known-browsers.md) | The followers' email about MPs' answers; known browsers |
| [08.21-navigation.md](08.21-navigation.md) | The menu, the footer, the account item and account screens |
| [08.22-terms-and-storage.md](08.22-terms-and-storage.md) | Terms of use, the privacy notice, what the browser stores, link previews (Open Graph) |
| [08.23-comments.md](08.23-comments.md) | Comments ("Aptarimas") and "Sutinku" |
| [08.24-email-rhythm.md](08.24-email-rhythm.md) | How often a member's emails come; the stop link; the gathered email |
| [08.25-public-profiles.md](08.25-public-profiles.md) | Public profiles, "Apie mane", following a member, the party on the profile |
| [08.26-analytics.md](08.26-analytics.md) | Analytics ("Statistika"): where the Seimas and the members agree, faction unity |
| [08.27-usage.md](08.27-usage.md) | Usage statistics: the team's own counting |
| [08.28-getting-started.md](08.28-getting-started.md) | Getting started: the menu's lines, the new member's start on the front page, "Kur ką rasti", the profile's one form |
| [09-hosting.md](09-hosting.md) | Hosting and operations: layout, environments, delivery, watching it, database connections |
| [10-quality.md](10-quality.md) | Quality |
| [12-decisions.md](12-decisions.md) | The owner's decisions that hold, by section |
| [12-open.md](12-open.md) | Still open: designed, not built; where the code does not do what a rule says; awaiting the owner, a native speaker, the lawyer; decisions needed; risks |
| [appendix-a-summary-email.md](appendix-a-summary-email.md) | Sample summary email |
| [appendix-b-terms.md](appendix-b-terms.md) | Terms: English and Lithuanian |

Also here: [ui/](ui/README.md), the interface prototype (tokens, sample pages).
