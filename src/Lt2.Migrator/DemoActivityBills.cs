using Lt2.Appeals;
using Lt2.SharedKernel;

namespace Lt2.Migrator;

/// <summary>
/// What <c>--demo-activity</c> writes on the trial's real Seimas bills (design 8.26.5): invented members' sides, arguments
/// and comments, and invented organisations' statements. The bills are real (chosen from the trial's agenda of 2026-12-01
/// and 2026-12-03); everything said about them here is invented and is not anyone's opinion. A bill that is not in the data
/// or is already finished is skipped, so the list may outlive the bills.
/// </summary>
public static class DemoActivityBills
{
    /// <summary>
    /// Bills never touched: XVP-1322 (another piece of work is describing it, owner 2026-11-26), and decisions about named
    /// people (a judge's dismissal, an appointment), where invented members' sides would read as opinions about a person.
    /// </summary>
    public static readonly IReadOnlySet<string> Excluded = new HashSet<string>(StringComparer.Ordinal) { "XVP-1322", "XVP-2023", "XVP-2052" };

    /// <summary>An invented argument: its side, text, the share of its side who mark it "Man svarbu", and its author's display name.</summary>
    public sealed record Argument(string Side, string Text, double Marks, string Author);

    /// <summary>An invented comment under the bill's argument at <paramref name="Under"/>, by a named invented member.</summary>
    public sealed record Comment(int Under, string Author, string Text, int Agreements);

    /// <summary>One real bill: how many invented members take a side, the share asking to vote for, and the arguments and comments.</summary>
    public sealed record Bill(string Number, int Positions, double ForShare, IReadOnlyList<Argument> Arguments, IReadOnlyList<Comment>? Comments = null);

    private static Argument For(string text, double marks, string author) => new(Sides.For, text, marks, author);

    private static Argument Against(string text, double marks, string author) => new(Sides.Against, text, marks, author);

