# LT2 interface texts: what stays inline, what goes behind a link

Audit of the explanatory texts a person sees without asking for them, worktree `m2a-appeals-b33ed1` as of 2026-11-15
(read-only; nothing in the repository was changed, nothing was built or run). Source of truth: the Razor components and
the `.resx` files; the rendered site was not opened.

**Every Lithuanian text marked [juodraštis] is a draft for a native speaker's read** (AGENTS.md: new copy is read by a
native speaker before it is shown externally).

Resource files, abbreviated in the tables:

| Abbr. | File |
|---|---|
| AU | `src/Lt2.Web.Client/Appeals/AppealUi.resx` |
| UT | `src/Lt2.Web.Client/UiText.resx` |
| AT | `src/Lt2.Web/Components/Appeals/AppealText.resx` |
| PT | `src/Lt2.Web/Components/PageText.resx` |
| PA | `src/Lt2.Web/Components/Parliament/ParliamentText.resx` |
| FT | `src/Lt2.Web/Components/Feedback/FeedbackText.resx` |

**Summary.** 162 rows covering about 230 keys by name plus the staff families (`Mod.Sentence.*`, `Admin.Help.*`, …),
out of 1 174 keys in the six files: keep 102, shorten 39, to methodology 7, under a link 6 (three of them behind one
link on the registration form), delete 6, new link or summary text 2. Inline explanation per screen, before → after
(visible without asking, in the busiest usual state):

| Screen | Before | After |
|---|---|---|
| Bill page, member with a side on an open bill | 11 | 8 + „Kas yra riba?“ (≈ 950 → ≈ 550 characters) |
| Organisation profile, representative | 11 | 8 |
| Account `/paskyra` | 9 | 8 (≈ 1 200 → ≈ 750 characters) |
| Summaries `/suvestines` | 8 | 3 + one link |
| Organisation registration | 7 | 1 + „Kaip tai veikia“ (+ the website format hint) |
| Front page, visitor | 7 | 7 (one shortened); signed in 6 → 5 |
| Feedback panel, member | 5 | 4 (two shortened, GitHub behind a link) |
| Agenda | 4 | 4 (two shortened) |
| Organisation profile, visitor | 4 | 2 |
| Statement form on the bill page (representative) | 4 | 4, each shorter |

Proposal words: **keep** (must be known before acting), **shorten**, **under a link** (on the page, collapsed),
**to methodology** (removed from the screen, a short link to `/metodika` or `/moderavimo-taisykles`), **delete**.
"Where" is `component:line` and the element's class (`.lead`, `.small`, `.fine`, `.note`, `.field__hint`).

---

## 0. The collapsing pattern

**What exists.** Ten `<details>` disclosures, all `<details …><summary class="linkish …">…</summary>…</details>`:

| Where | Summary text |
|---|---|
| `BillPage.razor:198` | „Kaip balsavo kiekvienas Seimo narys“ (`linkish`, no `small`) |
| `OrganisationPage.razor:112` | „Ankstesnės versijos ({0})“ |
| `AppealReasons.razor:63` (`class="moderate"`) | „Moderuoti“ |
| `DateFlagForm.razor:9` (`class="stack stack--tight"`) | „Data neteisinga?“ |
| `VisitorReport.razor:6` (`class="report-visitor"`) | „Pranešti“ / „Pranešti apie klaidą“ |
| `OrganisationManage.razor:138` | „Keisti organizacijos duomenis“ (`linkish`, no `small`) |
| `StatementModerationPanel.razor:27` | „Kitos pozicijos ({0})“ |
| `HiddenItems.razor:56`, `Invitations.razor:40` | staff screens |
| `FeedbackBalloon.razor:7` | the balloon (own styling) |

Styling in `src/Lt2.Web/wwwroot/css/lt2.css`: only `.linkish` (442–448, a link look for buttons and summaries),
`.report-visitor > summary { display: inline; cursor: pointer; }` and `.report-visitor[open] > p { margin-top }`
(517–518), and the balloon's own rules (527–533). There is no general disclosure class.

**Does it fit?** Yes: it is native HTML, so it opens on the server-rendered public pages without JavaScript and inside
WebAssembly islands before they start (the islands rule in AGENTS.md disables controls, but a `<details>` is not a
control the island owns). What is missing for a consistent look:

1. The browser's triangle shows on every summary except `.report-visitor` (inline) and the balloon. Some have it, some
   not.
2. Spacing after opening depends on the parent: `DateFlagForm` adds `stack stack--tight` to the `<details>`, the others
   rely on whatever surrounds them.
3. Summary text size is mixed (`small` on some).
4. The summary of a block-level disclosure takes the full row width, so the click area extends far past the words.

**Recommendation: one class, `more`, used for every "Plačiau"-type disclosure.**

```html
<details class="more stack stack--tight" id="register-more">
    <summary class="linkish small">Kaip tai veikia</summary>
    …collapsed paragraphs or list…
</details>
```

```css
/* A text one click away (owner 2026-11): a link-looking summary, no triangle, the opened text spaced like a stack. */
.more > summary { align-self: flex-start; cursor: pointer; list-style: none; }
.more > summary::-webkit-details-marker { display: none; }
.more > summary::marker { content: ""; }
```

- Fold `.report-visitor`'s two rules into it (`VisitorReport` becomes `class="more report-visitor"`, keeping only its
  inline position rule at 591).
- The summary names what opens ("Kaip tai veikia", "Ką reiškia ši žyma?", "Kas yra riba?"), not a bare "Plačiau",
  because a page may hold several; "Plačiau" only where it is the only one on the screen.
- In islands do not bind `open` from C# (the balloon does it on a server-rendered page, which is fine): a re-render
  must not close a disclosure the person opened.
- Existing disclosures with an action inside (`Moderuoti`, `Data neteisinga?`, `Keisti organizacijos duomenis`) keep
  their wording; they only take the class.

---

## 1. Organisation registration `/organizacijos/nauja` (the model)

`src/Lt2.Web.Client/Pages/NewOrganisation.razor` + `Organisations/OrganisationForm.razor`; alternatively
`Appeals/WritingRestricted.razor` for a restricted writer; `Components/NeedsScript.razor` without JavaScript.

What a person sees now, top to bottom: crumbs, h1 „Registruoti organizaciją“, a lead, a four-point list in `.small`, a
`.fine` methodology link, then the form: name, description (+ counter), website (+ a three-sentence hint), legal code,
the lobbyist checkbox, „Registruoti“. About 720 characters of explanation before the first field.

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| R1 | `Org.Register.Lead` (AU) | `NewOrganisation.razor:21` `.lead` | Organizacija gali skelbti savo poziciją dėl Seimo projektų – savo vardu, atskiroje projekto puslapio dalyje. | shorten: the one inline paragraph; takes in the one must-know fact of Point1 (it is public at once) | [juodraštis] Užregistruota organizacija iš karto rodoma viešai ir savo vardu gali skelbti pozicijas dėl Seimo projektų. |
| R2 | `Org.Register.Point1` (AU) | `:23` `ul.small` | Organizacija rodoma iš karto; niekas jos iš anksto netvirtina. Moderatoriai peržiūri naujas organizacijas po to. | under a link „Kaip tai veikia“ (with R3, R4, R6), shortened: "shown at once" moved to R1 | [juodraštis] Niekas jos iš anksto netvirtina; moderatoriai naujas organizacijas peržiūri vėliau. |
| R3 | `Org.Register.Point2` (AU) | `:24` `ul.small` | Jūs tapsite pirmuoju jos atstovu. Atstovų vardai viešai nerodomi, o tai, ko prašote jūs pats, lieka privatu ir su organizacija nesiejama. | under a link „Kaip tai veikia“, shortened (nothing of the person becomes public, so not a must-know) | [juodraštis] Jūs tapsite pirmuoju jos atstovu. Atstovų vardai viešai nerodomi, o tai, ko prašote jūs pats, su organizacija nesiejama. |
| R4 | `Org.Register.Point3` (AU) | `:25` `ul.small` | Iš pradžių organizacija rodoma kaip „Nepatvirtinta“. Ją patvirtinsite el. pašto adresu organizacijos svetainės domene arba paprašę moderatorių. | under a link „Kaip tai veikia“ | (unchanged) |
| R5 | `Org.Register.Point4` (AU) | `:26` `ul.small` | Niekas neparduodama: jokia įmoka nesuteikia vietos, eilės ar paminėjimo. | to methodology: already there, `Method.Organisations.4` (AT) in „Organizacijų pozicijos“ (`/metodika#nothing-paid`); reached through R6 | – (key can go once nothing uses it) |
| R6 | `Org.Register.Method` (AU) | `:28` `.fine` link | Kaip veikia organizacijos ir ką reiškia patvirtinimas | keep, moved inside the disclosure as its last line | (unchanged) |
| R7 | *new* `Org.Register.More` (AU) | summary of the new `<details class="more">` replacing `ul#register-what` and the `.fine` line | – | new summary text | [juodraštis] Kaip tai veikia |
| R8 | `Org.Field.WebsiteHint` (AU) | `OrganisationForm.razor:25` `.field__hint` | Nebūtina. Adresas, prasidedantis https://. Pagal svetainės domeną organizaciją galėsite patvirtinti. | shorten: keep the format; the confirmation sentence is R4 | [juodraštis] Nebūtina. Adresas, prasidedantis https://. |
| R9 | `Org.Field.DescriptionHint` (AU) | `:18` `.field__hint` | {0} / {1} ženklų | keep (limit) | |
| R10 | `Org.Field.Name`, `.Description`, `.Website`, `.LegalCode`, `.Lobbyist` (AU) | `:12–37` labels | Pavadinimas · Trumpas aprašymas savais žodžiais (nebūtina) · Svetainė · Juridinio asmens kodas (nebūtina) · Esame registruoti lobistai | keep (labels). See finding F2: `Org.Field.LegalCode` is also the profile's fact label, where "(nebūtina)" is wrong | |
| R11 | `Restricted.Title/Until/UntilLifted/Reason/Object/Rules` (AU) | `WritingRestricted.razor:7–11` `.note` | Moderatorius apribojo jūsų galimybę rašyti. … Nurodyti, ko prašote, ir žymėti „Man svarbu“ galite kaip anksčiau. Jei nesutinkate, parašykite moderatoriams – adresas moderavimo taisyklėse | keep (status shown instead of the form; says where to object) | |
| R12 | `NeedsScript` (UT) | `NeedsScript.razor:6` `noscript .note` | Šios puslapio dalies mygtukams reikia „JavaScript“: … | keep (only without JavaScript) | |
| R13 | `Org.Edit.Saved` (AU) | `OrganisationForm.razor:113` status | Išsaugota. Naujas pavadinimas ar svetainė peržiūrėti pateks moderatoriams; ankstesni lieka viešoje istorijoje. | keep (status after a change on the profile; says what becomes public) | |

