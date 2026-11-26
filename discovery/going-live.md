# Before a public launch of stage 1

Stage 1 is built and runs as a closed trial at https://test.lietuva2.lt. These are the questions the owner has to
settle before it opens to the public. The full list of open items, with the design sections they touch, is
`design/12-open.md` on `master`; this note keeps only what blocks a launch. Nothing here has a date.

## Decisions for the owner

1. **Where the product lives.** Does the application take `lietuva2.lt`, and where does this information website go
   (a subdomain, a path, or folded into the product's own pages such as "Metodika")?
2. **Who may register.** Keep registration by invitation at launch (`Registration:RequireInvitation`), or open it?
   Email-only registration makes a campaign of fake accounts cheap; invitations are the main defence until the EU
   Digital Identity Wallet is available.
3. **When summaries start going to MPs.** Who writes and signs the introduction letter, which offices are talked to
   first, and the real threshold (default 100 participants; the minimum of 50 is a placeholder).
4. **Comments under MPs' responses.** Keep them on, as on the trial, or switch them off before real MPs are on the
   site?
5. **Moderators.** Who moderates, how many, who enters the Seimas agenda every working day, and whether AI-written
   descriptions ("Sukurta DI · žmonių neperžiūrėta") may stand unreviewed at launch.
6. **The trial's data.** Launch on a fresh database (the trial holds invented accounts and activity)?

## Legal and language

7. **The controller's details** in the terms and the privacy notice (the "[bus įrašyta]" facts: VšĮ LT2.0's details,
   contacts, the email, server and backup providers, the inspectorate's contacts).
8. **One reading by a lawyer** of the terms, the privacy notice and the consent texts: positions and "Man svarbu" as
   special-category data (is a permit or a DPIA filing needed?), the legal basis of the usage statistics, follower
   numbers, public profiles and "Sutinku", whether summaries are citizens' appeals and not marketing, and whether an
   organisation's statement passed to MPs is lobbying.
9. **One reading by a native speaker** of every text on the site and in the emails.

## Running it

10. **Production apart from the trial:** its own server, encrypted backups with a rehearsed restore, an uptime check and
    alerts, a telemetry backend (hosted EU free tier or self-hosted), CDN and DDoS protection (Bunny or Cloudflare).
11. **Email:** a sending subdomain with SPF, DKIM and DMARC, and a transactional provider for sign-in codes before any
    public campaign (plain SMTP mailboxes have daily caps and report no complaints).
12. **The security contact** for `/.well-known/security.txt`, and secret scanning switched on in the repository.
13. **The live checks of the Seimas import** (the live contract suite, 20 profiles against lrs.lt, 30 votes whose
    ballots sum to their totals) and a load test at the size of a widely shared campaign.