    public static readonly IReadOnlyList<Bill> Open =
    [
        new("XVP-1980", 2480, 0.46,
        [
            For("Trejų metų biudžetas leidžia iš anksto planuoti gynybos ir sveikatos apsaugos išlaidas, o ne kasmet pradėti iš naujo.", 0.34, "Giedrė L."),
            For("Didesnės išlaidos gynybai šiuo metu būtinos, net jei dėl to teks skolintis.", 0.22, "Mindaugas R."),
            Against("Asignavimai pajamas viršija beveik 6 mlrd. eurų. Tokį deficitą teks dengti skola, kurią grąžins kitos kartos.", 0.41, "Aistė Kazlaitė"),
            Against("Prieš didinant išlaidas reikėtų peržiūrėti, kurios programos nepasiekia savo tikslų.", 0.18, "Paulius D."),
        ],
        [
            new(2, "Laura P.", "Ar yra skaičiavimas, kiek kainuotų skolos palūkanos 2029 metais? Būtų lengviau vertinti.", 41),
            new(2, "Tomas V.", "Aiškinamajame rašte yra lentelė apie valdžios sektoriaus skolą, verta pažiūrėti prieš balsuojant.", 18),
        ]),
        new("XVP-1450", 1640, 0.78,
        [
            For("Kai kainos paskelbiamos iš karto, vairuotojai gali palyginti degalines ir rinktis pigesnę.", 0.44, "Andrius K."),
            For("Agentūra matytų, ar kainos keičiamos vienu metu visose degalinėse.", 0.19, "Jurgita M."),
            Against("Pranešti per 10 minučių po kiekvieno kainos pakeitimo – nemaža našta mažai degalinei kaimo vietovėje.", 0.37, "Vytautas S."),
        ]),
        new("XVP-1445", 1920, 0.84,
        [
            For("Kilpos ir galūnes suspaudžiantys spąstai kankina gyvūnus ir dažnai sugauna ne tuos, kuriems buvo skirti, net šunis ir kates.", 0.52, "Eglė S."),
            For("Medžiotojui pakaktų elektroninio bilieto – mažiau popierių, tas pats patikrinimas.", 0.12, "Darius B."),
            Against("Draudimas laikyti spąstus paliestų ir tuos, kurie jais gaudo kenkėjus savo sodyboje.", 0.29, "Kęstutis J."),
        ],
        [
            new(2, "Rasa G.", "Pelėms ir žiurkėms skirtiems spąstams draudimas, regis, netaikomas: projektas kalba apie medžiojamuosius gyvūnus.", 27),
        ]),
        new("XVP-1446", 610, 0.79,
        [
            For("Be baudos draudimas laikyti kilpas liktų tik popieriuje.", 0.4, "Eglė S."),
        ]),
        new("XVP-1521", 1350, 0.71,
        [
            For("Nedidelis sodo namas nėra daugiabutis; leidimo procedūra užtrunka mėnesius ir kainuoja brangiai.", 0.46, "Rimas T."),
            Against("Be leidimo sodų bendrijose gali atsirasti namų, kurie užstos kaimynams saulę ir nesilaikys atstumų nuo sklypo ribos.", 0.33, "Inga V."),
        ],
        [
            new(1, "Rimas T.", "Atstumų nuo sklypo ribos reikalavimai lieka galioti ir be leidimo – keičiasi tik dokumentų tvarka.", 22),
        ]),
        new("XVP-1866", 540, 0.66, []),
        new("XVP-1447", 870, 0.41,
        [
            For("Šauliai ir kariai savanoriai neturėtų mokėti už ginkluotos apsaugos sutartį vien tam, kad galėtų laikyti tarnybai reikalingą ginklą namuose.", 0.38, "Marius Ž."),
            Against("Ginklai su didelės talpos dėtuvėmis be signalizacijos ir apsaugos – didesnė rizika, kad jie bus pavogti.", 0.43, "Lina A."),
        ]),
        new("XVP-177", 2210, 0.52,
        [
            For("Gydymas ir kursai padeda labiau nei teistumas, kuris jaunam žmogui ilgam uždaro kelią į darbą.", 0.39, "Simona R."),
            For("Policija ir teismai galėtų daugiau laiko skirti prekeiviams, o ne vartotojams.", 0.24, "Justas N."),
            Against("Tokia išimtis gali būti suprasta kaip ženklas, kad nedidelis narkotikų kiekis nėra pavojingas.", 0.42, "Ona K."),
            Against("Neaišku, kaip bus tikrinama, ar žmogus iš tikrųjų lanko kursus ar gydosi.", 0.21, "Arūnas P."),
        ],
        [
            new(3, "Simona R.", "Pagal projektą asmuo turėtų pats kreiptis į sveikatos priežiūros įstaigą – tai būtų matyti iš jos dokumentų.", 15),
        ]),
        new("XVP-1673", 760, 0.69,
        [
            For("Galimybė atsinešti savo indelį kavai ar maistui išsinešti sumažintų vienkartinių pakuočių.", 0.45, "Gabija M."),
            Against("Mažoms kavinėms teks keisti įrangą ir tvarką, o išlaidos atsidurs kainose.", 0.31, "Edvinas L."),
        ]),
        new("XVP-1675", 290, 0.62, []),
        new("XVP-1788", 1180, 0.81,
        [
            For("Vietinis maistas mokyklose šviežesnis, o pinigai lieka regiono ūkininkams.", 0.5, "Dalia Š."),
            Against("Savivaldybėms skiriama nauja funkcija, bet neaišku, iš kokių lėšų bus samdomi atsakingi darbuotojai.", 0.35, "Gintaras U."),
        ]),
        new("XVP-1787", 420, 0.76, []),
        new("XVP-1779", 990, 0.88,
        [
            For("Žmonės, išėję į išankstinę pensiją dėl ligos ar darbo netekimo, neturėtų už tai mokėti visą likusį gyvenimą.", 0.55, "Birutė A."),
        ]),
        new("XIVP-2536", 330, 0.57, []),
        new("XVP-1224", 1460, 0.23,
        [
            For("Teisininko pagalba padėtų Seimo nariams rengti tikslesnius įstatymų projektus.", 0.36, "Karolis E."),
            Against("Seimas jau turi Teisės departamentą, kurio išvadas gauna visi Seimo nariai; geriau stiprinti jį.", 0.47, "Neringa F."),
        ]),
        new("XVP-1404", 380, 0.26, []),
        new("XVP-1671", 450, 0.22,
        [
            For("Draudimas tiekti tokias prekes rinkai lieka, todėl Lietuvos vaikai jų parduotuvėse nepamatys.", 0.27, "Tadas O."),
            Against("Saldainiai ir žaislai, primenantys cigaretes, vaikams rūkymą paverčia įprastu dalyku.", 0.51, "Jolanta I."),
        ]),
        new("XVP-1886", 520, 0.74,
        [
            For("Tyrimuose dalyvaujantys žmonės turėtų gauti žalos atlyginimą taip pat greitai kaip pacientai, be ilgų ginčų dėl kaltės.", 0.42, "Milda C."),
        ]),
        new("XVP-1981", 640, 0.59,
        [
            For("Teismo leidimas laikinai apriboti veiklą apsaugo sąžiningus mokesčių mokėtojus nuo nesąžiningos konkurencijos.", 0.33, "Vaidas H."),
            Against("180 dienų veiklos apribojimas gali sužlugdyti įmonę dar prieš įrodant pažeidimą.", 0.38, "Agnė W."),
        ]),
        new("XVP-1253", 710, 0.83,
        [
            For("Saugumo aplinka nuo 2021 m. labai pasikeitė, todėl strategiją būtina atnaujinti.", 0.48, "Mindaugas R."),
        ]),
        new("XVP-1789", 560, 0.76,
        [
            For("Žvalgyba turi turėti aiškų teisinį pagrindą veikti prieš grėsmes, kol jos dar nepasiekė Lietuvos.", 0.41, "Laimonas Z."),
            Against("Tokioms operacijoms reikia aiškios parlamentinės priežiūros; norėtųsi, kad ji būtų įrašyta tame pačiame įstatyme.", 0.37, "Rūta Mockutė"),
        ]),
        new("XVP-781", 230, 0.55,
        [
            For("Automatiškai suformuoti protokolai sutaupytų pareigūnų laiko ir popieriaus.", 0.3, "Žilvinas Y."),
            Against("Jei protokolas suformuojamas automatiškai ir niekieno nepasirašomas, neaišku, kas atsako už klaidą.", 0.4, "Rasa G."),
        ]),
        new("XVP-1569", 310, 0.45, []),
        new("XVP-1611", 430, 0.58, []),
        new("XVP-1563", 180, 0.64, []),
        new("XVP-1950", 260, 0.61, []),
        new("XVP-1666", 210, 0.67, []),
        new("XVP-1605", 120, 0.6, []),
        new("XVP-1865", 140, 0.8, []),
        new("XVP-1108", 95, 0.7, []),
        new("XVP-1891", 85, 0.56, []),
        new("XVP-1908", 70, 0.44, []),
        new("XVP-783", 64, 0.52, []),
        new("XVP-1982", 38, 0.6, []),
        new("XVP-1858", 22, 0.68, []),
        new("XVP-1887", 14, 0.71, []),
    ];

