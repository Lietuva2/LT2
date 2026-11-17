"""Split the single design document into one file per section (2026-11-17).

The design used to be one file, design/system-design.md. It is now one file per section under design/, with
design/README.md as the index. A branch made before the split that still edits system-design.md can be brought
over by running this script on that branch's copy of the old file:

    python tools/split_design.py path/to/system-design.md [target-directory]

It writes the section files (and README.md) into the target directory (default: design/), overwriting them.
Nothing is reworded: headings are raised so that each file starts with a level-1 heading, and section 12 is
divided into what was decided and what is still open.
"""
import pathlib
import re
import sys

# (file name, first heading of the file, short description for the index)
FILES = [
    ('01-scope.md', '## 1. ', 'What changes from the concept, the problem stage 2 solves, what is kept and trimmed'),
    ('02-architecture.md', '## 2. ', 'Modular monolith, modules and how they talk, the outbox'),
    ('03-technology.md', '## 3. ', 'Technology choices; why Blazor and which render modes'),
    ('04-solution-layout.md', '## 4. ', 'Projects and folders as intended'),
    ('05-data-model.md', '## 5. ', 'Schemas: parliament, identity, appeals, notify, audit, settings, usage'),
    ('06-security.md', '## 6. ', 'What we protect, authorization, web hardening, data protection, integrity, supply chain, the rules and decisions of the 2026-11-15 review'),
    ('06.2-sign-in.md', '### 6.2 ', 'Sign-in and accounts: codes, passkeys, staff, registration, invitations, sessions'),
    ('06.3-security-rest.md', '### 6.3 ', None),  # merged back into 06-security.md
    ('07-mp-profiles.md', '## 7. ', 'Stage 1: Seimas open-data sources, import, what a profile shows, its API'),
    ('08.01-life-of-an-appeal.md', '### 8.1 ', 'Life of an appeal'),
    ('08.02-rules.md', '### 8.2 ', 'Rules: positions, arguments, "Man svarbu", text length and layout, limits'),
    ('08.03-moderation.md', '### 8.3 ', 'Moderation: queue, hiding, restrictions, statements'),
    ('08.04-summaries.md', '### 8.4 ', 'Summary emails to MPs: content, timing, threshold, eligibility'),
    ('08.05-mps-on-the-platform.md', '### 8.5 ', 'MPs on the platform: official address, take-over, office, responses'),
    ('08.06-what-participants-hear.md', '### 8.6 ', 'What participants hear'),
    ('08.07-public-record.md', '### 8.7 ', 'Public copies of summaries; the front page'),
    ('08.08-api.md', '### 8.8 ', 'Stage 2 API'),
    ('08.10-feedback.md', '### 8.10 ', 'Feedback from members'),
    ('08.11-reasons-to-come.md', '### 8.11 ', 'The agenda, front page, following, AI draft, share line, what is new since the last visit'),
    ('08.12-one-object.md', '### 8.12 ', 'Every bill is open: the bill\'s record, the bill\'s page, threshold, after the final vote'),
    ('08.13-description.md', '### 8.13 ', 'The description ("Apie ką projektas") and the AI summary job'),
    ('08.14-organisations.md', '### 8.14 ', 'Organisations: registration, confirmation, representatives, statements'),
    ('08.09-done-when.md', '### 8.9 ', 'Stage 2: done when'),
    ('08.15-explanatory-text.md', '### 8.15 ', 'Explanatory text on screens; hints and disclosures'),
    ('08.16-surfaces.md', '### 8.16 ', 'Surfaces (sheet and ground), the action row, joined buttons'),
    ('08.17-ballots.md', '### 8.17 ', 'The list of MPs\' ballots on a vote'),
    ('08.18-vote-history.md', '### 8.18 ', 'The history of the Seimas\'s votes'),
    ('08.19-sending.md', '### 8.19 ', 'Sending the summaries: reviewed a day ahead, the email and office copies, MP\'s link, introduction letter, suppression'),
    ('08.20-answers-and-known-browsers.md', '### 8.20 ', 'The followers\' email about MPs\' answers; known browsers'),
    ('08.21-navigation.md', '### 8.21 ', 'The menu, the footer, the account item and account screens'),
    ('08.22-terms-and-storage.md', '### 8.22 ', 'Terms of use, the privacy notice, what the browser stores'),
    ('08.23-comments.md', '### 8.23 ', 'Comments ("Aptarimas") and "Sutinku"'),
    ('08.24-email-rhythm.md', '### 8.24 ', 'How often a member\'s emails come; the stop link; the gathered email'),
    ('08.25-public-profiles.md', '### 8.25 ', 'Public profiles, "Apie mane", following a member, the party on the profile'),
    ('08.26-analytics.md', '### 8.26 ', 'Analytics ("Statistika")'),
    ('08.27-usage.md', '### 8.27 ', 'Usage statistics: the team\'s own counting'),
    ('09-hosting.md', '## 9. ', 'Hosting and operations: layout, environments, delivery, monitoring, connections'),
    ('10-quality.md', '## 10. ', 'Quality'),
    ('11-milestones.md', '## 11. ', 'Milestones'),
    ('12-decisions.md', '## 12. ', 'The log of what the owner decided, by date (section 12)'),
    ('appendix-a-summary-email.md', '## Appendix A.', 'Sample summary email'),
    ('appendix-b-terms.md', '## Appendix B.', 'Terms: English and Lithuanian'),
]

