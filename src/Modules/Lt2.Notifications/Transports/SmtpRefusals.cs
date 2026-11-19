using System.Text.RegularExpressions;
using MailKit.Net.Smtp;

namespace Lt2.Notifications.Transports;

/// <summary>What a failed send says about who is to blame (design 8.19.6).</summary>
internal enum SendFailure
{
    /// <summary>Anything else: a lost connection, a temporary answer about the recipient, a refused message. Retried with backoff.</summary>
    Other,

    /// <summary>
    /// The receiving server refused the recipient's mailbox for good (5.1.x, 5.2.x, or a plain 5xx to RCPT TO). The only failure
    /// that can put a recipient on the suppression list.
    /// </summary>
    Mailbox,

    /// <summary>
    /// The sender or the account is refused: a reply to MAIL FROM or to the authentication, an enhanced status 4.7.x or 5.7.x, or
    /// a reply that speaks of a limit, a quota or a rate. Says nothing about the recipient: the message waits and the stream
    /// pauses.
    /// </summary>
    Sender,
}

/// <summary>Reads a failed SMTP send: a pure function of the exception, so the rule is testable over real reply lines.</summary>
internal static partial class SmtpRefusals
{
    /// <summary>How long a stream stops sending after the server refused its sender or account.</summary>
    internal static readonly TimeSpan StreamPause = TimeSpan.FromHours(1);

    public static SendFailure Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error is MailKit.Security.AuthenticationException)
        {
            return SendFailure.Sender;
        }

        if (error is not SmtpCommandException smtp)
        {
            return SendFailure.Other;
        }

        var text = smtp.Message ?? "";
        var status = (int)smtp.StatusCode;
        var enhanced = EnhancedStatus().Match(text);
        var cls = enhanced.Success ? enhanced.Groups[1].Value : "";
        var subject = enhanced.Success ? enhanced.Groups[2].Value : "";

        // A message too big is about the message, not the sender's quota.
        if ((cls is "4" or "5" && subject == "3") || SizeText().IsMatch(text))
        {
            return SendFailure.Other;
        }

        if (smtp.ErrorCode == SmtpErrorCode.SenderNotAccepted)
        {
            return SendFailure.Sender;
        }

        // 5.1.x (the address) and 5.2.x (the mailbox, even "over quota") belong to the recipient, whatever else the line says.
        var mailboxCode = cls == "5" && subject is "1" or "2";
        if (smtp.ErrorCode == SmtpErrorCode.RecipientNotAccepted && status is >= 500 and < 600 && mailboxCode)
        {
            return SendFailure.Mailbox;
        }

        var policy = enhanced.Success && subject == "7" && cls is "4" or "5" && !Greylisting().IsMatch(text);
        if (policy || LimitText().IsMatch(text))
        {
            return SendFailure.Sender;
        }

        return smtp.ErrorCode == SmtpErrorCode.RecipientNotAccepted && status is >= 500 and < 600 ? SendFailure.Mailbox : SendFailure.Other;
    }

    [GeneratedRegex(@"(?<![\d.])([245])\.(\d{1,3})\.\d{1,3}(?![\d.])")]
    private static partial Regex EnhancedStatus();

    [GeneratedRegex(@"\b(rate|quota|limit|limits|limited|too many|throttl\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LimitText();

    [GeneratedRegex(@"gr[ae]y-?list", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Greylisting();

    [GeneratedRegex(@"message size|size limit|too large|too big", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeText();
}