Markup after (for the implementer):

```html
<h1>Registruoti organizaciją</h1>
<NeedsScript />
<p class="lead">R1</p>
<details class="more stack stack--tight" id="register-more">
    <summary class="linkish small">Kaip tai veikia</summary>
    <ul class="small stack stack--tight" id="register-what"><li>R2</li><li>R3</li><li>R4</li></ul>
    <p class="fine"><a href="/metodika#organizacijos">R6</a></p>
</details>
```

The ids `register-what` and the methodology link stay, so tests that look for them find them (inside the closed
`<details>` they are in the markup but not visible; a test asserting visibility would need to open it).

**After:** title, one sentence (public at once, speaks in its own name), „Kaip tai veikia“, five fields with labels and
two short hints, „Registruoti“. Inline explanation: about 720 → about 160 characters.

---

## 2. A bill's page `/seimas/{number}`

`src/Lt2.Web/Components/Parliament/BillPage.razor` with the islands `SidePicker`, `ShareLine`, `FollowButton`,
`AppealReasons`, `ReportAppeal`/`ReportForm`/`VisitorReport`, `OrganisationPositions`/`StatementView`, `DateFlagForm`,
and (moderators only) `AppealModerationPanel`, `StatementModerationPanel`. The busiest screen: for a signed-in member
with a side on an open bill, 11 explanatory lines are visible before scrolling to the votes.

