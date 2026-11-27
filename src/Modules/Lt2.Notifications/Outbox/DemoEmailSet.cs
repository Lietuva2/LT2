using System.Text.Json;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;

namespace Lt2.Notifications.Outbox;

/// <summary>
/// One demonstration email: a short name (the local part of its address, so Mailpit lists them by name) and the outbox message
/// written exactly as the code that really sends this kind writes it.
/// </summary>
internal sealed record DemoEmail(string Name, OutboxMessage Message)
{
    /// <summary>The invented address it goes to: <c>NAME@pavyzdys.test</c>.</summary>
    public string Address => Name + "@" + DemoEmailSet.Domain;
}

/// <summary>
/// The set behind <c>--demo-emails</c> (<see cref="DemoEmails"/>): one message of every outbox kind in <see cref="EmailStreams"/>, and of every
/// variant a kind has (every account notice, every moderation outcome), each with invented data (bill XVP-0412, the MPs Jonas
/// Petraitis and Ona Jonaitienė, the expert Rūta Mockutė, the organisation "Pavyzdinė mokesčių aiškumo asociacija") and
/// addressed to <c>NAME@pavyzdys.test</c>, so Mailpit lists them by name and the worker renders them through the real renderers.
/// A unit test fails until every kind in the table has a demonstration.
/// </summary>
internal static class DemoEmailSet
{
    /// <summary>The domain every demonstration address is under (never a real one).</summary>
    public const string Domain = "pavyzdys.test";

    private const string Bill = "XVP-0412";
    private const string BillTitle = "Gyventojų pajamų mokesčio įstatymo pataisa";
    private const string BillPath = "/seimas/" + Bill;
    private const string OtherBill = "XVP-0413";
    private const string OtherBillTitle = "Akcizų įstatymo pataisa";
    private const string OtherBillPath = "/seimas/" + OtherBill;
    private const string Organisation = "Pavyzdinė mokesčių aiškumo asociacija";
    private const string Mp = "Jonas Petraitis";
    private const string OfficialMp = "Ona Jonaitienė";
    private const string Reason = "Pasiūlymas keičia mokesčių tarifus visiems vienodai, todėl jis teisingesnis dirbantiems žmonėms, o ne tik didesnes pajamas gaunantiems.";
    private const string Statement = "Argumentas pažeidžia taisykles: jame kalbama apie asmenį, o ne apie projektą.";
    private const string OrganisationStatement = "Organizacijos pavadinimas klaidina: ji nėra susijusi su nurodyta svetaine.";
    private const string OrganisationPath = "/organizacijos/pavyzdine-mokesciu-aiskumo-asociacija";
    private const string Contact = "moderatoriai@pavyzdys.test";

    /// <summary>A stop-link token of the length and alphabet real ones have; invented.</summary>
    private const string Token = "kQ3vXz9LmN2pRtYb7WcHdJf5GsAe8UoIx1VnBqZk4MyTlPa6CwDrEgSh0FjOiKu";

    /// <summary>A code of the shape of a one-time link's code; invented.</summary>
    private const string LinkCode = "K7QM-4TXD-9HBR";

    /// <summary>The kinds one demonstration covers: every kind of the stream table.</summary>
    internal static IReadOnlyCollection<string> CoveredKinds(IEnumerable<DemoEmail> emails) =>
        [.. emails.Select(e => e.Message.Kind).Distinct(StringComparer.Ordinal)];

