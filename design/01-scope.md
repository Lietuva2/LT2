# 1. Scope and build order

## 1.1 What changes compared with the public concept

The public pages describe a first version (voting with delegation) and a second version (posts, topics, answers).
This design builds in a different order **[decided]**:

| Stage | What | Source in the concept |
|---|---|---|
| **0. Foundation** | Solution skeleton, sign-in, security plumbing, deployment, monitoring. | – |
| **1. MP profiles** | Every MP of the current term imported from Seimas open data: mandate, faction, committees, contacts, voting record, statistics. | Chapter 06, the "unjoined MP" profile |
| **2. Appeals** | People take a side on a Seimas bill and give reasons; MPs receive summary emails instead of hundreds of separate ones; MPs can answer once for everyone. | A trimmed second version, section 1.2 |
| **3. Voting and delegation** | Bill pages with AI summaries, delegation, LT2 result, analytics, election mode. | The documented first version |

Stage 3 is not designed here. Stage 2 is shaped so that stage 3 extends it: a person's side on a bill is stored so that it
can become their direct LT2 vote, and an MP's published position can become the MP's LT2 vote with its explanation.

**Following is the predecessor of delegation [owner, 2026-11-16: "if we're planning on voice delegation later on, that
will probably break this rule anyway. So let's have the following to be like a predecessor of delegating."].** A member
already chooses whom to hear from by following an MP, an organisation or a member with a public profile, and each shows
how many follow it (8.11.2). That number shows whom people choose to hear from, which is the ground delegation would later
stand on; how a choice to hear becomes a choice to be represented is stage 3's question. **A follow never becomes a
delegation by itself [the designer's proposal, awaiting the owner; no alternative recommended]:** when delegation is
designed it is its own explicit act, and follows that exist then are not converted, because people follow those they
disagree with in order to watch them, and nobody who followed was asked to be represented. It is a constraint on stage
3's design, not a feature of stage 2.

The public pages still say "first version = voting". They are **not** changed by this document; section 12 lists what
would need rewording once this order is agreed.

## 1.2 The problem stage 2 solves

Before a contested vote, people send MPs large numbers of near-identical emails. For the senders it is slow and they never
learn whether anyone read them; MPs and their offices cannot tell how many distinct people stand behind the flood, and the
other side of the argument is invisible.

Stage 2 replaces the flood with one object per bill, an **appeal** (*kreipimasis*):

- it is always tied to one Seimas bill **[decided]**;
- it has two sides: "asks to vote for" and "asks to vote against";
- a person joins one side (their **position**) and may add or endorse **reasons**;
- every sitting MP receives a **summary email** with the numbers for both sides and the most endorsed reasons
  **[decided: all MPs, automatically; nobody picks recipients]**;
- an MP (or the MP's office) can publish one **response** that everyone who took a side is told about.

One appeal per bill, not one per author: every active bill is open on its own page, and nobody proposes, opens or approves
an appeal first; everyone who takes part on a bill joins the same one **[decided by the owner on 2026-11-14, 8.12; it
replaced several proposals per bill, decided 2026-11-13]**. This is the same rule as "one vote per bill" in chapter 04 of
the concept, and it is what makes one summary per bill possible.

## 1.3 What is kept from the second version and what is trimmed

| Second-version feature | In stage 2 | Why |
|---|---|---|
| Free-form posts and questions | **Trimmed** to a short *reason* attached to a bill and a side | Free text with no bill needs AI grouping into topics and heavy moderation. |
| Tagging an MP, delegate or bill (`@`, `#`) | **Dropped** | Every appeal belongs to its bill's page; the addressee is every MP. |
| "Man svarbu" | **Adapted**: joining a side is the main action; "Man svarbu" marks the reasons that matter to you | Keeps the term for what it means. |
| AI grouping into topics and questions | **Trimmed** to "this reason looks like an existing one" within one bill and side | The bill already is the topic. |
| Topic page | **Becomes** the bill's page (8.12.1) | Same layout idea: counts, grouped reasons, answers. |
| Answer once for everyone | **Kept**: one response per MP per bill | The core of the value for MPs. |
| Work statuses (Atsakė, Rengia projektą, Įregistruotas, Nepritariu) | **Trimmed** to a stance: will vote for / will vote against / undecided, plus text | The bill already exists, so "preparing a bill" has no meaning here. |
| "Mano temos" | **Replaced** by the summary email and a plain list "appeals addressed to me" | The email is the inbox. |
| Public response rate (shown at ≥ 70 %) | **Later** | Data is collected from day one; showing it is a tone decision to take with MPs. |
| Personal feed | **Dropped** | Email notifications and "my appeals" are enough at this size. |
| Comments and discussion threads | ~~**Dropped**~~ **Comments kept, threads dropped** [changed by the owner on 2026-11-16, 8.23] | Dropped on 2026-11-12 for the moderation cost, with reasons carrying the content. The owner reversed it on 2026-11-16: a text alone on a bill's page has no place for a reply. Comments ("Aptarimas") are one level of remarks under an argument, a statement or a response, shown at once, in the moderators' list only when reported, each with "Sutinku", each kind with a switch that stops new ones; replies to replies stay dropped. Never counted and never sent to MPs. |
| Petitions from topics, MPs' draft bills | **Dropped** | Not needed for the problem. |
| Moderation (AI helper plus moderators) | **Kept, required** | Anything that reaches 141 inboxes must pass a human. |