### 2a. Header, description, sides, threshold

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| B1 | `Bill.PlannedMoves` (AT) | `BillPage.razor:44` `.small` beside the „Planuojama …“ tag | Seimo darbotvarkės dažnai keičiasi, todėl data gali pasislinkti. | to methodology: the tag already says „Planuojama“ and the stages section links „Iš kur žinoma planuojama data“. Methodology needs a new part „Planuojamos datos“ (M1 below); `/moderavimo-taisykles` „Planuojamos datos“ (`Rules.Dates.1`) says it only for moderators | – |
| B2 | `Follow.What.bill` (AU) | `FollowButton.razor:16` `.small` | Gausite laišką, kai projektas bus įtrauktas į darbotvarkę ir kai Seimas dėl jo balsuos. | keep (one sentence; what pressing „Sekti“ does) | |
| B3 | `Follow.Following.bill` (AU) | `FollowButton.razor:16` `.small` | Sekate šį projektą. Kas ką seka, niekas kitas nemato. | shorten (same text for all three kinds, B3/O5/P3) | [juodraštis] Sekate. To niekas kitas nemato. |
| B4 | `Follow.SignIn.bill` (AU) | `FollowButton.razor:10` `.small` (visitor) | Prisijungę gausite laišką, kai projektas bus įtrauktas į darbotvarkę ir kai Seimas dėl jo balsuos. | keep (one sentence) | |
| B5 | `What.Source.*`, `What.MadeOn`, `What.MadeFrom`, `History.Link`, `What.ESeimas` (AT) | `BillPage.razor:85–110` `.fine` | „Sukurta DI · žmonių neperžiūrėta“ · Parengta {0} · Oficialus tekstas, iš kurio parengta · Keitimų istorija · Projekto tekstas e. seime | keep (design 8.13 requires label, date, source, history beside the description) | |
| B6 | `What.None` (AT) | `BillPage.razor:119` `p`; also `AppealHistory.razor:29` | Šio projekto aprašymo dar nėra. Ką jis keičia, rašoma oficialiame projekto tekste. | shorten: the next line is the link „Projekto tekstas e. seime“ | [juodraštis] Šio projekto aprašymo dar nėra. |
| B7 | `Sides.Older` (AT) ×2 | `:133`, `:138` `.small` under each figure | iš jų {0} – paskyros senesnės nei 30 d. | keep (figure caption; design requires the 30-day count) | |
| B8 | `Sides.Nobody` (AT) | `:144` `.small` | Dėl šio projekto dar niekas nenurodė, ko prašo Seimo narių. | keep (status) | |
| B9 | `Closed.Frozen` (AT) | `:148` `.small` | Seimas balsavo galutinai; skaičiai užfiksuoti {0} ir nebesikeičia. | keep (status) | |
| B10 | `Surge.Note` (AT) | `:152` `.note` | Per paskutines {0} valandas {1} % pozicijų pateikė paskyros, sukurtos per tą patį laiką. Skaičius „senesnės nei 30 d.“ jų neapima. | keep (a data warning beside the numbers it qualifies) | |
| B11 | `Side.SignIn` (AU) | `SidePicker.razor:17` `.small` (visitor) | Prisijunkite, kad galėtumėte nurodyti, ko prašote. Jūsų pasirinkimas nebus viešas. | keep (what becomes public, before acting) | |
| B12 | `Consent.Title`, `Consent.Text`, `Consent.Check` (AU) | `SidePicker.razor:22–26` | Tai, ar prašote balsuoti už, ar prieš, yra jūsų politinė nuomonė – ypatingai saugomi asmens duomenys. … (≈330 characters) | keep in full (consent to special-category data; shown only on the first side) | |
| B13 | `Side.Yours.for` / `.against` (AU) | `SidePicker.razor:48` `.small` | Jūs prašote balsuoti už. Tai matote tik jūs. | keep | |
| B14 | `Side.Private` (AU) | `SidePicker.razor:55` `.small` (signed in, no side yet) | Jūsų pasirinkimas nėra viešas. Jį galite keisti, kol Seimas nebalsavo. | shorten; "changeable until the vote" to methodology (new part M2) | [juodraštis] Jūsų pasirinkimas nėra viešas. |
| B15 | `Threshold.SetBy.default` (AT) | `BillPage.razor:165` `.small` | Riba ta pati visiems projektams. | delete (true for almost every bill; the exception B17 stays) | – |
| B16 | `Threshold.Explained` (AT) | `:165` `.small` | Riba – kiek dalyvių, abiejų pusių kartu, turi turėti projektas, kad patektų į suvestines Seimo nariams. | to methodology: already in „Kas patektų į suvestines Seimo nariams“ → „Riba“ (`Method.Digests.Threshold` / `ThresholdValue`); needs an anchor `id="riba"` on that row. Replaced by a link (B18) | – |
| B17 | `Threshold.SetBy.moderator` + `Threshold.Note` (AT) | `:165`, `:168` `.small` | Šio projekto ribą nustatė moderatorius. · Moderatoriaus paaiškinimas: {0} | keep (design: the bill's own threshold comes with a public statement) | |
| B18 | *new* `Threshold.What` (AT) | after the „{n} dalyvių · riba {N}“ line, `a.small` → `/metodika#riba` | – | new link text | [juodraštis] Kas yra riba? |
| B19 | `Share.ToGo` / `Share.Reached` (AU) | `ShareLine.razor:13` | Iki ribos trūksta {0} dalyvių · Riba pasiekta | keep (data) | |
| B20 | `Share.Lead` (AU) | `ShareLine.razor:14` `.small` | Pakvieskite kitus: jūsų asmeninė kvietimo nuoroda atveria šio projekto puslapį. Ji ta pati, kad ir ko prašote patys, ir nieko apie jūsų pasirinkimą neatskleidžia. | shorten to one sentence; keeps the reassurance that sharing reveals nothing | [juodraštis] Pakvieskite kitus savo kvietimo nuoroda: ji atveria šį projektą ir neatskleidžia, ko prašote jūs. |
| B21 | `Share.NoLink` (AU) | `ShareLine.razor:28` `.small` | Jūsų kvietimo kodas šiuo metu neveikia, todėl nuorodos parodyti negalime. Daugiau – paskyros puslapyje. | keep (status) | |
| B22 | `Closed.EachMp` (AT) | `:199` summary | Kaip balsavo kiekvienas Seimo narys | keep; give the `<details>` the class `more`. **Superseded by 8.17** (2026-11-15): the `<details>` list is gone; the closed bill links to the vote page's ballot list (`#balsai`) | |

### 2b. Arguments (`AppealReasons.razor`)

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| B23 | `Reasons.None` (AU) | `:26` `.small` | Argumentų dar nėra. | keep (empty state) | |
| B24 | `Reasons.NeedSide` / `Reasons.EndorsementsLeft` / `Reasons.Left` / `Reasons.NoneLeft` (AU) | `:105` `.small` | „Man svarbu“ žymėti gali tie, kurie nurodė, ko prašo. (limits: only when set) | keep (says why the „Man svarbu“ buttons are disabled; a `title` alone does not reach phones) | |
| B25 | `Reasons.EitherSide` (AU) | `:113` `.small`, in the writing form | Argumentą galite parašyti už arba prieš, nepriklausomai nuo to, ko prašote patys, ir net jei dar nenurodėte. Prie argumento nerodoma, ko prašote jūs. | to methodology: already in „Argumentai“ (`Method.Reasons.1`, `/metodika#argumentai`). The switch right below offers both sides; what becomes public is in B26. Optional link from B26's hint: „Kaip skelbiami argumentai“ | – |
| B26 | `Reasons.Hint` (AU) | `:134` `.field__hint` | Paprastas tekstas, {0} iš {1} ženklų. Argumentas bus viešas su jūsų vardu ir pasirinkta puse. | keep (limit + what becomes public) | |
| B27 | `Reasons.SimilarText` (AU) | `:141` `.small` | Gal jūsų mintį kas nors jau parašė? Jei argumentas tos pusės, kurios prašote patys, galite pažymėti „Man svarbu“; arba vis tiek paskelbkite savo. | shorten (the own-side rule is on the disabled button and B24) | [juodraštis] Gal jūsų mintį kas nors jau parašė? Galite pažymėti „Man svarbu“ arba vis tiek paskelbti savo. |
| B28 | `Reasons.WithdrawConfirm` (AU) | `:71` `.small` (after „Atsiimti“) | Argumentas bus ištrintas kartu su jo „Man svarbu“ žymomis. Jau sudarytose suvestinėse jo tekstas liks. | keep (cannot be undone; already one click away) | |
| B29 | `Report.Visitor` + `Report.OrWrite` (AU) | `VisitorReport.razor:9–11` inside `<details>` | Pranešti apie turinį moderatoriams gali prisijungę Lietuva 2.0 naudotojai: prisijunkite arba parašykite … | keep (already collapsed); class `more` | |

### 2c. Organisations' statements (`OrganisationPositions.razor`, `StatementView.razor`)

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| B30 | `Org.Section.NotCounted` (AU) | `:12` `.small` | Tai organizacijų nuomonės jų pačių vardu. Jos neskaičiuojamos tarp dalyvių ir nekeičia nei skaičių, nei artėjimo prie ribos. | shorten (design: the section says statements are not counted); one text shared with O4 | [juodraštis] Organizacijų pozicijos neskaičiuojamos tarp dalyvių. |
| B31 | `Org.Section.None` (AU) | `:23` `.small` | Kol kas nėra. | keep | |
| B32 | `Org.Section.Frozen` (AU) | `:57` `.fine` | Seimas dėl projekto jau balsavo: pozicijos nebekeičiamos. | keep (status) | |
| B33 | `Org.Section.Register` (AU) | `:65` `.small` link (member with no organisation) | Atstovaujate organizacijai? Užregistruokite ją ir paskelbkite jos poziciją. | shorten (it is a link; shorter reads as one) | [juodraštis] Atstovaujate organizacijai? Užregistruokite ją. |
| B34 | `Org.Mine.Private` (AU) | `:71` `.fine` (representatives) | Tai matote tik jūs ir kiti organizacijos atstovai. Pozicija skelbiama organizacijos vardu: kas ją parašė, nerodoma, ir ji niekaip nesusijusi su tuo, ko prašote jūs pats. | shorten; the rest is B36 and methodology (M3) | [juodraštis] Tai matote tik jūs ir kiti atstovai. |
| B35 | `Org.Refusal.*` (AU) | `:81` `.note--quiet` | Organizacija užblokuota moderatorių: … | keep (status) | |
| B36 | `Org.Form.Lead` (AU) | `:109` `.small` (form open) | Pozicija skelbiama iš karto organizacijos vardu, su jos žyma. Kiekvienas pakeitimas lieka viešoje istorijoje organizacijos puslapyje. Pozicija neskaičiuojama tarp dalyvių. | shorten: keep what becomes public; "not counted" is B30 a few lines up | [juodraštis] Pozicija skelbiama iš karto organizacijos vardu, o kiekvienas jos pakeitimas lieka viešoje istorijoje. |
| B37 | `Org.Form.TextHint` (AU) | `:126` `.field__hint` | {0} / {1} ženklų | keep | |
| B38 | `Org.Form.LinkHint` (AU) | `:131` `.field__hint` | Adresas, prasidedantis https://. Šalia bus rodomas svetainės pavadinimas. | shorten (format only) | [juodraštis] Adresas, prasidedantis https://. |
| B39 | `Org.Form.GoalHint` (AU) | `:140` `.field__hint` | Skaičiuojami dalyviai, prašantys to paties, ko prašo organizacija. Tikslas tik jūsų nariams ir rėmėjams: jis nekeičia ribos ir nepatenka į suvestines. | shorten: the first sentence is needed to fill the field; the second is in `Method.Organisations.2` | [juodraštis] Skaičiuojami dalyviai, prašantys to paties, ko prašo organizacija. |
| B40 | `Org.Form.WithdrawConfirm` (AU) | `:99` `.small` | Pozicija nebebus rodoma. Ankstesnės jos versijos lieka organizacijos puslapyje. Atsiimti? | keep (already after a click; says what stays public) | |
| B41 | `Org.Link` (AU) | `StatementView.razor:38` | Visa pozicija: | keep (label) | |

### 2d. Aside

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| B42 | `Digests.OnAppeal.None` (AT) | `BillPage.razor:235` `.small` | Šis projektas dar nebuvo nė vienoje suvestinėje. Į suvestines patenka projektai, pasiekę ribą. | shorten (the rule is B18's link) | [juodraštis] Šis projektas dar nebuvo nė vienoje suvestinėje. |
| B43 | `Digests.OnAppeal.Frozen` (AT) | `:253` `.fine` | Skaičiai tokie, kokie buvo suvestinėje; skliaustuose – paskyros senesnės nei 30 d. | keep (legend of the numbers above it) | |
| B44 | `Digests.DryRun` (AT) | `:255` `.fine` | Bandymas: suvestinės sudaromos, bet Seimo nariams nesiunčiamos. | keep (must not mislead that MPs receive anything) | |
| B45 | `Flag.Open` and the form (AU) | `DateFlagForm.razor:10` summary | Data neteisinga? | keep (already collapsed); class `more` | |
| B46 | `NotAPoll` (AT) | `:263` `.note--quiet` | Tai nėra reprezentatyvi apklausa: skaičiai rodo savanoriškai dalyvaujančių Lietuva 2.0 naudotojų nuomonę. | keep (AGENTS.md: every place that shows the numbers says so) | |
| B47 | `Planned.ByModerator`, `Planned.Source`, `Bill.ESeimas` (AT/PA) | `:295–303` `.fine` | Planuojamą datą patikslino moderatorius. · Iš kur žinoma planuojama data · Projektas e-seime | keep (short, source links) | |
| B48 | `Hidden.Heading`, `Hidden.Lead`, `Hidden.Statement` (AT) | `:63–67` (record hidden) | Moderatoriai uždarė dalyvavimą dėl šio projekto: nurodyti, ko prašote, ir rašyti argumentų dabar negalima. | keep (status) | |
| B49 | `ModPanel.Lead` (AU) | `AppealModerationPanel.razor:16` `.small` (moderators) | Tai matote kaip moderatorius. Kiekvienas veiksmas registruojamas; argumento autorius gauna jūsų paaiškinimą. | keep (staff; what follows from their action) | |

**After:** number, title, „Planuojama …“, „Sekti“ with one line; the description with its source line; the counts with
their 30-day captions; the side buttons with „Tai matote tik jūs“; „{n} dalyvių · riba {N}“ with „Kas yra riba?“; one
share sentence; the arguments; „Organizacijų pozicijos neskaičiuojamos tarp dalyvių.“; aside: summaries (one line),
„Bandymas …“, the not-a-poll note. 11 explanatory lines → 8 and a link, ≈ 950 → ≈ 550 characters for that state.

---

## 3. Organisation profile `/o/{slug}`

`src/Lt2.Web/Components/Organisations/OrganisationPage.razor`; representatives also see the island
`Organisations/OrganisationManage.razor` with `OrganisationInvitationLink.razor`.

### 3a. Everyone

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| O1 | `Org.Profile.Meaning.none/.domain/.moderator` (AU) + `Org.Profile.MethodLink` | `OrganisationPage.razor:89` `.fine` | „Patvirtinta per el. pašto adresą“: organizacijos atstovas gavo kodą el. pašto adresu nurodytame domene. Tai rodo, kad organizacija valdo domeną, bet ne tai, kad ji yra ta, kuo save vadina. Daugiau | under a link: `<details class="more">` with the meaning and „Daugiau“ inside. The methodology already has „Ką reiškia žymos“ (`Method.Organisations.Labels`, `id="m-organisation-labels"`) | summary [juodraštis]: Ką reiškia ši žyma? |
| O2 | `Org.Profile.LobbyistYes` / `No` (AU) | `:85` `dd` | taip, pačios organizacijos teigimu · nenurodė | keep (data) | |
| O3 | `Org.Profile.StatementsLead` (AU) | `:96` `.small` | Kiekviena pozicija rodoma su visomis ankstesnėmis versijomis. | delete: each statement's „Ankstesnės versijos ({0})“ says it | – |
| O4 | `Org.Profile.NotCounted` (AU) | `:156` `.note--quiet` aside | Organizacijų pozicijos neskaičiuojamos tarp dalyvių ir nekeičia projektų skaičių. | shorten, same text as B30 (one key could serve both) | [juodraštis] Organizacijų pozicijos neskaičiuojamos tarp dalyvių. |
| O5 | `Follow.Following.organisation` (AU) | `FollowButton.razor:16` `.small` | Sekate šią organizaciją. Kas ką seka, niekas kitas nemato, ir pati organizacija taip pat. | shorten (as B3) | [juodraštis] Sekate. To niekas kitas nemato. |
| O6 | `Follow.What.organisation` / `Follow.SignIn.organisation` (AU) | `FollowButton.razor:16` / `:10` `.small` | Gausite laišką, kai organizacija paskelbs poziciją dėl projekto. | keep (one sentence) | |
| O7 | `Org.Profile.Frozen` (AU) | `:108` `.fine` | Seimas dėl projekto jau balsavo: pozicija nebekeičiama. | keep (status) | |
| O8 | `Org.Profile.BlockedLead`, `.BlockedWhy`, `.Closed` (AU) | `:49–58` | Moderatoriai užblokavo šią organizaciją: jos pozicijos niekur nerodomos. | keep (status) | |
| O9 | `Org.Profile.NoDescription`, `.NoStatements` (AU) | `:74`, `:99` `.small` | Organizacija aprašymo nepateikė. · Organizacija dar nepaskelbė nė vienos pozicijos. | keep (empty states) | |

### 3b. Representatives only (`OrganisationManage.razor`, `OrganisationInvitationLink.razor`)

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| O10 | `Org.Manage.Lead` (AU) | `OrganisationManage.razor:16` `.small` | Šią dalį matote tik jūs ir kiti organizacijos atstovai. | keep | |
| O11 | `Org.Confirm.Lead` (AU) | `:36` `.small` | Nurodykite el. pašto adresą domene {0} (arba jo subdomene). Atsiųsime kodą; jį įvedus, prie organizacijos bus rodoma, kad ji patvirtinta per el. pašto adresą šiame domene. | shorten | [juodraštis] Nurodykite el. pašto adresą domene {0} arba jo subdomene – atsiųsime kodą. |
| O12 | `Org.Confirm.NotStored` (AU) | `:46` `.fine` | Adreso nesaugome: išsaugomas tik domenas. Viešųjų pašto tarnybų adresai organizacijos nepatvirtina. | keep (what is stored; and a public-mail address will fail) | |
| O13 | `Org.Confirm.Unavailable.*` + `Org.Confirm.AskModerators` (AU) | `:29–31` `.small` | Domenu patvirtinti galima tik organizaciją, turinčią svetainę: … | keep (status) | |
| O14 | `Org.Reps.Private` (AU) | `:66` `.fine` | Atstovų vardus matote tik jūs ir kiti atstovai; viešai jie nerodomi. | delete: O10 says the same at the top of this section | – |
| O15 | `Org.Invite.Made` (AU) | `:90` `p` (after „Pakviesti kolegą atstovauti“) | Perduokite šią nuorodą kolegai. Ji tinka vieną kartą, iki {0}. Ją atvėręs ir sutikęs prisijungęs narys taps atstovu. | shorten (the invite page itself says what happens) | [juodraštis] Perduokite šią nuorodą kolegai. Ji tinka vieną kartą, iki {0}. |
| O16 | `Org.leave.Confirm`, `Org.close.Confirm` (AU) | `:110` `.small` (after a click) | Esate paskutinis atstovas. Uždarius organizaciją … To atšaukti negalima. | keep (cannot be undone) | |
| O17 | `Org.Code.Lead` (AU) | `OrganisationInvitationLink.razor:16` `.small` | Ši nuoroda atveria registracijos formą su organizacijos kvietimo kodu. Dalinkitės ja su savo nariais ir rėmėjais. | shorten | [juodraštis] Dalinkitės ja su nariais ir rėmėjais: nuoroda atveria registraciją su organizacijos kvietimo kodu. |
| O18 | `Org.Code.BillsLead` (AU) | `:30` `.small` | Nuorodos su tuo pačiu kodu, kurios po registracijos atveria projektą, dėl kurio organizacija paskelbė poziciją: | shorten | [juodraštis] Tas pats kodas, bet po registracijos atveriamas projektas: |
| O19 | `Org.Code.Private` (AU) | `:59` `.fine` | Matote tik, kiek žmonių užbaigė registraciją su organizacijos kodu: ne kas jie, ne kada ir ne kurią pusę renkasi. Naujos paskyros su organizacija niekas nesieja. | shorten; the second sentence to methodology (M3, new sentence) | [juodraštis] Matote tik skaičių: ne kas užsiregistravo, ne kada ir ne kurią pusę pasirinko. |
| O20 | `Org.Code.OffWithOrganisation` / `.SwitchedOff` / `.None` / `.UsedUp` + `Org.Code.Ask` (AU) | `:54–56` `.small` | … todėl nuorodos parodyti negalime. | keep (status) | |
| O21 | `Org.Code.ReplaceConfirm` (AU) | `:65` `.small` (after a click) | Dabartinis kodas ir visos jo nuorodos nustos veikti. Skaičius išliks. | keep | |

**After (visitor):** name, label and lobbyist tags, „Sekti“ with one line, „Apie organizaciją“ facts with „Ką reiškia ši
žyma?“, the statements, one not-counted note. 4 → 2 explanatory lines inline. **After (representative):** the managing
part loses two paragraphs (one deleted, one behind a link) and six others get shorter: 11 → 8 lines, about 40 % fewer
characters.

---

## 4. Organisations list `/organizacijos`

`src/Lt2.Web/Components/Organisations/OrganisationList.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| L1 | `Org.List.Lead` (AU) | `:16` `.lead` | Organizacijos, skelbiančios savo pozicijas dėl Seimo projektų. Prie kiekvienos parašyta, ar ir kaip ji patvirtinta. Pozicijos neskaičiuojamos tarp dalyvių. | shorten: the labels are visible in each row; the link „Kaip veikia organizacijos“ is at the end | [juodraštis] Organizacijos, skelbiančios pozicijas dėl Seimo projektų. Jų pozicijos neskaičiuojamos tarp dalyvių. |
| L2 | `Org.List.Method` (AU) | `:53` link | Kaip veikia organizacijos | keep | |

**After:** title, two short sentences, search, the list, „Registruoti organizaciją“ and the methodology link.

---

## 5. Invitation to represent `/organizacijos/kvietimas/{token}`

`src/Lt2.Web.Client/Pages/OrganisationInvite.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| I1 | `Org.Invite.Lead` (AU) | `:34` `.lead` | Jus kviečia atstovauti organizacijai „{0}“. | keep | |
| I2 | `Org.Invite.What` (AU) | `:35` `.small` | Atstovas gali skelbti ir keisti organizacijos pozicijas, keisti jos duomenis ir kviesti kitus. Jūsų vardą matys tik kiti atstovai; tai, ko prašote jūs pats, lieka privatu. | shorten: what the person agrees to and who sees their name stay; the last clause to methodology (M3) | [juodraštis] Atstovas skelbia organizacijos pozicijas, keičia jos duomenis ir kviečia kitus. Jūsų vardą matys tik kiti atstovai. |
| I3 | `Org.Invite.Missing`, `.Unusable`, `.Already` (AU) | `:22–30` `.note` | … | keep (status) | |

**After:** title, „Jus kviečia …“, one two-sentence paragraph, „Tapti atstovu“.

---

## 6. Account `/paskyra`

`src/Lt2.Web.Client/Pages/Hello.razor` with `Appeals/FollowList.razor`, `Organisations/MyOrganisations.razor`,
`Pages/MyInvitationCode.razor`. Nine explanatory paragraphs, one per section plus the lead.

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| A1 | `Hello.SignedIn` (UT) | `Hello.razor:22` `.lead` | Esate prisijungę prie Lietuva 2.0. | delete: the greeting and the menu show it | – |
| A2 | `Activation.Text` (UT) | `:37` (staff, awaiting activation) | Jums suteiktas darbuotojo vaidmuo. … kiti paskyros prieigos raktai bus pašalinti. | keep (staff; something is removed) | |
| A3 | `Passkeys.Text` (UT) | `:52` `p` | Prieigos raktas leidžia prisijungti be kodo: piršto atspaudu, veidu arba įrenginio PIN kodu. | keep (one sentence that says what a passkey is) | |
| A4 | `Sessions.Text` (UT) | `:59` `p` | Čia matote, kur esate prisijungę. Nepažįstamą sesiją galite baigti. | shorten | [juodraštis] Nepažįstamą sesiją galite baigti. |
| A5 | `Party.Text` (UT) | `:103` `p` | Nebūtina. Jūsų pasirinkimo niekam nerodome. Seimo nariui jo suvestinėje parodytume tik bendrą skaičių, … tik tada, kai jų yra bent 20. | keep (what is stored and how it is used; the consent box refers to it) | |
| A6 | `Party.Consent` (UT) | `:118` | Sutinku, kad Lietuva 2.0 saugotų mano nurodytą partiją taip, kaip aprašyta. Galiu ją bet kada pašalinti. | keep (consent) | |
| A7 | `PositionConsent.Text` (UT) | `:130` `p` | Esate sutikę, kad saugotume jūsų pozicijas … Atšaukus sutikimą, visos jūsų pozicijos ir „Man svarbu“ žymos bus ištrintos … | keep (consent; cannot be undone) | |
| A8 | `Follows.Lead` (AU) | `FollowList.razor:10` `p` | Apie sekamus projektus siunčiame laišką, kai projektas įtraukiamas į Seimo darbotvarkę ir kai Seimas dėl jo balsuoja; apie sekamas organizacijas – kai jos paskelbia poziciją dėl projekto. Kas ką seka, mato tik jūs. | shorten: the three email switches at the end of the section (`Follows.Email.planned/.voted/.statements`) list exactly these events | [juodraštis] Ką sekate, matote tik jūs. |
| A9 | `Follows.None` (AU) | `FollowList.razor:15` `.small` | Kol kas nieko nesekate. Projektą, Seimo narį ar organizaciją galite sekti jų puslapyje. | keep (empty state) | |
| A10 | `Follows.Max` (AU) | `:82` `.small` | Sekate {0} iš daugiausia {1}. | keep (limit) | |
| A11 | `Org.MyList.None` (AU) | `MyOrganisations.razor:32` `.small` | Jūs neatstovaujate jokiai organizacijai. | keep | |
| A12 | `MyInvitation.Lead` (UT) | `MyInvitationCode.razor:15` `p` | Pakvieskite kitus: nuoroda atveria registraciją su jūsų kodu, o kodą galima įvesti ir ranka. | keep (one sentence) | |
| A13 | `MyInvitation.Private` (UT) | `:34` `.small` | Matote tik skaičių – ne kas užsiregistravo ir ne kada. | keep (short) | |
| A14 | `MyInvitation.Off` (UT) | `:30` `.note` | Jūsų kvietimo kodą išjungė administratorius … | keep (status) | |
| A15 | `MyInvitation.ReplaceConfirm` (UT) | `:40` `.small` (after a click) | Bus sukurtas naujas kodas, o senasis nebetiks. Skaičius išliks. | keep | |
| A16 | `Data.Text` (UT) | `Hello.razor:146` `p` | Galite atsisiųsti viską, ką mums pateikėte, arba ištrinti paskyrą. Ištrynus paskyrą, ištrinamos ir jūsų pozicijos, „Man svarbu“ žymos, argumentai ir pranešimai, o bendri skaičiai sumažėja. Jau sudarytose suvestinėse skaičiai lieka tokie, kokie buvo. | under a link: keep the first sentence; the consequences move into the confirmation that „Ištrinti paskyrą“ already opens (A17). Nothing is lost before acting: the confirmation is the step before deleting | [juodraštis] Galite atsisiųsti viską, ką mums pateikėte, arba ištrinti paskyrą. |
| A17 | `Data.DeleteWarning` (UT) | `:150` `.note` (after „Ištrinti paskyrą“) | Paskyra bus ištrinta visam laikui. To atšaukti negalima. | keep, extended with A16's consequences | [juodraštis] Paskyra bus ištrinta visam laikui kartu su jūsų pozicijomis, „Man svarbu“ žymomis, argumentais ir pranešimais. Bendri skaičiai sumažės, o jau sudarytose suvestinėse liks tokie, kokie buvo. To atšaukti negalima. |

**After:** greeting and tags; each section with its heading, at most one short line (two where consent needs it), its
controls. 9 → 8 paragraphs; the lead and the longest part of „Jūsų duomenys“ and „Ką sekate“ gone from view
(≈ 1 200 → ≈ 750 characters).

---

## 7. Sign-in and registration `/prisijungti` (and the front page, which embeds the same steps)

`src/Lt2.Web.Client/Pages/SignIn.razor`, `Components/SignInFlow.razor`, `src/Lt2.Web/Components/Layout/MainLayout.razor:40`.

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| S1 | `Welcome.Lead` (UT) | `SignIn.razor:12` `.lead` | Pasakykite Seimo nariams, kaip prašote balsuoti, ir sužinokite, ką jie atsakė. | keep (see finding F6: promises MP answers, an M2b feature) | |
| S2 | `SignIn.Intro` (UT) | `SignInFlow.razor:24` `p` | Atsiųsime vienkartinį kodą el. paštu. Slaptažodžio nereikia. | keep | |
| S3 | `SignIn.PasskeyHint` (UT) | `:34` `.small` | Prieigos raktas – tai piršto atspaudas, veidas arba įrenginio PIN kodas vietoj kodo el. paštu. | shorten (says the same as A3 in other words) | [juodraštis] Piršto atspaudu, veidu ar įrenginio PIN kodu – be kodo el. paštu. |
| S4 | `Invitation.Hint` (UT) | `:56` `.field__hint` | Kol platforma bandoma, registruotis galima tik su kvietimu. Kas jums davė kodą, matys, kad juo užsiregistruota, bet ne kas užsiregistravo. | shorten; both facts stay (why the field, what the inviter sees) | [juodraštis] Kol vyksta bandymas, registruotis galima tik su kvietimo kodu. Kodą davęs žmogus matys tik skaičių, ne jus. |
| S5 | `Register.Fine` (UT) | `:60` `.fine` | Išsiųsime laišką šiuo adresu. Ar adresas jau registruotas, čia neparodome. | shorten: the second sentence explains a security behaviour nobody needs before acting | [juodraštis] Išsiųsime laišką šiuo adresu. |
| S6 | `DisplayName.Hint` (UT) | `:76` `.field__hint` | Šį vardą kiti matys prie jūsų parašytų argumentų. Ar prašote balsuoti už, ar prieš – nemato niekas. | keep (what becomes public) | |
| S7 | `Code.SentAfter*`, `Code.NewestOnly` (UT) | `:105–124` | Jei adresu … yra paskyra, kodą jau išsiuntėme. Jis galioja 10 minučių. · Jei gavote kelis laiškus, tinka tik naujausias kodas. | keep (needed to finish) | |
| S8 | `NoScript` (UT) | `:18` `noscript` | Prisijungti ir registruotis galima tik įjungus „JavaScript“. … | keep | |
| S9 | `CannotSignIn` (FT) | `MainLayout.razor:40` `.small` | Nepavyksta prisijungti? Parašykite mums: | keep | |

**After:** sign-in: two lines; registration: one hint, one fine line; completion: the name hint and the age box.

---

## 8. Front page `/`

`src/Lt2.Web/Components/Pages/Home.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| H1 | `Home.Kicker` (PT) | `:20` `.lead` | Žmonės ir Seimas – vienoje vietoje. | keep (tagline) | |
| H2 | `Home.Lede` (PT) | `:21` `p` | Lietuva 2.0 kuriama tam, kad būtų paprasčiau matyti, kaip dirba Seimas, ir aiškiai pasakyti Seimo nariams, ko prašote. | keep (the one "what this is" paragraph) | |
| H3 | `Home.Trial` (PT) | `:22` `.note` | Vyksta uždaras bandymas: registruotis galima tik su kvietimo kodu. | keep (must know before trying to register) | |
| H4 | `Home.WelcomeText` (PT) | `:28` `p` (signed in) | Paskyroje rasite sekamus projektus, nustatymus ir kvietimo kodą. | delete: the button „Mano paskyra“ follows | – |
| H5 | `Agenda.NonePlanned` (PT) | `:46` | Šiuo metu darbotvarkėse projektų nėra. Kai Seimas juos suplanuos, jie atsiras čia. | keep (empty state) | |
| H6 | `Home.Older` (PT) | `:93–94` `.small` | (iš jų {0} – paskyros senesnės nei 30 d.) | keep (caption) | |
| H7 | `Agenda.OlderNote` (PT) | `:123` `.small` | Skliaustuose – kiek iš jų paskyrų senesnės nei 30 d. | keep (legend of the agenda rows) | |
| H8 | `NotAPoll` (AT) | `:124` `.note--quiet` | Tai nėra reprezentatyvi apklausa … | keep | |
| — | S2–S5 | the embedded sign-in steps | | as in section 7 | |

**After:** logo, tagline, one paragraph, the trial note, the sign-in sheet (or „Mano paskyra“), the agenda, the cards,
the legend and the not-a-poll note.

---

## 9. Projects `/projektai`

`src/Lt2.Web/Components/Appeals/AppealList.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| P1 | `List.Lead` (AT) | `:16` `.lead` | Seimo svarstomi projektai: dėl kiekvieno galite nurodyti, ko prašote Seimo narių – balsuoti už ar prieš – ir parašyti argumentų. Pirmiau rodomi projektai, dėl kurių Lietuva 2.0 naudotojai aktyviausi. | shorten: the order is shown by „Rikiuoti: …“ | [juodraštis] Seimo svarstomi projektai: dėl kiekvieno galite nurodyti, ko prašote Seimo narių, ir parašyti argumentų. |
| P2 | `List.Counts` (AT) | `:110` `.small` in every row | prašo už {0} (iš jų {1} – senesnės nei 30 d. paskyros), prieš {2} (iš jų {3}) | shorten: same form as the agenda rows, with one legend under the list (reuse the wording of `Agenda.OlderNote`, as a key in AT) | [juodraštis] už {0} ({1}) · prieš {2} ({3}) — legend: Skliaustuose – kiek iš jų paskyrų senesnės nei 30 d. |
| P3 | `NotAPoll` (AT) | `:131` | Tai nėra reprezentatyvi apklausa … | keep | |

**After:** title, one sentence, tabs, search, order line, rows of one short line each, legend, not-a-poll note.

---

## 10. Agenda `/darbotvarke`

`src/Lt2.Web/Components/Agenda/AgendaPage.razor`, `AgendaItemRow.razor`, `AgendaAppeal.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| G1 | `Agenda.Lead` (PT) | `AgendaPage.razor:15` `.lead` | Ką Seimas planuoja svarstyti artimiausiu metu ir dėl ko balsavo per pastarąją savaitę. Prie kiekvieno projekto matyti, ko prašo Lietuva 2.0 naudotojai; dalyvauti galima dėl kiekvieno. | shorten: the rows show both | [juodraštis] Ką Seimas planuoja svarstyti artimiausiu metu ir dėl ko balsavo per pastarąją savaitę. |
| G2 | `Agenda.Source` (PT) | `:22` `.small` (+ read time + source link) | Darbotvarkių projektus lrs.lt skelbia maždaug 10–12 dienų į priekį, ir jie dažnai keičiasi, todėl datos gali pasislinkti. | shorten; the 10–12 days to methodology (M1) | [juodraštis] Darbotvarkių projektai dažnai keičiasi, todėl datos gali pasislinkti. |
| G3 | `Agenda.ReadAt` / `Agenda.NeverRead` (PT) | `:25–29` | Paskutinį kartą perskaityta {0}. | keep (data freshness) | |
| G4 | `Agenda.NoTitle` (PT) | `AgendaItemRow.razor:34` `.small` | Projekto atviruosiuose Seimo duomenyse dar nėra. | keep (status) | |
| G5 | `Agenda.OlderNote`, `NotAPoll` | `:106–107` | | keep | |

**After:** title, one sentence; under „Ką Seimas planuoja svarstyti“ one line with the read time and the lrs.lt link.

---

## 11. Summaries `/suvestines`

`src/Lt2.Web/Components/Digests/DigestPage.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| D1 | `Digests.Lead` (AT) | `:25` `.lead` | Vieša kopija to, ką Seimo nariai gautų. Skaičiai ir argumentai užfiksuoti suvestinės sudarymo metu ir nebesikeičia. | keep (what this is) | |
| D2 | `Digests.DryRun` (AT) | `:26` `.note` | Bandymas: suvestinės sudaromos, bet Seimo nariams nesiunčiamos. | keep | |
| D3 | `Digests.How`, `.Who/.WhoValue`, `.When/.WhenValue`, `.What/.WhatValue` (AT) | `:103–107` aside box | Kaip sudaroma suvestinė: Kas gautų – Visi Seimo nariai, kurių oficialus adresas yra atviruosiuose duomenyse, vienodai; Kada – …; Kas patenka – Projektai, pasiekę ribą | to methodology: „Kas patektų į suvestines Seimo nariams“ has Kada (Savaitinė / Prieš posėdį) and Kas patenka (Riba, Kas patenka į savaitinę); it lacks „Kas gautų“ → add that row (M4). The box becomes one link | link [juodraštis]: Kaip sudaroma suvestinė → |
| D4 | `Digests.Seen` + `Digests.Methodology` (AT) | `:109` `.small` | Visada rodomos abi pusės ir argumentai, kuriuos peržiūrėjo moderatorius. Taisyklės ir dabartinės reikšmės | to methodology: already there („Skaičiai“: visada abi pusės; „Argumentai“: … kuriuos peržiūrėjo moderatorius) | – |
| D5 | `Digests.NoMpFigures` (AT) | `:124` `.fine` | Viešoje kopijoje nėra skaičių, skirtų konkrečiam Seimo nariui. | to methodology: needs a new row (M4) | – |
| D6 | `Surge.Note` (AT) | `:74` `.note` | | keep (data warning) | |
| D7 | `NotAPoll` (AT) | `:123`, `:134` | | keep | |

**After:** title, the lead, the dry-run note, the summary; aside: the earlier summaries, one link to the rules, the
not-a-poll note. 8 → 4 explanatory items.

---

## 12. Description history `/seimas/{number}/istorija`

`src/Lt2.Web/Components/Appeals/AppealHistory.razor`: labels and data only; `What.None` (B6) is shortened there too.
Nothing else to change.

---

## 13. MPs `/seimo-nariai`, `/p/{slug}`, votes, factions and committees

`src/Lt2.Web/Components/Parliament/*.razor`: lean already; data labels and captions.

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| M-1 | `Mps.Lead` (PA) | `MpList.razor:14` `.lead` | {0}. Profiliai sudaryti automatiškai iš Seimo atvirųjų duomenų. | keep | |
| M-2 | `Mp.Auto` (PA) | `MpProfile.razor:181` `.note--quiet` | Profilis sudarytas automatiškai iš Seimo atvirųjų duomenų. | delete: the source line at the foot of the page (`SourceLine`: „Šaltinis: … · Kaip tai skaičiuojama“) says it; the list's lead says it too | – |
| M-3 | `Follow.What.mp` / `Follow.SignIn.mp` (AU) | `FollowButton.razor` `.small` | Laiškuose apie jūsų sekamus projektus matysite, kaip balsavo šis Seimo narys. | keep (one sentence) | |
| M-4 | `Follow.Following.mp` (AU) | `FollowButton.razor` `.small` | Sekate šį Seimo narį. Kas ką seka, niekas kitas nemato. | shorten (as B3) | [juodraštis] Sekate. To niekas kitas nemato. |
| M-5 | `Stat.*`, `Stat.FactionPeriod` (PA) | `MpProfile.razor:57–83` `.small` | balsavo {0} iš {1} · {0} iš {1}, kai frakcija turėjo poziciją · Skaičiuojama tik narystės frakcijoje laikotarpiu: {0}. | keep (captions of the two statistics) | |
| M-6 | `Vote.ConsensusText` (PA) | `VotePage.razor` | Seimas pritarė bendru sutarimu: Seimo nariai nebalsavo atskirai, todėl balsų skaičių nėra. | keep (explains missing numbers where they are missing) | |
| M-7 | `Source.*` (PA) | `SourceLine.razor` `.fine` | Šaltinis: Lietuvos Respublikos Seimo atvirieji duomenys, CC BY 4.0 · atnaujinta {0} · Kaip tai skaičiuojama | keep (licence attribution) | |

---

## 14. Feedback balloon (every page) and `/atsiliepimai`

`src/Lt2.Web/Components/Feedback/FeedbackBalloon.razor`, `FeedbackContent.razor`, `FeedbackForm.razor`,
`FeedbackPage.razor`

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| F-1 | `NotChat` (FT) | `FeedbackBalloon.razor:21`, `FeedbackPage.razor:10` `.small` | Tai ne pokalbis: žinutę perskaito komanda, o atsako el. paštu, jei leidžiate. | shorten (the reply is explained at F-4) | [juodraštis] Tai ne pokalbis: žinutę skaito komanda. |
| F-2 | `TextHint` (FT) | `FeedbackForm.razor:19` `.field__hint` | Iki 2 000 ženklų. Nerašykite, kurią pusę pasirinkote dėl projekto – mums to nereikia. | keep (limit; keeps special-category data out) | |
| F-3 | `Attached` (FT) | `:39` `.small` | Kartu išsiųsime (galite atžymėti): | keep (what is sent) | |
| F-4 | `MayReplyHint` (FT) | `:53` `.small` | Pažymėjus, atsiliepimas bus susietas su jūsų paskyra, kad komanda galėtų atsakyti el. paštu (jūsų adreso ji nemato). Nepažymėjus, atsiliepimas išsaugomas be jokio ryšio su jūsų paskyra. | shorten; both facts stay (what is stored) | [juodraštis] Pažymėjus, atsiliepimą susiesime su paskyra ir atsakysime el. paštu (adreso komanda nemato). Kitaip jis išsaugomas be ryšio su paskyra. |
| F-5 | `Issues`, `IssuesLink`, `IssuesPublic` (FT) | `FeedbackContent.razor:23` `.small` (when `Feedback:IssuesUrl` is set) | Darbus komanda tvarko ir GitHub: ten galite pranešti apie klaidą ar pasiūlyti patobulinimą. Lietuva 2.0 darbai GitHub. Ten parašytas pranešimas yra viešas ir rodomas su jūsų GitHub vardu. | under a link: one summary, the two sentences inside | summary [juodraštis]: Pranešti viešai GitHub |
| F-6 | `Visitor` + `CannotSignIn` (FT) | `FeedbackContent.razor:14–17` | Atsiliepimus svetainėje gali palikti prisijungę Lietuva 2.0 naudotojai. | keep | |

**After:** the heading, one short line, the text box with its limit, the kind, what is attached, the reply box with
its two short sentences, „Siųsti“, and a closed „Pranešti viešai GitHub“.

---

## 15. Site-wide

| ID | Key (file) | Where | Current | Proposal |
|---|---|---|---|---|
| X1 | `Notice.InventedParliament` (PT) | `MainLayout.razor:32` `.note` | Seimo narių ir balsavimų duomenys čia išgalvoti – tai tik bandymas. | keep (the notice that data is invented) |
| X2 | `Footer.Line` (PT) | `MainLayout.razor:45` | Lietuva 2.0 · uždaras bandymas | keep |
| X3 | `Error.Text`, `NotFound.Text` (PT) | `Error.razor`, `NotFound.razor` | | keep (error pages) |

---

## 16. Staff screens (moderators and administrators)

Looked at for length only; staff need their instructions, and the action sentences (`Mod.Sentence.*`,
`OrgMod.Sentence.*`, `OrgMod.Restore.Sentence.*`, `Hidden.Restore*.Sentence*`) appear only after an action is chosen,
so they are already one click away: **keep** all of them.

| ID | Key (file) | Where | Current | Proposal | New text |
|---|---|---|---|---|---|
| T1 | `Mod.Lead` (AU) | `Moderation.razor:19` `.lead` | Projektai, kuriems reikia jūsų sprendimo (pranešta apie aprašymą, pastabos apie datą, staigus naujų paskyrų antplūdis, sulaikytas siuntimas, nepasiekta riba, nors projektas jau darbotvarkėje), ir pažymėti, praneštieji ar dar nematyti argumentai. Kiekvienas veiksmas registruojamas; argumento autorius gauna jūsų paaiškinimą. | shorten: each item's note says why it is there | [juodraštis] Projektai ir argumentai, kuriems reikia jūsų sprendimo. Kiekvienas veiksmas registruojamas; argumento autorius gauna jūsų paaiškinimą. |
| T2 | `Mod.UnderThreshold` (AU) | `Moderation.razor:56` `.note--quiet` | … todėl į suvestinę Seimo nariams jis nepateks. Galite patvirtinti vieną skubią suvestinę arba nieko nedaryti. | shorten: the action buttons are the options | [juodraštis] Projektas įtrauktas į Seimo darbotvarkę, planuojama {0}. Dalyvių {1} iš {2}, todėl į suvestinę Seimo nariams jis nepateks. |
| T3 | `Mod.DateFlags` (AU) | `:62` `.note--quiet` | … Patikrinkite nuorodas: pataisykite planuojamą datą, patvirtinkite skubią suvestinę arba palikite, kaip yra. | shorten (same reason) | [juodraštis] Nariai praneša, kad planuojama data neteisinga arba balsuojama anksčiau ({0}). Patikrinkite nuorodas. |
| T4 | `Mod.Surge` (AU) | `:72` `.note` | … Galite sulaikyti siuntimą, kol išsiaiškinsite, arba nieko nedaryti. | shorten (same reason) | [juodraštis] Per paskutines {0} val. {1} % naujų pozicijų pateikė per tą laiką sukurtos paskyros; projekto puslapyje rodoma pastaba apie šuolį. |
| T5 | `Mod.Reports`, `Mod.Reports.Description` (AU) | `Reports.razor:9` `.note--quiet` | Nariai pranešė apie šį turinį: {0}. Perskaitykite pranešimus: jei pažeidimo nėra, palikite, kitaip paslėpkite. Kas pranešė, nerodoma. | keep (guidance new moderators need at the point of decision); optional shorten later | |
| T6 | `Mod.Held`, `Mod.Refused`, `Admin.Refused`, `Hidden.ReasonNotOpen`, `OrgMod.Barred`, `OrgMod.Hidden.NotOpen` (AU) | various `.note` | | keep (status) | |
| T7 | `ModOpen.SourceHint`, `Mod.DescriptionHint`, `Mod.DescriptionUrlHint`, `Restrict.StatementHint`, `OrgMod.LookAtWebsite` (AU) | action forms | | keep (field hints for doing it right) | |
| T8 | `Admin.Lead`, `Admin.GroupNote.*`, `Admin.Help.*`, `Admin.MinimumPlaceholder` (AU) | `Settings.razor` | | keep (per-setting help). See finding F5: `Admin.Help.Follows:MaxPerAccount` is out of date | |
| T9 | `Inv.Lead`, `Inv.LevelsNote`, `Fb.Lead`, `Hidden.Lead` (AU) | `Invitations.razor`, `FeedbackStaff.razor`, `HiddenItems.razor` | | keep (short; legends) | |

---

## 17. The methodology page and the moderation rules

`/metodika` (`Parliament/Methodology.razor`) is the destination; it is long by nature and stays so. What the moves
above need there, and what is wrong there now:

| ID | What | Exists? | Proposal |
|---|---|---|---|
| M1 | „Planuojamos datos“ (from B1, G2) | No (only `/moderavimo-taisykles#Dates` for moderators) | new section, `id="planuojamos-datos"`, after „Kaip atpažįstamos projekto stadijos“. Heading [juodraštis]: **Planuojamos datos**. Text [juodraštis]: „Planuojamas datas imame iš Seimo darbotvarkių projektų, kuriuos lrs.lt skelbia maždaug 10–12 dienų į priekį. Jie dažnai keičiasi, todėl data gali pasislinkti. Jei data neteisinga, projekto dalyviai gali apie tai pranešti, o moderatorius ją patikrina ir pataiso.“ |
| M2 | „Kas matoma viešai, o kas ne“ (from B14, and the place the privacy lines of B20, B34, O19, I2, A8 point to) | No: privacy of sides is only in `Rules.Privacy.1` (about moderators) and in the consent box | new section, `id="privatumas"`, before „Argumentai“. Heading [juodraštis]: **Kas matoma viešai, o kas ne**. Text [juodraštis]: „Ko prašote dėl projekto – balsuoti už ar prieš – niekam nerodome: nei kitiems naudotojams, nei moderatoriams, nei administratoriams. Viešai skelbiami tik bendri skaičiai. Savo pasirinkimą galite keisti ar atsiimti, kol Seimas dėl projekto nebalsavo galutinai. Ką sekate, mato tik jūs. Kvietimo kodo savininkas mato tik, kiek žmonių juo užsiregistravo, – ne kas ir ne kada.“ |
| M3 | Organisations: representatives' own side, organisation codes (from B34, O19, I2) | Partly: `Method.Organisations.1` says representatives' names are not public | two sentences added to „Organizacijų pozicijos“ after `Method.Organisations.1` [juodraštis]: „Tai, ko atstovas prašo pats, su organizacija niekaip nesiejama. Organizacijos kvietimo kodu užsiregistravusių paskyrų su organizacija niekas nesieja: atstovai mato tik jų skaičių.“ |
| M4 | Summaries: who would receive them; the public copy (from D3, D5) | No | two rows in `#digest-rules` [juodraštis]: „Kas gautų“ – „visi Seimo nariai, kurių oficialus adresas yra atviruosiuose duomenyse, vienodai“ (the existing `Digests.Who`/`WhoValue` can serve); „Vieša kopija“ – „kiekviena suvestinė skelbiama puslapyje „Suvestinės“ su tais pačiais skaičiais, tik be skaičių, skirtų konkrečiam Seimo nariui“ |
| M5 | Anchor for „Kas yra riba?“ (B18) | The row exists (`Method.Digests.Threshold`) | `id="riba"` on that `<dt>` |
| M6 | Heading of „Ribos vienam žmogui“ | **Broken**: `A["Method.Limits"]` has no resource; the page shows the literal "Method.Limits" (F1) | add the key [juodraštis]: **Ribos vienam žmogui** |
| M7 | `Method.Lead` | Out of date: "Seimo narių profiliai sudaromi automatiškai. Čia aprašyta … ką tiksliai skaičiuoja du profilio rodikliai." The page now also explains summaries, arguments, descriptions, organisations and limits | rewrite [juodraštis]: „Čia aprašyta, iš kur imami duomenys, kaip skaičiuojami Seimo narių rodikliai ir kaip veikia dalyvavimas dėl projektų: suvestinės, argumentai, aprašymai ir organizacijos.“ |
| M8 | `Method.Limits.Note` | Uses the retired term: "kol kreipimasis atviras" | wording [juodraštis]: "… kol dalyvauti dėl projekto galima." |

`/moderavimo-taisykles`: no move goes there except T5's guidance, which `Rules.Reports.1` would need a sentence for if
T5 were shortened later. `Rules.Appeals.1` says "Projekto aprašymą rašo moderatorius", which is out of date since AI
summaries (8.13); not an inline-noise issue, noted for the owner.

---

## 18. Texts that say the same thing on several screens

| Topic | Keys (file) | Proposal |
|---|---|---|
| Organisations' statements are not counted | `Org.Section.NotCounted`, `Org.Profile.NotCounted`, `Org.List.Lead` (3rd sentence), `Org.Form.Lead` (3rd), `Org.Form.GoalHint` (2nd) (AU); `Method.Organisations.2` (AT) | one sentence, one key, on the bill section, the profile and the list; dropped from the form |
| Your side is private | `Side.SignIn`, `Side.Private`, `Side.Yours.*`, `Consent.Text`, `Reasons.EitherSide`, `Share.Lead`, `Org.Register.Point2`, `Org.Mine.Private`, `Org.Invite.What` (AU); `DisplayName.Hint`, `PositionConsent.Text` (UT); `Rules.Privacy.1`, `Method.Reasons.1` (AT) | short inline where acting; full statement in the new M2 |
| Representatives' names are private | `Org.Register.Point2`, `Org.Mine.Private`, `Org.Manage.Lead`, `Org.Reps.Private`, `Org.Invite.What` (AU); `Method.Organisations.1`, `Rules.Organisations.4` (AT) | delete `Org.Reps.Private`; the others shortened |
| Following is private / what it sends | `Follow.Following.bill/.mp/.organisation`, `Follow.What.*`, `Follow.SignIn.*`, `Follows.Lead`, `Follows.Email.*` (AU) | one "Sekate. To niekas kitas nemato." for all kinds; `Follows.Lead` reduced to privacy |
| Planned dates move | `Bill.PlannedMoves` (AT), `Agenda.Source` (PT), `Rules.Dates.1` (AT) | M1 |
| What the threshold is | `Threshold.Explained`, `Threshold.SetBy.default`, `Digests.OnAppeal.None` (2nd sentence), `Digests.WhatValue`, `Method.Digests.ThresholdValue` (AT); `Admin.Help.Digest:ThresholdDefault` (AU) | one link „Kas yra riba?“ to M5 |
| Older-than-30-days counts | `Sides.Older`, `List.Counts`, `Digests.OnAppeal.Frozen` (AT); `Home.Older`, `Agenda.OlderNote` (PT) | one bracket form plus one legend (P2) |
| Summaries are not sent | `Digests.DryRun`, `Method.Digests.NotSent` (AT); `Admin.GroupNote.summaries` (AU) | keep (each on a different screen; short) |
| Closed trial, invitation only | `Home.Trial`, `Footer.Line` (PT); `Invitation.Hint` (UT); `Admin.Help.Registration:RequireInvitation` (AU) | keep; S4 shortened |
| Invitation count shows no names | `MyInvitation.Private`, `Invitation.Hint` (2nd) (UT); `Org.Code.Private`, `Inv.Lead` (AU) | shortened; M2 |
| Label meanings | `Org.Profile.Meaning.*`, `Org.Register.Point3`, `Org.Confirm.Lead`, `Org.Confirm.NotStored` (AU); `Method.Organisations.Means.*` (AT) | profile: under a link (O1) |
| Nothing is sold | `Org.Register.Point4` (AU); `Method.Organisations.4` (AT) | methodology only (R5) |
| Statement history is public | `Org.Form.Lead`, `Org.Profile.StatementsLead`, `Org.Form.WithdrawConfirm`, `Org.Edit.Saved` (AU) | delete `Org.Profile.StatementsLead` |
| Profiles are automatic | `Mps.Lead`, `Mp.Auto`, `Method.Lead` (PA) | delete `Mp.Auto` |
| What a passkey is | `Passkeys.Text`, `SignIn.PasskeyHint` (UT) | S3 shortened |
| Same sentence, two keys | `Org.Section.Frozen` / `Org.Profile.Frozen` (AU); `Reasons.None` (AU) / `Home.NoReasons` (PT) / `Digests.NoReasons` (AT); `Reasons.Anonymous` (AU) / `Anonymous` (AT); `Hidden.Lead` exists in both AT (bill page) and AU (hidden list) with different texts | fine as they are (different resource classes); the two `Hidden.Lead` keys are a naming trap worth renaming one day |

---

## 19. Resource keys no component uses

Searched every `.razor` and `.cs` under `src/` and `tests/` for the quoted key, then for every key built at run time
(`L["Prefix." + x]`, `$"Prefix.{x}"`, ternaries, `GoalKey(...) + "Only"`, `LabelMeaning(...)`, `ActionLabel(...)`,
the `Sections` list of the moderation rules, the settings' `Name/Help/Unit`).

**Unused (certain):**

| File | Keys |
|---|---|
| AT | `Stages.Heading` ("Eiga Seime"), `List.Threshold` ("{0} dalyvių · riba {1}") |
| AU | `Edit.Title`, `Edit.TitleReturned`, `Edit.Submit` (the retired proposal edit), `Threshold.Change`, `Threshold.Label`, `Threshold.Save`, `Mod.Statement`, `Mod.Publish`, `Mod.Keep`, `Mod.Hide`, `Mod.SetThreshold`, `Mod.Merge`, `Mod.SetPlanned`, `Mod.Hold`, `Mod.Release`, `Mod.Urgent` (the older button labels; `Mod.Action.*` replaced them), `Admin.Default`, `Admin.Saved`, `Admin.Invalid` (replaced by `Admin.DefaultIs`, `Admin.SavedOne`), `Mod.Status.open`, `Mod.Status.published` (only `Mod.Status.pending` is read, `Moderation.razor:106`, `AppealModerationPanel.razor:32`, `StatementQueueItem.razor:12`) |
| PA | `Vote.Bill` ("Projektas") |

**Used only at run time (not dead; listed because a plain search misses them):** `Follow.What.*`, `Follow.Following.*`
(`FollowButton.razor:16`), `Mp.Label.V/M`, `Mp.Former.V/M` (`MpProfile.razor:223`), `Sessions.Method.*`
(`Hello.razor:67`), `Kind.*` (FT, `FeedbackForm.razor:31`), `Hidden.RestoreAppeal.Sentence.*` (`RestoreAction.razor:54`),
`OrgMod.Sentence.*`, `OrgMod.Action.*`, `OrgMod.Done.*` (`OrgModerationActions.razor`), `Mod.Action.*`, `Mod.Sentence.*`,
`Mod.Done.*` (`ModerationActions.razor`), `Org.leave.*`, `Org.close.*` (`OrganisationManage.razor:110`),
`Org.Profile.Meaning.*` (`OrganisationPage.razor` `LabelMeaning`), `Org.Goal.*Only` (`OrganisationWords.GoalKey`),
`Rules.*` (the `Sections` array), `Admin.Setting/Help/Unit/Group/GroupNote.*` (`Settings.razor`), `Position.*`,
`Body.*`, `Choice.*`, `Stage.*`, `VoteKind.*`, `Month.*` (`Formats.cs`), `Digests.Kind/Status/Side.*`, `Report.Ground.*`,
`Flag.*`, `Reasons.for/against`, `Fb.*`, `Inv.Kind.*`, `Level.*`, `Role.*`.

**Uncertain:** the run-time families above were matched by prefix, not value by value; a value the server never sends
(for example a `Digests.Status.*` or `Org.State.*` for a state that no longer exists) would look used. Checking those
needs the enumerations in the contracts, which this audit did not do.

**Missing (the opposite):** `Method.Limits` (F1).

---

## 20. Other findings (not text length, seen on the way)

- **F1** `/metodika`: the heading of the „Ribos vienam žmogui“ section (`Methodology.razor:122`, `A["Method.Limits"]`)
  has no resource, so the page shows the literal "Method.Limits" when the limits section renders. Fix: M6.
- **F2** `OrganisationPage.razor:81` uses the form label `Org.Field.LegalCode` „Juridinio asmens kodas (nebūtina)“ as
  the profile's fact label, so the public profile reads "(nebūtina)". Needs its own key (e.g. `Org.Profile.LegalCode`
  „Juridinio asmens kodas“). `Org.Field.Website` is shared the same way but has no "(nebūtina)", so it is fine.
- **F3** The retired term *kreipimasis* is still shown: `Digests.Items` „kreipimųsi: {0}“ (`DigestPage.razor:38`,
  `:118`) and `Method.Limits.Note` (M8). Proposed: „projektų: {0}“ [juodraštis].
- **F4** `Method.Lead` describes only MP profiles (M7); the menu calls the page „Kaip skaičiuojama“, which fits less
  now that it explains organisations and descriptions. Not proposed here; a naming question for the owner.
- **F5** `Admin.Help.Follows:MaxPerAccount` says "projektų ir Seimo narių kartu"; organisations are followed too and
  count towards the same limit (`Follows.Max` counts all three).
- **F6** `Welcome.Lead` „… ir sužinokite, ką jie atsakė“ promises MP responses, an M2b feature not built.
- **F7** `Rules.Appeals.1` „Projekto aprašymą rašo moderatorius“ is out of date since AI summaries (design 8.13).
