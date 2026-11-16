using Lt2.Notifications;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Lt2.UnitTests;

/// <summary>Design 8.19: when summaries really go, which runs count, who receives one and in which order.</summary>
public sealed class SendingRuleTests
{
    private static readonly DateTimeOffset Monday0700 = new(2026, 11, 16, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sending_is_off_by_the_setting_and_otherwise_what_the_setting_says_once_the_letter_went()
    {
        SendingRules.State("off", introductionSent: true, inventedData: false, mailCaught: false).ShouldBe(new SendingState("off", "off", null));
        SendingRules.State("approval", true, false, false).ShouldBe(new SendingState("approval", "approval", null));
        SendingRules.State("automatic", true, false, false).ShouldBe(new SendingState("automatic", "automatic", null));
        SendingRules.State("nonsense", true, false, false).Mode.ShouldBe("off");
    }

    [Fact]
    public void Nothing_goes_before_the_introduction_letter_whatever_the_setting_says()
    {
        var state = SendingRules.State("automatic", introductionSent: false, inventedData: false, mailCaught: false);
        state.Mode.ShouldBe("off");
        state.Lock.ShouldBe(SendingRules.NoIntroduction);
        state.On.ShouldBeFalse();
    }

    [Fact]
    public void Invented_data_reaches_no_mailbox_unless_the_deployment_declares_its_mail_caught()
    {
        SendingRules.State("approval", introductionSent: true, inventedData: true, mailCaught: false).ShouldBe(new SendingState("off", "approval", SendingRules.NotCaught));
        SendingRules.State("approval", true, inventedData: true, mailCaught: true).Mode.ShouldBe("approval");
        SendingRules.MailMayLeave(inventedData: true, mailCaught: false).ShouldBeFalse("the introduction letter is held too");
        SendingRules.MailMayLeave(inventedData: false, mailCaught: false).ShouldBeTrue("real data: the transport decides");
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    public async Task The_data_is_invented_when_the_deployment_says_so_or_the_parliament_data_holds_anything_synthetic(
        bool demoAllowed, bool synthetic, bool caught, bool mayLeave)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Allowed"] = demoAllowed ? "true" : "false",
            ["Email:Caught"] = caught ? "true" : "false",
        }).Build();
        var locks = new MailLocks(configuration, new Parliament(synthetic));
        (await locks.InventedDataAsync(CancellationToken.None)).ShouldBe(demoAllowed || synthetic);
        (await locks.MailMayLeaveAsync(CancellationToken.None)).ShouldBe(mayLeave);
    }

    private sealed class Parliament(bool synthetic) : IMpMailAddresses
    {
        public Task<IReadOnlyDictionary<long, string>> OfficialAddressesForSendingAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());

        public Task<bool> HoldsInventedDataAsync(CancellationToken cancellationToken) => Task.FromResult(synthetic);
    }

    [Fact]
    public void A_lock_is_named_only_when_it_is_what_keeps_sending_off()
    {
        SendingRules.State("off", introductionSent: false, inventedData: true, mailCaught: false).Lock.ShouldBeNull("the setting itself says off");
    }

    [Theory]
    [InlineData("weekly", "off", "dry-run")]
    [InlineData("urgent", "off", "dry-run")]
    [InlineData("weekly", "approval", "waiting")]
    [InlineData("before-vote", "approval", "waiting")]
    [InlineData("urgent", "approval", "sent")]
    [InlineData("weekly", "automatic", "sent")]
    [InlineData("before-vote", "automatic", "sent")]
    public void A_summary_is_a_dry_run_while_sending_is_off_waits_with_approval_and_an_urgent_one_goes_at_once(string kind, string mode, string status) =>
        SendingRules.NewRunStatus(kind, mode).ShouldBe(status);

    [Theory]
    [InlineData("waiting", true, true)]
    [InlineData("sent", true, true)]
    [InlineData("dry-run", true, false)]
    [InlineData("cancelled", true, false)]
    [InlineData("expired", true, false)]
    [InlineData("dry-run", false, true)]
    [InlineData("waiting", false, true)]
    [InlineData("sent", false, true)]
    [InlineData("cancelled", false, false)]
    [InlineData("expired", false, false)]
    public void While_sending_is_on_only_waiting_and_sent_runs_count_and_a_declined_or_expired_one_never_does(string status, bool sendingOn, bool counts) =>
        SendingRules.Counts(status, sendingOn).ShouldBe(counts);

    [Fact]
    public void Only_an_email_that_went_or_would_go_counts_for_the_ceiling()
    {
        SendingRules.CountsForCeiling("sent", "sent", sendingOn: true).ShouldBeTrue();
        SendingRules.CountsForCeiling("sent", "queued", true).ShouldBeTrue();
        SendingRules.CountsForCeiling("sent", "introduced", true).ShouldBeTrue("the letter in its place is an email that week");
        SendingRules.CountsForCeiling("waiting", "waiting", true).ShouldBeTrue("a waiting summary holds its place");
        SendingRules.CountsForCeiling("sent", "skipped-preference", true).ShouldBeFalse();
        SendingRules.CountsForCeiling("sent", "suppressed", true).ShouldBeFalse();
        SendingRules.CountsForCeiling("sent", "skipped-ceiling", true).ShouldBeFalse();
        SendingRules.CountsForCeiling("cancelled", "not-sent", true).ShouldBeFalse();
        SendingRules.CountsForCeiling("dry-run", "dry-run", sendingOn: true).ShouldBeFalse("earlier dry runs do not thin the first real summary");
        SendingRules.CountsForCeiling("dry-run", "dry-run", sendingOn: false).ShouldBeTrue("as before while sending is off");
    }

    [Fact]
    public void A_waiting_summary_expires_after_the_set_hours_or_when_its_sitting_day_begins_whichever_is_first()
    {
        // Monday 07:00 in Vilnius; a sitting on Tuesday begins at Tuesday 00:00, before 24 hours have passed.
        var tuesday = new DateOnly(2026, 11, 17);
        SendingRules.ExpiresAt(Monday0700, 24, tuesday).ShouldBe(VilniusTime.StartOf(tuesday));
        SendingRules.ExpiresAt(Monday0700, 24, tuesday.AddDays(2)).ShouldBe(Monday0700.AddHours(24));
        SendingRules.ExpiresAt(Monday0700, 6, null).ShouldBe(Monday0700.AddHours(6));
        SendingRules.Expired(Monday0700.AddHours(6), Monday0700.AddHours(6)).ShouldBeTrue("nothing is sent late");
        SendingRules.Expired(Monday0700.AddHours(6).AddSeconds(-1), Monday0700.AddHours(6)).ShouldBeFalse();
        SendingRules.Expired(Monday0700, null).ShouldBeFalse();
    }

    [Fact]
    public void A_weekly_summarys_sitting_is_the_first_planned_date_it_holds_from_its_day_on()
    {
        var monday = new DateOnly(2026, 11, 16);
        SendingRules.SittingOf(null, monday, [monday.AddDays(3), null, monday.AddDays(1), monday.AddDays(-2)]).ShouldBe(monday.AddDays(1));
        SendingRules.SittingOf(monday.AddDays(2), monday, [monday.AddDays(1)]).ShouldBe(monday.AddDays(2), "a before-vote summary's own sitting");
        SendingRules.SittingOf(null, monday, [null]).ShouldBeNull();
    }

    [Theory]
    [InlineData("default", "weekly", true)]
    [InlineData("default", "before-vote", true)]
    [InlineData("default", "urgent", true)]
    [InlineData("weekly", "weekly", true)]
    [InlineData("weekly", "before-vote", false)]
    [InlineData("weekly", "urgent", false)]
    [InlineData("before-vote", "weekly", false)]
    [InlineData("before-vote", "before-vote", true)]
    [InlineData("before-vote", "urgent", true)]
    [InlineData("stopped", "weekly", false)]
    [InlineData("stopped", "urgent", false)]
    [InlineData("stopped", "introduction", false)]
    [InlineData("weekly", "introduction", true)]
    public void The_mps_choice_lets_through_its_kinds_and_an_urgent_summary_counts_as_one_before_votes(string frequency, string kind, bool allowed) =>
        SendingRules.PreferenceAllows(frequency, kind).ShouldBe(allowed);

    [Fact]
    public void Recipients_are_never_a_suppressed_address_then_follow_the_choice_then_get_the_letter_first_then_the_ceiling()
    {
        SendingRules.DeliveryStatus(suppressed: true, "default", "weekly", introduced: false, alreadyThisWeek: 9, maxPerWeek: 2).ShouldBe("suppressed");
        SendingRules.DeliveryStatus(false, "before-vote", "weekly", introduced: true, 0, 2).ShouldBe("skipped-preference");
        SendingRules.DeliveryStatus(false, "default", "weekly", introduced: false, 0, 2).ShouldBe("introduced", "the letter in place of the first summary");
        SendingRules.DeliveryStatus(false, "default", "weekly", true, alreadyThisWeek: 2, maxPerWeek: 2).ShouldBe("skipped-ceiling");
        SendingRules.DeliveryStatus(false, "default", "urgent", true, alreadyThisWeek: 2, maxPerWeek: 2).ShouldBe("queued", "an urgent summary may exceed the ceiling by one");
        SendingRules.DeliveryStatus(false, "default", "urgent", true, alreadyThisWeek: 3, maxPerWeek: 2).ShouldBe("skipped-ceiling");
        SendingRules.DeliveryStatus(false, "default", "weekly", true, 1, 2).ShouldBe("queued");
    }

    [Fact]
    public void An_extra_recipient_gets_a_copy_only_once_confirmed_only_of_what_the_mp_gets_and_never_at_a_suppressed_address()
    {
        SendingRules.ExtraStatus("queued", extraConfirmed: true, extraSuppressed: false).ShouldBe("queued");
        SendingRules.ExtraStatus("queued", extraConfirmed: false, extraSuppressed: false).ShouldBeNull("waiting for the official address");
        SendingRules.ExtraStatus("skipped-preference", true, false).ShouldBeNull();
        SendingRules.ExtraStatus("introduced", true, false).ShouldBeNull("the letter is the MP's own");
        SendingRules.ExtraStatus("queued", true, extraSuppressed: true).ShouldBe("suppressed");
    }

    [Fact]
    public void One_mps_email_lists_the_bills_their_committee_leads_then_the_planned_ones_by_date_then_by_participants()
    {
        static DigestItemInfo Item(string number, DateOnly? planned, int participants) =>
            new(number, number, null, planned, participants, 0, 0, 0, null, null, []);
        var monday = new DateOnly(2026, 11, 16);
        var items = new[]
        {
            Item("XVP-0001", null, 500),
            Item("XVP-0002", monday.AddDays(3), 10),
            Item("XVP-0003", monday.AddDays(1), 5),
            Item("XVP-0004", null, 900),
            Item("XVP-0005", null, 1),
        };

        SendingRules.OrderForMp(items, new HashSet<string>()).Select(i => i.BillNumber)
            .ShouldBe(["XVP-0003", "XVP-0002", "XVP-0004", "XVP-0001", "XVP-0005"], "no lead committee known: that group is empty");
        SendingRules.OrderForMp(items, new HashSet<string> { "XVP-0005" }).Select(i => i.BillNumber).First()
            .ShouldBe("XVP-0005", "a bill the MP's committee leads comes first");
    }

    [Theory]
    [InlineData("padejeja@pavyzdys.test", true)]
    [InlineData("  padejeja@pavyzdys.test ", true)]
    [InlineData("padejeja", false)]
    [InlineData("pa dejeja@pavyzdys.test", false)]
    [InlineData("a@b@pavyzdys.test", false)]
    [InlineData("padejeja@localhost", false)]
    [InlineData("", false)]
    public void The_extra_recipient_is_one_plain_address(string address, bool valid) => SendingRules.IsAddress(address).ShouldBe(valid);

    [Fact]
    public void Declining_a_summary_needs_a_statement_of_twenty_characters() =>
        SendingRules.CancelStatementMinimum.ShouldBe(20);

    [Fact]
    public void A_permanent_refusal_is_a_5xx_answer_to_the_recipient_and_nothing_else()
    {
        var mailbox = new MimeKit.MailboxAddress("", "pavyzdys.laima.uosyte@lrs.lt");
        Lt2.Notifications.Outbox.OutboxDispatcher.PermanentRefusal(new MailKit.Net.Smtp.SmtpCommandException(
            MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable, mailbox, "550 no such user")).ShouldBeTrue();
        Lt2.Notifications.Outbox.OutboxDispatcher.PermanentRefusal(new MailKit.Net.Smtp.SmtpCommandException(
            MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxBusy, mailbox, "450 try later")).ShouldBeFalse("a temporary refusal is retried");
        Lt2.Notifications.Outbox.OutboxDispatcher.PermanentRefusal(new MailKit.Net.Smtp.SmtpCommandException(
            MailKit.Net.Smtp.SmtpErrorCode.SenderNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable, mailbox, "550 sender")).ShouldBeFalse("our own sender refused is not the MP's address");
        Lt2.Notifications.Outbox.OutboxDispatcher.PermanentRefusal(new IOException("connection reset")).ShouldBeFalse();
    }

    [Fact]
    public void A_link_token_is_derived_the_same_way_every_time_and_a_new_generation_or_another_key_gives_another()
    {
        static MailKeys Keys(string key) => new(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [MailKeys.ConfigurationKey] = key }).Build());
        var keys = Keys("one-key");
        var token = keys.DerivedToken(99000006, "preferences", 0);
        token.Length.ShouldBe(43, "32 bytes, URL-safe");
        MailKeys.LooksLikeToken(token).ShouldBeTrue();
        keys.DerivedToken(99000006, "preferences", 0).ShouldBe(token);
        keys.DerivedToken(99000006, "preferences", 1).ShouldNotBe(token);
        keys.DerivedToken(99000007, "preferences", 0).ShouldNotBe(token);
        keys.DerivedToken(99000006, "extra-stop", 0).ShouldNotBe(token);
        Keys("another-key").DerivedToken(99000006, "preferences", 0).ShouldNotBe(token);
        MailKeys.TokenHash(token).ShouldNotContain(token);
        keys.AddressHash(" Pavyzdys.Laima.Uosyte@LRS.lt ").ShouldBe(keys.AddressHash("pavyzdys.laima.uosyte@lrs.lt"), "compared without case and spaces");
        keys.AddressHash("pavyzdys.laima.uosyte@lrs.lt").ShouldNotContain("uosyte");
        MailKeys.LooksLikeToken("not a token").ShouldBeFalse();
    }
}
