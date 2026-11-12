using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Lt2.Notifications.Transports;

internal sealed class SmtpOptions
{
    public const string Section = "Email:Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary><c>None</c> (local Mailpit), <c>StartTls</c> or <c>SslOnConnect</c>.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.None;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "prisijungimas@lt2.localhost";

    public string FromName { get; set; } = "Lietuva 2.0";
}

/// <summary>Sends email over SMTP with MailKit. Locally this delivers to the Mailpit container.</summary>
internal sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? "", cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