    /// <summary>
    /// Three invented organisations (design 8.14.14), each with a statement on one of the bills above: names that say they are
    /// examples, websites on the reserved <c>pavyzdys.test</c> domain. One confirmed by its domain, one unconfirmed, one
    /// confirmed by a moderator.
    /// </summary>
    public static IReadOnlyList<InventedOrganisationSpec> Organisations(Func<int, Guid> representative) =>
    [
        new(
            "pavyzdine-gyvunu-globos-draugija",
            "Pavyzdinė gyvūnų globos draugija",
            "Išgalvota organizacija bandomiesiems duomenims: rūpinasi beglobiais ir laukiniais gyvūnais.",
            "https://gyvunai.pavyzdys.test",
            "domain",
            representative(1),
            new InventedStatement(
                "XVP-1445",
                StatementStances.For,
                "Prašome balsuoti už. Kilpos ir galūnes suspaudžiantys spąstai sužaloja gyvūnus, o į juos patenka ir saugomų rūšių gyvūnai bei augintiniai.\nSiūlome kartu numatyti, kaip bus surenkami jau turimi spąstai.",
                "https://gyvunai.pavyzdys.test/pozicija",
                2500)),
        new(
            "pavyzdinis-sodininku-bendriju-susivienijimas",
            "Pavyzdinis sodininkų bendrijų susivienijimas",
            "Išgalvota organizacija bandomiesiems duomenims: sodininkų bendrijų pirmininkai ir nariai.",
            "https://sodai.pavyzdys.test",
            null,
            representative(2),
            new InventedStatement(
                "XVP-1521",
                StatementStances.For,
                "Prašome balsuoti už. Daugelis sodo namų jau seniai stovi, o leidimų tvarka nedidelėms statyboms per sudėtinga.",
                null,
                null)),
        new(
            "pavyzdine-smulkiojo-verslo-mokesciu-taryba",
            "Pavyzdinė smulkiojo verslo mokesčių taryba",
            "Išgalvota organizacija bandomiesiems duomenims: smulkiųjų įmonių buhalteriai ir savininkai.",
            null,
            "moderator",
            representative(3),
            new InventedStatement(
                "XVP-1981",
                StatementStances.Remarks,
                "Pastabos: prašome įstatyme aiškiai nurodyti, kokius duomenis mokesčių administratorius turi pateikti teismui, ir numatyti, kaip įmonė gali greitai užginčyti apribojimą.",
                "https://mokesciu-taryba.pavyzdys.test/pastabos",
                null)),
    ];
}
