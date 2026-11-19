# 1. Scope

## 1.1 What the product is

Lietuva 2.0 is a platform for people who want to tell the Seimas what they think of the bills before it, and for the MPs
and their offices who want to know. It has:

- **MP profiles** for every MP of the current term, imported from Seimas open data: mandate, faction, committees,
  contacts, voting record and statistics.
- **Appeals** on Seimas bills: people take a side on a bill and give reasons; MPs receive summary emails instead of
  hundreds of separate ones; an MP can answer once for everyone.

Voting with delegation is neither designed nor built. Following an MP, an organisation or a member with a public profile
(8.11.2) is a request for emails, with a public count of followers; it is not a delegation.

## 1.2 The problem it solves

Before a contested vote, people send MPs large numbers of near-identical emails. For the senders it is slow and they never
learn whether anyone read them; MPs and their offices cannot tell how many distinct people stand behind the flood, and the
other side of the argument is invisible.

LT2 replaces the flood with one object per bill, an **appeal** (*kreipimasis*):

- it is always tied to one Seimas bill;
- it has two sides: "asks to vote for" and "asks to vote against";
- a person joins one side (their **position**) and may add or endorse **reasons**;
- every sitting MP receives a **summary email** with the numbers for both sides and the most endorsed reasons; nobody
  picks recipients (summaries go to MPs only while `Digest:Sending` allows it, 8.19.1);
- an MP (or the MP's office) can publish one **response** that everyone who took a side is told about.

One appeal per bill, not one per author: every active bill is open on its own page, and nobody proposes, opens or approves
an appeal first; everyone who takes part on a bill joins the same one (8.12). That is what makes one summary per bill
possible.

## 1.3 What it does and deliberately does not do

- **No free-form posts or questions.** A member writes a short *reason* attached to a bill and a side.
- **No tagging** of an MP, a delegate or a bill (`@`, `#`): every appeal belongs to its bill's page, and the addressee is
  every MP.
- **"Man svarbu"** marks the reasons that matter to you; joining a side is the main action.
- **No AI grouping into topics.** Within one bill and side, a plain text comparison says "this argument looks like an
  existing one"; the bill already is the topic.
- **The bill's page** (8.12.1) shows the counts, the reasons and the answers.
- **Answer once for everyone:** one response per MP per bill. This is the core of the value for MPs.
- **An MP's stance** is will vote for, will vote against or undecided, plus a text; there are no work statuses, since the
  bill already exists.
- **An MP's inbox** is the summary email and the list "Projektai, dėl kurių į jus kreipiamasi" (8.5.7).
- **No public response rate** of MPs is shown.
- **No personal feed:** email notices and the member's own pages are enough.
- **Comments, not threads** (8.23): "Aptarimas" holds one level of remarks under an argument, a statement or a response,
  shown at once, in the moderators' list only when reported, each with "Sutinku", each kind with a switch that stops new
  ones. There are no replies to replies. Comments are never counted and never sent to MPs.
- **No petitions and no MPs' draft bills.**
- **Moderation is required:** rule-based pre-screening holds a flagged text for a moderator, and every summary is
  reviewed by moderators before it goes to MPs (8.4), since anything that reaches 141 inboxes must pass a human.