    /// <summary>Every demonstration email, written for <paramref name="now"/>.</summary>
    internal static IReadOnlyList<DemoEmail> Build(DateTimeOffset now)
    {
        var today = VilniusTime.DateOf(now);
        var list = new List<DemoEmail>();
        void Add<T>(string kind, string name, Func<string, T> payload) =>
            list.Add(new DemoEmail(name, OutboxMessage.Create(kind, payload(name + "@" + Domain))));

        // Codes stream.
        Add(MessageKinds.SignInCodeEmail, "sign-in-code", to => new SignInCodeEmail(to, "482915", 10));
        Add(MessageKinds.SignInCodeEmail, "sign-in-code-email-change", to => new SignInCodeEmail(to, "730416", 10, SignInCodePurposes.EmailChange));
        Add(MessageKinds.EmailChangeCodeEmail, "email-change-code", to => new EmailChangeCodeEmail(to, "615208", 10));
        Add(MessageKinds.EmailChangeTakenEmail, "email-change-taken", to => new EmailChangeTakenEmail(to));
        Add(MessageKinds.EmailChangedEmail, "email-changed", to => new EmailChangedEmail(
            to, "r***@pavyzdys.test", now.AddMinutes(-2), AccountScreens.EmailRestore + "#" + LinkCode, AccountScreens.EmailRestore, LinkCode, now.AddDays(7)));
        Add(MessageKinds.EmailRestoredEmail, "email-restored", to => new EmailRestoredEmail(to, now));
        Add(MessageKinds.OrganisationCodeEmail, "organisation-code", to => new OrganisationCodeEmail(to, "204817", 30, Organisation, "pavyzdys.test"));
        Add(MessageKinds.MpTakeoverLinkEmail, "mp-takeover-link", to => new MpTakeoverLinkEmail(
            to, Mp, "/seimo-narys/perimti#" + LinkCode, 24, "/seimo-narys/perimti", LinkCode));
        Add(MessageKinds.MpTakeoverLinkEmail, "mp-registration-link", to => new MpTakeoverLinkEmail(
            to, Mp, "/seimo-narys/perimti#" + LinkCode, 24, "/seimo-narys/perimti", LinkCode, AtRegistration: true));
        foreach (var notice in AccountNotices.All)
        {
            Add(MessageKinds.AccountNoticeEmail, "account-notice-" + notice, to => notice switch
            {
                AccountNotices.RoleGranted or AccountNotices.RoleActivated => new AccountNoticeEmail(to, notice, Role: "moderator"),
                AccountNotices.AccountReset => new AccountNoticeEmail(to, notice, PasskeysRemoved: 2),
                AccountNotices.AccountLocked or AccountNotices.AccountUnlocked => new AccountNoticeEmail(to, notice, Statement: notice == AccountNotices.AccountLocked ? "Paskyra naudota klaidinančiai: kelių žmonių vardu skelbtas tas pats tekstas." : "Paskyros savininkas patvirtino, kad ją naudoja vienas asmuo.", Contact: Contact),
                AccountNotices.MpLinkEnded => new AccountNoticeEmail(to, notice, MpName: Mp, Statement: "Profilis perduotas kitam asmeniui pagal Seimo nario prašymą."),
                AccountNotices.MpLinked or AccountNotices.MpGivenUp or AccountNotices.MpOfficeAdded or AccountNotices.MpOfficeRemoved or AccountNotices.MpOfficeLeft =>
                    new AccountNoticeEmail(to, notice, MpName: Mp),
                _ => new AccountNoticeEmail(to, notice),
            });
        }

        // Notices stream: moderation and reports.
        foreach (var notice in ModerationNotices.All)
        {
            var comment = notice is ModerationNotices.CommentHidden or ModerationNotices.CommentRestored;
            Add(MessageKinds.ModerationNoticeEmail, "moderation-notice-" + notice, to => new ModerationNoticeEmail(
                to,
                notice,
                Bill,
                BillTitle,
                notice is ModerationNotices.ReasonPublished or ModerationNotices.ReasonRestored or ModerationNotices.CommentRestored ? null : Statement,
                Reason[..80] + "…",
                BillPath,
                comment ? Contact : null));
        }

        (string Name, string Target, string Outcome, string? Bill, string? Title, string? Name2, string? Path, string? Statement)[] reports =
        [
            ("kept-reason", ReportTargets.Reason, ReportOutcomes.Kept, Bill, BillTitle, null, BillPath, "Argumentas atitinka taisykles."),
            ("hidden-comment", ReportTargets.Comment, ReportOutcomes.Hidden, Bill, BillTitle, null, null, Statement),
            ("blocked-organisation", ReportTargets.Organisation, ReportOutcomes.Blocked, null, null, Organisation, null, OrganisationStatement),
            ("unconfirmed-organisation", ReportTargets.Organisation, ReportOutcomes.Unconfirmed, null, null, Organisation, OrganisationPath, null),
            ("merged-reason", ReportTargets.Reason, ReportOutcomes.Merged, Bill, BillTitle, null, null, null),
            ("corrected-record", ReportTargets.Record, ReportOutcomes.Corrected, Bill, BillTitle, null, BillPath, "Aprašymas patikslintas pagal oficialų tekstą."),
            ("removed-record", ReportTargets.Record, ReportOutcomes.Removed, Bill, BillTitle, null, null, "Aprašyme buvo asmeninė nuomonė, o ne projekto turinys."),
            ("withdrawn-statement", ReportTargets.Statement, ReportOutcomes.Withdrawn, Bill, BillTitle, null, null, null),
            ("gone-comment", ReportTargets.Comment, ReportOutcomes.Gone, Bill, BillTitle, null, null, null),
            ("hidden-profile", ReportTargets.Profile, ReportOutcomes.Hidden, null, null, "Rūta Mockutė", null, "Tekste buvo asmens kontaktai, kuriuos skelbti negalima."),
            ("kept-response", ReportTargets.Response, ReportOutcomes.Kept, Bill, BillTitle, null, BillPath, null),
        ];
        foreach (var r in reports)
        {
            Add(MessageKinds.ReportDecidedEmail, "report-decided-" + r.Name, to => new ReportDecidedEmail(to, r.Target, r.Outcome, r.Bill, r.Title, r.Name2, r.Path, r.Statement));
        }

        Add(MessageKinds.WritingRestrictionEmail, "writing-restriction-restricted", to => new WritingRestrictionEmail(
            to, WritingRestrictionNotices.Restricted, today.AddDays(14), Statement, Contact));
        Add(MessageKinds.WritingRestrictionEmail, "writing-restriction-lifted", to => new WritingRestrictionEmail(
            to, WritingRestrictionNotices.Lifted, null, "Apribojimas panaikintas, nes jis buvo skirtas per klaidą.", Contact));
        Add(MessageKinds.FeedbackReplyEmail, "feedback-reply", to => new FeedbackReplyEmail(
            to, "Ačiū už pastabą. Antraštės dydį pataisėme, kad jis tilptų ir mažame ekrane.", "Antraštė per didelė telefone, tekstas išeina už ekrano krašto…"));
        Add(MessageKinds.FeedbackNoticeEmail, "feedback-notice", to => new FeedbackNoticeEmail(to, 3, "/moderavimas/atsiliepimai"));
        Add(MessageKinds.DigestReviewEmail, "digest-review-weekly", to => new DigestReviewEmail(to, "weekly", now.AddDays(2), 6, 21, "/moderavimas/suvestines"));
        Add(MessageKinds.DigestReviewEmail, "digest-review-before-vote", to => new DigestReviewEmail(to, "before-vote", now.AddDays(1), 1, 2, "/moderavimas/suvestines"));

        // Notices stream: organisations and the MP's own notices.
        foreach (var notice in OrganisationNotices.All)
        {
            var statementNotice = notice.StartsWith("statement-", StringComparison.Ordinal);
            var commentNotice = notice is OrganisationNotices.CommentHidden or OrganisationNotices.CommentRestored;
            var onBill = statementNotice || commentNotice;
            var withReasons = notice is OrganisationNotices.Blocked or OrganisationNotices.StatementHidden or OrganisationNotices.CommentHidden or OrganisationNotices.ConfirmationWithdrawn
                or OrganisationNotices.KeptOutOfSummaries or OrganisationNotices.BackInSummaries;
            Add(MessageKinds.OrganisationNoticeEmail, "organisation-notice-" + notice, to => new OrganisationNoticeEmail(
                to,
                notice,
                Organisation,
                onBill ? BillPath : OrganisationPath,
                withReasons ? (notice == OrganisationNotices.BackInSummaries ? "Organizacija pateikė įrodymų, kad nurodyta svetainė priklauso jai." : OrganisationStatement) : null,
                withReasons ? Contact : null,
                onBill ? Bill : null,
                onBill ? BillTitle : null));
        }

        Add(MessageKinds.MpOfficeInvitationEmail, "mp-office-invitation", to => new MpOfficeInvitationEmail(
            to, Mp, "/seimo-narys/biuras#" + LinkCode, "/seimo-narys/biuras", LinkCode, now.AddDays(7)));
        foreach (var notice in MpResponseNotices.All)
        {
            var hidden = notice is MpResponseNotices.Hidden or MpResponseNotices.CommentHidden;
            Add(MessageKinds.MpResponseNoticeEmail, "mp-response-notice-" + notice, to => new MpResponseNoticeEmail(
                to, notice, Mp, Bill, BillTitle, BillPath, hidden ? "Tekste buvo kito asmens vardas, kurio Seimo narys neįgaliojo." : null, hidden ? Contact : null));
        }

        foreach (var notice in ProfileNotices.All)
        {
            var hidden = notice == ProfileNotices.AboutHidden;
            Add(MessageKinds.ProfileNoticeEmail, "profile-notice-" + notice, to => new ProfileNoticeEmail(
                to, notice, hidden ? "Tekste buvo asmens kontaktai, kuriuos skelbti negalima." : null, hidden ? Contact : null, AccountScreens.Profile));
        }

        // Notices stream: follows, each alone, and the comment notice; then the gathered email with a part of every kind.
        var follows = FollowPayloads(today);
        Add(MessageKinds.FollowPlannedEmail, "follow-planned", to => follows.Planned with { To = to });
        Add(MessageKinds.FollowVotedEmail, "follow-voted", to => follows.Voted with { To = to });
        Add(MessageKinds.FollowVotedEmail, "follow-voted-consensus", to => follows.Consensus with { To = to });
        Add(MessageKinds.FollowClosedEmail, "follow-closed-rejected", to => follows.Rejected with { To = to });
        Add(MessageKinds.FollowClosedEmail, "follow-closed-withdrawn", to => follows.Withdrawn with { To = to });
        Add(MessageKinds.FollowStatementEmail, "follow-statement", to => follows.Statement with { To = to });
        Add(MessageKinds.FollowStatementEmail, "follow-statement-unconfirmed", to => follows.StatementUnconfirmed with { To = to });
        Add(MessageKinds.FollowResponsesEmail, "follow-responses", to => follows.Responses with { To = to });
        Add(MessageKinds.FollowMemberEmail, "follow-member-argument", to => follows.MemberArgument with { To = to });
        Add(MessageKinds.FollowMemberEmail, "follow-member-comment", to => follows.MemberComment with { To = to });
        Add(MessageKinds.CommentNoticeEmail, "comment-notice", to => follows.Comments with { To = to });
        foreach (var rhythm in new[] { "immediate", "daily", "weekly" })
        {
            Add(MessageKinds.GatheredNoticesEmail, "gathered-" + rhythm, to => new GatheredNoticesEmail(
                to,
                rhythm,
                [
                    Part(MessageKinds.FollowPlannedEmail, follows.Planned with { To = to }),
                    Part(MessageKinds.FollowVotedEmail, follows.Voted with { To = to }),
                    Part(MessageKinds.FollowClosedEmail, follows.Rejected with { To = to }),
                    Part(MessageKinds.FollowStatementEmail, follows.Statement with { To = to }),
                    Part(MessageKinds.FollowResponsesEmail, follows.Responses with { To = to }),
                    Part(MessageKinds.FollowMemberEmail, follows.MemberArgument with { To = to }),
                    Part(MessageKinds.CommentNoticeEmail, follows.Comments with { To = to }),
                ],
                AccountScreens.EmailRhythm,
                Token));
        }

        // MP stream.
        AddMpMail(list, now, today);
        return list;
    }