OPEN_FILE = '12-open.md'
OPEN_DESCRIPTION = 'What is still open: proposals awaiting the owner, decisions needed, risks (section 12)'
# Bold block titles in section 12 that belong to what is still open (besides any title "awaiting the owner").
OPEN_BLOCKS = ('**Decisions needed', '**Risks**', '**If this order is agreed')


def is_open(title):
    return title.startswith(OPEN_BLOCKS) or 'awaiting the owner' in title.split('**')[1]


def outside_fences(lines):
    fence = False
    for i, line in enumerate(lines):
        if line.startswith('```'):
            fence = not fence
            continue
        if not fence:
            yield i, line


def raise_headings(lines, by):
    out, fence = [], False
    for line in lines:
        if line.startswith('```'):
            fence = not fence
        elif not fence and re.match(r'^#{2,6} ', line):
            line = line[by:] if line.startswith('#' * (by + 1)) else line
        out.append(line)
    return out


def split_section_12(lines):
    """Divide section 12 at its bold block titles into decided and open."""
    head, blocks, current = [], [], None
    for line in lines:
        if re.match(r'^\*\*[A-Z0-9]', line):
            current = [line]
            blocks.append(current)
        elif current is None:
            head.append(line)
        else:
            current.append(line)
    decided, still_open = [], []
    for block in blocks:
        (still_open if is_open(block[0]) else decided).append(block)
    return head, decided, still_open


def main():
    source = pathlib.Path(sys.argv[1])
    target = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else pathlib.Path('design')
    lines = source.read_text(encoding='utf-8').splitlines(keepends=True)

    starts = {}
    for i, line in outside_fences(lines):
        for name, heading, _ in FILES:
            if line.startswith(heading) and name not in starts:
                starts[name] = i
    missing = [name for name, _, _ in FILES if name not in starts]
    if missing:
        sys.exit(f'headings not found: {missing}')

    intro = lines[:starts['01-scope.md']]
    order = sorted(starts.items(), key=lambda kv: kv[1])
    chunks = {}
    for (name, a), (_, b) in zip(order, order[1:] + [('', len(lines))]):
        chunks[name] = lines[a:b]

    # "## 8. Stage 2: appeals" has no text of its own; it opens the 8.x files' index group.
    chunks['07-mp-profiles.md'] = [l for l in chunks['07-mp-profiles.md'] if not l.startswith('## 8. ')]
    # 6.3 to 6.9 stay with 6 and 6.1.
    chunks['06-security.md'] += chunks.pop('06.3-security-rest.md')

    written = {}
    for name, chunk in chunks.items():
        by = 2 if chunk[0].startswith('### ') else 1
        text = raise_headings(chunk, by)
        if name == '12-decisions.md':
            head, decided, still_open = split_section_12(text)
            written[name] = ['# 12. Decisions made\n', '\n',
                             'What the owner decided or accepted, by date. What is still open is in [12-open.md](12-open.md).\n',
                             '\n'] + [l for block in decided for l in block]
            written[OPEN_FILE] = ['# 12. Still open: proposals, decisions needed, risks\n', '\n',
                                  'What the owner decided is in [12-decisions.md](12-decisions.md). Do not decide an open item in code.\n',
                                  '\n'] + [l for block in still_open for l in block]
            continue
        written[name] = text

    target.mkdir(parents=True, exist_ok=True)
    for name, text in written.items():
        while text and text[-1].strip() == '':
            text.pop()
        (target / name).write_text(''.join(text) + '\n', encoding='utf-8', newline='\n')

    index = ['# LT2 system design\n', '\n']
    index += intro[2:] if intro[0].startswith('# ') else intro
    while index and index[-1].strip() == '':
        index.pop()
    index += ['\n', '\n', '## Where each part is\n', '\n',
              'One file per section; a section number such as "8.12" or "6.2" in the text or in code comments is the file\n',
              'that starts with it. Read the files a change touches, not the whole design.\n', '\n',
              '| File | Section |\n', '|---|---|\n']
    described = [(n, d) for n, _, d in FILES if d] + [(OPEN_FILE, OPEN_DESCRIPTION)]
    for name, description in sorted(described):
        index.append(f'| [{name}]({name}) | {description} |\n')
    index += ['\n', 'Also here: [ui/](ui/README.md), the interface prototype (tokens, sample pages), and\n',
              '[text-audit-2026-11-15.md](text-audit-2026-11-15.md), the screen-by-screen record of explanatory text (8.15).\n']
    (target / 'README.md').write_text(''.join(index), encoding='utf-8', newline='\n')
    print(f'{len(written)} files and README.md written to {target}')


if __name__ == '__main__':
    main()
