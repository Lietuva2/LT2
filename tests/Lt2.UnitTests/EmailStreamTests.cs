using System.Reflection;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Outbox;
using Lt2.Notifications.Transports;
using Lt2.SharedKernel;
using MailKit.Net.Smtp;
using MimeKit;
using Shouldly;

namespace Lt2.UnitTests;

/// <summary>Design 8.19.3, 8.19.6: the three email streams, their daily ceilings, and who a refusal is about.</summary>
public sealed class EmailStreamTests
{
    private static readonly MailboxAddress Recipient = new("", "ruta.mockute@pavyzdys.test");

    private static IEnumerable<string> EveryOutboxKind() =>
        typeof(MessageKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Concat(MpMailKinds.All);

    [Fact]
    public void Every_outbox_kind_has_a_stream_so_a_new_kind_cannot_be_added_without_one()
    {
        var kinds = EveryOutboxKind().ToList();
        kinds.ShouldNotBeEmpty();
        foreach (var kind in kinds)
        {
            EmailStreams.All.ShouldContain(EmailStreams.Of(kind), kind);
        }

        EmailStreams.Kinds.Except(kinds).ShouldBeEmpty("a stream for a kind that does not exist");
    }

    [Fact]
    public void A_kind_without_a_stream_is_refused_not_guessed() =>
        Should.Throw<InvalidOperationException>(() => EmailStreams.Of("email.nobody-wrote-this"));

    [Fact]
    public void Sign_in_and_registration_codes_and_every_account_security_email_are_on_the_codes_stream()
    {
        foreach (var kind in new[]
        {
            MessageKinds.SignInCodeEmail, MessageKinds.AccountNoticeEmail, MessageKinds.EmailChangeCodeEmail, MessageKinds.EmailChangeTakenEmail,
            MessageKinds.EmailChangedEmail, MessageKinds.EmailRestoredEmail, MessageKinds.OrganisationCodeEmail, MessageKinds.MpTakeoverLinkEmail,
        })
        {
            EmailStreams.Of(kind).ShouldBe(EmailStreams.Codes, kind);
        }
    }

    [Fact]
    public void Follow_gathered_moderation_report_feedback_and_organisation_emails_are_on_the_notices_stream()
    {
        foreach (var kind in new[]
        {
            MessageKinds.FollowPlannedEmail, MessageKinds.FollowVotedEmail, MessageKinds.FollowClosedEmail, MessageKinds.FollowStatementEmail,
            MessageKinds.FollowResponsesEmail, MessageKinds.FollowMemberEmail, MessageKinds.GatheredNoticesEmail, MessageKinds.CommentNoticeEmail,
            MessageKinds.ModerationNoticeEmail, MessageKinds.ReportDecidedEmail, MessageKinds.WritingRestrictionEmail, MessageKinds.FeedbackReplyEmail,
            MessageKinds.FeedbackNoticeEmail, MessageKinds.OrganisationNoticeEmail, MessageKinds.ProfileNoticeEmail, MessageKinds.DigestReviewEmail,
        })
        {
            EmailStreams.Of(kind).ShouldBe(EmailStreams.Notices, kind);
        }
    }

    [Fact]
    public void Mp_mail_is_on_the_mp_stream() =>
        MpMailKinds.All.ShouldAllBe(kind => EmailStreams.Of(kind) == EmailStreams.Mp);

    [Fact]
    public void The_codes_kinds_are_the_ones_sent_first() =>
        OutboxDispatcher.FirstKinds.ShouldBe(EmailStreams.KindsOf(EmailStreams.Codes), ignoreOrder: true);

    [Theory]
    [InlineData(0, 0, int.MaxValue)]
    [InlineData(0, 50_000, int.MaxValue)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 999, 1)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1000, 1500, 0)]
    public void A_stream_may_send_what_is_left_of_its_ceiling_and_a_ceiling_of_zero_is_none(int ceiling, int sent, int remaining) =>
        StreamCeilings.Remaining(ceiling, sent).ShouldBe(remaining);

    [Fact]
    public void A_stream_has_reached_its_ceiling_when_it_has_sent_it_and_never_with_no_ceiling()
    {
        StreamCeilings.Reached(3, 2).ShouldBeFalse();
        StreamCeilings.Reached(3, 3).ShouldBeTrue();
        StreamCeilings.Reached(0, 1_000_000).ShouldBeFalse();
    }

    [Fact]
    public void The_ceiling_counts_the_last_twenty_four_hours() => StreamCeilings.Window.ShouldBe(TimeSpan.FromHours(24));

    [Fact]
    public void Every_stream_has_its_own_ceiling_setting_that_is_off_by_default_and_never_negative()
    {
        var keys = EmailStreams.All.Select(StreamCeilings.KeyOf).ToList();
        keys.Select(k => k.Name).Distinct().Count().ShouldBe(EmailStreams.All.Count);
        foreach (var key in keys)
        {
            key.Default.ShouldBe(0, key.Name);
            SettingKeys.All.ShouldContain(key);
            key.IsValid(System.Text.Json.JsonSerializer.SerializeToElement(-1)).ShouldBeFalse();
            key.IsValid(System.Text.Json.JsonSerializer.SerializeToElement(1000)).ShouldBeTrue();
        }
    }

    private static SmtpCommandException Reply(SmtpErrorCode where, int status, string line) =>
        where == SmtpErrorCode.RecipientNotAccepted
            ? new SmtpCommandException(where, (SmtpStatusCode)status, Recipient, line)
            : new SmtpCommandException(where, (SmtpStatusCode)status, line);

    [Theory]
    // The recipient's mailbox, refused for good: the only refusals that can suppress an address.
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 550, "550 5.1.1 <ruta.mockute@pavyzdys.test>: Recipient address rejected: User unknown", "Mailbox")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 550, "550 No such user here", "Mailbox")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 553, "553 5.1.3 The recipient address is not a valid RFC 5321 address", "Mailbox")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 550, "550 5.2.1 The email account that you tried to reach is disabled", "Mailbox")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 552, "552 5.2.2 The recipient's mailbox is full: over quota", "Mailbox")]
    // The sender's own limit or account: a limit, a quota or a rate, an enhanced 4.7.x or 5.7.x, MAIL FROM, the login.
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 550, "550 5.4.5 Daily sending limit exceeded", "Sender")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 554, "554 5.7.1 Message rejected: you have reached the daily limit of 1000 emails for this mailbox", "Sender")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 550, "550 Mailbox sending quota exceeded, try again in 24 hours", "Sender")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 450, "450 4.7.1 Too many messages from this account, try again later", "Sender")]
    [InlineData(SmtpErrorCode.MessageNotAccepted, 452, "452 4.5.3 Hourly sending limit exceeded", "Sender")]
    [InlineData(SmtpErrorCode.MessageNotAccepted, 554, "554 5.7.1 Sending rate exceeded", "Sender")]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode, 421, "421 4.7.0 Rate limit exceeded; try again later", "Sender")]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode, 530, "530 5.7.0 Authentication required", "Sender")]
    [InlineData(SmtpErrorCode.SenderNotAccepted, 550, "550 sender not accepted", "Sender")]
    [InlineData(SmtpErrorCode.SenderNotAccepted, 553, "553 5.7.1 Sender address rejected: not owned by the authenticated user", "Sender")]
    // Anything else is retried as before.
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 450, "450 4.2.1 The user you are trying to contact is receiving mail too quickly", "Other")]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, 451, "451 4.7.1 Greylisted, please try again in 5 minutes", "Other")]
    [InlineData(SmtpErrorCode.MessageNotAccepted, 552, "552 5.3.4 Message size exceeds fixed limit", "Other")]
    [InlineData(SmtpErrorCode.MessageNotAccepted, 554, "554 5.6.0 Message content rejected", "Other")]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode, 451, "451 4.3.0 Local error in processing", "Other")]
    public void A_refusal_is_about_the_mailbox_the_sender_or_neither(SmtpErrorCode where, int status, string line, string expected) =>
        SmtpRefusals.Classify(Reply(where, status, line)).ToString().ShouldBe(expected, line);

    [Fact]
    public void A_failed_login_is_the_senders_and_a_lost_connection_is_neither()
    {
        SmtpRefusals.Classify(new MailKit.Security.AuthenticationException("535: Authentication credentials invalid")).ShouldBe(SendFailure.Sender);
        SmtpRefusals.Classify(new IOException("connection reset")).ShouldBe(SendFailure.Other);
        SmtpRefusals.Classify(new TimeoutException()).ShouldBe(SendFailure.Other);
        SmtpRefusals.Classify(new InvalidOperationException("No template")).ShouldBe(SendFailure.Other);
    }

    [Fact]
    public void A_stream_pauses_for_an_hour_after_its_sender_is_refused() => SmtpRefusals.StreamPause.ShouldBe(TimeSpan.FromHours(1));
}