    private static GatheredNotice Part<T>(string kind, T payload) => new(kind, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web));

    private sealed record FollowSet(
        FollowPlannedEmail Planned,
        FollowVotedEmail Voted,
        FollowVotedEmail Consensus,
        FollowClosedEmail Rejected,
        FollowClosedEmail Withdrawn,
        FollowStatementEmail Statement,
        FollowStatementEmail StatementUnconfirmed,
        FollowResponsesEmail Responses,
        FollowMemberEmail MemberArgument,
        FollowMemberEmail MemberComment,
        CommentNoticeEmail Comments);

    private static FollowSet FollowPayloads(DateOnly today)
    {
        const string to = "";
        var account = AccountScreens.EmailRhythm;
        var voted = new FollowVotedEmail(
            to, Bill, BillTitle, BillStages.Consideration, today.AddDays(-1), false, 71, 48, 9, "Pritarta po svarstymo.", "/seimas/" + Bill + "/balsavimas",
            [new FollowedMpChoice(Mp, BallotChoices.Against), new FollowedMpChoice(OfficialMp, BallotChoices.For), new FollowedMpChoice("Rūta Mockutė", null)], account, Token);
        var statement = new FollowStatementEmail(to, Organisation, "domain", "pavyzdys.test", Bill, BillTitle, StatementStances.Remarks, BillPath, account, today, Token);
        var argumentId = Guid.Parse("0193a1b2-0000-7000-8000-0000000000bb");
        var closed = new FollowClosedEmail(to, OtherBill, OtherBillTitle, ClosingReasons.Rejected, today.AddDays(-2), "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/pavyzdys", OtherBillPath, account, Token);
        return new FollowSet(
            new FollowPlannedEmail(to, Bill, BillTitle, BillStages.Adoption, today.AddDays(3), BillPath, account, Token),
            voted,
            voted with { IsConsensus = true, For = null, Against = null, Abstain = null, OfficialNote = null, Mps = [] },
            closed,
            closed with { Reason = ClosingReasons.Withdrawn },
            statement,
            statement with { ConfirmedBy = null, ConfirmedDomain = null, Stance = StatementStances.For },
            new FollowResponsesEmail(
                to, Bill, BillTitle, today.AddDays(-1),
                [new FollowedResponse(Mp, "Reformų partija", ResponseStances.Against), new FollowedResponse(OfficialMp, "Pilietinė sąjunga", ResponseStances.For), new FollowedResponse("Rūta Mockutė", null, ResponseStances.Undecided)],
                BillPath, account, Token),
            new FollowMemberEmail(to, "Rūta Mockutė", Bill, BillTitle, BillTextLinks.ToReason(Bill, argumentId), "/nariai/ruta-mockute", account, MemberTextKinds.Argument, today, Token),
            new FollowMemberEmail(to, "Rūta Mockutė", Bill, BillTitle, BillTextLinks.ToComment(Bill, argumentId), "/nariai/ruta-mockute", account, MemberTextKinds.Comment, today, Token),
            new CommentNoticeEmail(
                to,
                [new CommentedArgumentLine(Bill, BillTitle, BillTextLinks.ToComment(Bill, argumentId), today), new CommentedArgumentLine(OtherBill, OtherBillTitle, BillTextLinks.ToComment(OtherBill, argumentId), today)],
                account,
                Token));
    }

    private static void AddMpMail(List<DemoEmail> list, DateTimeOffset now, DateOnly today)
    {
        void Add<T>(string kind, string name, Func<string, T> payload) =>
            list.Add(new DemoEmail(name, OutboxMessage.Create(kind, payload(name + "@" + Domain))));

        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var runId = Guid.Parse("0193a1b2-0000-7000-8000-0000000000aa");
        var longReason = string.Concat(Enumerable.Repeat("Pajamų mokestis turi būti teisingas visiems dirbantiems ir nepriklausyti nuo to, kur žmogus gyvena. ", 5)).Trim();
        var response = new MpStance(99000006, OtherBill, ResponseStances.Against, now.AddDays(-2));

        MpDigestBill First() => new(
            new DigestItemInfo(Bill, BillTitle, BillStages.Adoption, monday.AddDays(3), 6800, 6150, 4280, 3900, null, null,
                [new DigestReason(Sides.For, "Gynyba finansuojama nekeliant PVM.", 1310), new DigestReason(Sides.For, longReason, 880), new DigestReason(Sides.Against, "Skatina keisti rezidavimo vietą.", 970)],
                ForReasonCount: 14, AgainstReasonCount: 9,
                Organisations:
                [
                    new DigestOrganisation(StatementStances.For, Guid.Parse("0193a1b2-0000-7000-8000-0000000000c1"), "pavyzdine-mokesciu-aiskumo-asociacija", Organisation, Guid.Parse("0193a1b2-0000-7000-8000-0000000000d1")),
                    new DigestOrganisation(StatementStances.For, Guid.Parse("0193a1b2-0000-7000-8000-0000000000c2"), "pavyzdine-dirbanciuju-bendrija", "Pavyzdinė dirbančiųjų bendrija", Guid.Parse("0193a1b2-0000-7000-8000-0000000000d2")),
                    new DigestOrganisation(StatementStances.Against, Guid.Parse("0193a1b2-0000-7000-8000-0000000000c3"), "pavyzdinis-darbdaviu-forumas", "Pavyzdinis darbdavių forumas", Guid.Parse("0193a1b2-0000-7000-8000-0000000000d3")),
                    new DigestOrganisation(StatementStances.Remarks, Guid.Parse("0193a1b2-0000-7000-8000-0000000000c4"), "pavyzdine-viesuju-finansu-stebesena", "Pavyzdinė viešųjų finansų stebėsena", Guid.Parse("0193a1b2-0000-7000-8000-0000000000d4")),
                ]),
            new DeliveryParty(Bill, "Reformų partija", 410, 520),
            null);
        MpDigestBill Second() => new(
            new DigestItemInfo(OtherBill, OtherBillTitle, BillStages.Consideration, null, 2210, 0, 640, 0, 38, 24,
                [new DigestReason(Sides.For, "Mažina žalą sveikatai.", 540), new DigestReason(Sides.For, "Dažniau tikrinami pardavėjai.", 120)],
                ForReasonCount: 2, AgainstReasonCount: 0),
            null,
            response);
        MpDigestEmail Digest(string to, string recipient, string kind, string? preferences, string? unsubscribe, string? copyStop) => new(
            to, recipient, OfficialMp, kind, monday, [First(), Second()], MpMailPaths.PublicCopy(monday), preferences, unsubscribe, copyStop, null, runId);

        Add(MpMailKinds.Digest, "mp-digest-official", to => Digest(to, Recipients.Official, "weekly", MpMailPaths.Preferences(Token), MpMailPaths.OneClick(Token), null));
        Add(MpMailKinds.Digest, "mp-digest-extra-copy", to => Digest(to, Recipients.Extra, "before-vote", null, null, MpMailPaths.StopCopies(Token)));
        Add(MpMailKinds.Digest, "mp-digest-office-copy", to => Digest(to, Recipients.Office, "weekly", null, MpMailPaths.OneClick(Token), MpMailPaths.StopCopies(Token)));

        const string letter = "Gerbiama Seimo nare,\n\nLietuva 2.0 yra savanorių sukurta platforma, kurioje gyventojai pasako, ko prašo iš Seimo narių ir kodėl.\n\nKiekvieną savaitę atsiųsime suvestinę:\n- kiek žmonių prašo balsuoti už ir prieš kiekvieną projektą;\n- kokie argumentai dažniausi.\n\nBūsime dėkingi už Jūsų atsiliepimą.";
        Add(MpMailKinds.Introduction, "mp-introduction", to => new MpIntroductionEmail(
            to, 1, "Lietuva 2.0: Jūsų rinkėjų nuomonė vienoje vietoje", letter, "Donatas Šimelis", "VšĮ Lietuva 2.0 direktorius",
            MpMailPaths.Preferences(Token), MpMailPaths.OneClick(Token), false, null));
        Add(MpMailKinds.Introduction, "mp-introduction-test", to => new MpIntroductionEmail(
            to, 1, "Lietuva 2.0: Jūsų rinkėjų nuomonė vienoje vietoje", letter, "Donatas Šimelis", "VšĮ Lietuva 2.0 direktorius",
            null, null, true, null));

        foreach (var notice in MpMailNotices.All)
        {
            Add(MpMailKinds.Notice, "mp-mail-notice-" + notice, to => new MpMailNoticeEmail(
                to,
                notice,
                OfficialMp,
                notice == MpMailNotices.Changed ? "weekly" : null,
                notice switch
                {
                    MpMailNotices.ExtraConfirm => MpMailPaths.ConfirmExtra(Token),
                    MpMailNotices.ExtraWelcome => MpMailPaths.StopCopies(Token),
                    _ => MpMailPaths.Preferences(Token),
                },
                notice is MpMailNotices.ExtraConfirm or MpMailNotices.ExtraConfirmed or MpMailNotices.ExtraRemoved ? "biuras@pavyzdys.test" : null,
                notice == MpMailNotices.ExtraConfirm ? 24 : null));
        }

        foreach (var frequency in new[] { Frequencies.BeforeVote, Frequencies.Stopped })
        {
            Add(MpMailKinds.Notice, "mp-mail-notice-changed-" + frequency, to => new MpMailNoticeEmail(
                to, MpMailNotices.Changed, OfficialMp, frequency, MpMailPaths.Preferences(Token), null));
        }
    }
}
