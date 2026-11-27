namespace Lt2.Web.Client.Components;

/// <summary>
/// Where each MP sits in the plenary hall, from the Seimas's seating plan "Seimo narių vietos Seimo posėdžių salėje" of
/// <see cref="Date"/> (a one-page PDF with photos and names, linked from https://www.lrs.lt/sip/portal.show?p_r=15278&amp;p_k=1),
/// copied by hand once (design 7.3). The open data has no seats. When the Seimas publishes a new plan (MPs join, leave or
/// change faction), this file is copied again by hand and <see cref="Date"/> changed.
/// </summary>
/// <remarks>
/// Sectors 1 to 4 left to right as the plan is drawn, the rostrum below; rows from the front (1) to the back (6); in a row,
/// the seats left to right. Each seat is the MP's address name (<c>/p/{slug}</c>); "" is a seat the plan leaves free.
/// </remarks>
public static class SeatingPlan
{
    /// <summary>The plan's date, as printed on it.</summary>
    public const string Date = "2026-10-14";

    public static readonly IReadOnlyList<(int Sector, int Row, IReadOnlyList<string> Seats)> Rows =
    [
        (1, 6, ["mindaugas-linge", "ingrida-simonyte", "radvile-morkunaite-mikuleniene", "laurynas-kasciunas", "zygimantas-pavilionis", "dalia-asanaviciute-gruzauskiene", "gintare-skaiste", "aiste-gedviliene", "virgilijus-alekna"]),
        (1, 5, ["paule-kuzmickiene", "vytautas-kernagis", "dainius-kreivys", "emanuelis-zingeris", "jurgita-sejoniene", "arvydas-pocius", "arminas-lydeka", "viktorija-cmilyte-nielsen"]),
        (1, 4, ["valdas-rakutis", "arunas-valinskas", "matas-maldeikis", "kestutis-masiulis", "arvydas-anusauskas", "simonas-kairys", "ricardas-juska"]),
        (1, 3, ["jurgis-razma", "liutauras-kazlavickas", "vytautas-juozapaitis", "raimondas-kuodis", "simonas-gentvilas", ""]),
        (1, 2, ["angele-jakavonyte", "audronius-azubalis", "giedre-balcytyte", "andrius-bagdonas", "edita-rudeliene"]),
        (1, 1, ["audrius-petrosius", "daiva-ulbinaite", "vitalijus-gailius", "eugenijus-gentvilas"]),
        (2, 6, ["lina-sukyte-korsake", "saulius-bucevicius", "remigijus-zemaitaitis", "robert-puchovic", "arturas-skardzius", "aidas-gedvilas", "bronis-rope", "dainius-gaizauskas"]),
        (2, 5, ["vytautas-jucius", "daiva-zebeliene", "dainoras-bradauskas", "mantas-poskus", "karolis-neimantas", "valius-azuolas", "eimantas-kirkutis"]),
        (2, 4, ["raimondas-sukys", "kestutis-bilius", "daiva-petkeviciene", "tomas-domarkas", "ligita-girskiene", "ausrine-norkiene"]),
        (2, 3, ["martynas-gedvilas", "tadas-sadauskis", "ceslav-olsevski", "jaroslaw-narkiewicz", "rita-tamasuniene"]),
        (2, 2, ["petras-dargis", "rimas-jonas-jankunas", "ignas-vegele", "linas-urmanavicius"]),
        (2, 1, ["viktoras-pranckietis", ""]),
        (3, 6, ["saulius-skvernelis", "agne-sirinskiene", "domas-griskevicius", "lukas-savickas", "dainius-varnas", "sarunas-birutis", "vytautas-grubliauskas", "inga-ruginiene"]),
        (3, 5, ["algirdas-butkevicius", "rima-baskiene", "linas-kukuraitis", "tomas-tomilinas", "jekaterina-rojaka", "andrius-busila", "modesta-petrauskaite"]),
        (3, 4, ["zigmantas-balcytis", "ruta-miliute", "agne-jakaviciute-miliauskiene", "kestutis-mazeika", "laura-asadauskaite", "juozas-olekas"]),
        (3, 3, ["viktoras-fiodorovas", "vitalijus-sersniovas", "vytautas-sinica", "martynas-katelynas", "jurate-zailskiene"]),
        (3, 2, ["arturas-zuokas", "vilija-targamadze", "darius-jakavicius", "tadas-prajara"]),
        (3, 1, ["tadas-barauskas", "ingrida-braziuliene"]),
        (4, 6, ["gintautas-paluckas", "eugenijus-sabutis", "raminta-popoviene", "rimantas-sinkevicius", "birute-vesaite", "indre-kiziene", "orinta-leipute", "algirdas-sysas", "julius-sabatauskas"]),
        (4, 5, ["kestutis-vilkauskas", "laurynas-sedvydis", "saulius-caplinskas", "ramunas-vyzintas", "robertas-kaunas", "linas-balsys", "remigijus-motuzas", "rasa-budbergyte"]),
        (4, 4, ["saulius-luscikas", "jurgita-sukeviciene", "antanas-nedzinskas", "alvydas-mockus", "algimantas-radvila", "ruslanas-baranovas", "dovile-sakaliene"]),
        (4, 3, ["audrius-radvilavicius", "arunas-dudenas", "paulius-visockas", "ilona-gelaznikiene", "linas-jonauskas", "darius-razmislevicius"]),
        (4, 2, ["tomas-martinaitis", "roma-janusoniene", "giedrius-drukteinis", "violeta-turauskaite", "matas-skamarakas"]),
        (4, 1, ["lilija-vaitiekuniene", "sarunas-sukevicius", "vaida-aleknaviciene", "karolis-podolskis"]),    ];
}
