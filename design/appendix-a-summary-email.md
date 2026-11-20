# Appendix A. Sample summary email

The text part of a weekly summary to an MP's official address, as `MpMailTemplates` renders it from
`Templates/EmailTemplates.resx`. All names and numbers are fictional. The first bill's MP has answered; the second's
line would read "Atsakyti vieną kartą visiems: …" instead. Each bill's address appears once in the text part: the response's
"Keisti arba papildyti" or "Atsakyti vieną kartą visiems", never also "Visi skaičiai ir argumentai" (kept only for a copy
without either). The HTML part (8.19.3) shows the same content as a card per bill, with link labels instead of
addresses: "Atsakyti visiems" (button), "Keisti arba papildyti", "Skaityti visą" (for "Visas argumentas: …"), "Ši suvestinė viešai"
(for "Ta pati suvestinė viešai: …"), and the bill's heading as the link to its page. A side without published arguments reads "Argumentų už dar
nėra." (or "prieš"); a side with one or two published arguments reads "Dažniausi argumentai už:" with no "(iš N)", since the
two shown are all of them; a bill with a surge note adds "Per paskutines {0} valandas {1} % pozicijų pateikė paskyros, sukurtos
per tą patį laiką. Skaičius „senesnės nei 30 d.“ jų neapima." under the counts. An argument longer than about 300
characters is cut with "…" and followed by "Visas argumentas: {link}" to it on the public copy. After the arguments, the
organisations' line (8.14.4) has one sentence per stance with organisations named ("Organizacijos, prašančios balsuoti
už: …", "… prieš: …", "Pastabų atsiuntė: …"), each name's address to its statement on its own line under it; in the HTML
part the names are the links; a bill with no organisation named has no line.

```
Tema: Lietuva 2.0 suvestinė Seimo nariams, 2026-09-21

Laba diena,

projektų šioje suvestinėje: 2. Dėl jų į Seimo narius kreipiasi Lietuva 2.0 naudotojai.

XVP-0412 · Gyventojų pajamų mokesčio įstatymo pataisa
Priėmimas · planuojama rugsėjo 24 d.
  Prašo balsuoti už: 6 800 (iš jų 6 150 – paskyros senesnės nei 30 d.)
  Prašo balsuoti prieš: 4 280 (iš jų 3 900 – paskyros senesnės nei 30 d.)
  Iš nurodžiusių, kad palaiko Reformų partija: 410 prašo balsuoti už.
  Iš nurodžiusių, kad palaiko Reformų partija: 520 prašo balsuoti prieš.
  Dažniausi argumentai už (iš 14):
  · Didžiausias pajamas gaunantys moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai. (1 920)
  · Gynyba finansuojama nekeliant PVM. (1 310)
  Dažniausi argumentai prieš (iš 9):
  · Akcijų pardavimo įskaičiavimas paliečia vienkartinius įvykius, pvz., šeimos verslo pardavimą. (1 480)
  · Skatina įmonių steigėjus keisti mokestinę rezidavimo vietą. (970)
  Organizacijos, prašančios balsuoti už: Pavyzdinė mokesčių aiškumo asociacija.
    Pavyzdinė mokesčių aiškumo asociacija: https://lietuva2.lt/seimas/XVP-0412#statement-0193a1b2-0000-7000-8000-0000000000d1
  Jūsų atsakymas: balsuosiu prieš (2026-09-18). Keisti arba papildyti: https://lietuva2.lt/seimas/XVP-0412

XVP-1811 · …

Tai nėra reprezentatyvi apklausa: skaičiai rodo savanoriškai dalyvaujančių Lietuva 2.0 naudotojų nuomonę.
Ta pati suvestinė viešai: https://lietuva2.lt/suvestines/2026-09-21

Lietuva 2.0

Keisti suvestinių dažnumą arba jų atsisakyti: https://lietuva2.lt/suvestines/nustatymai/…
Prisijungę prie Lietuva 2.0 šiuo adresu galite perimti savo Seimo nario profilį ir atsakyti visiems iš karto: https://lietuva2.lt/prisijungti
```
