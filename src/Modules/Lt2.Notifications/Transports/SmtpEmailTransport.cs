using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Lt2.Notifications.Outbox;

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

/// <summary>
/// The MP stream (design 8.19.3): its own sender address (<c>Email:Mp:FromAddress</c>) and, when <c>Email:Mp:Smtp:Host</c> is
/// set, its own SMTP account (<c>Email:Mp:Smtp</c>); otherwise the common account. Deployment configuration, like the rest of
/// <c>Email</c>.
/// </summary>
internal sealed class MpStreamOptions
{
    public const string Section = "Email:Mp";

    public string FromAddress { get; set; } = "suvestines@lt2.localhost";

    public string FromName { get; set; } = "Lietuva 2.0";

    public SmtpOptions? Smtp { get; set; }
}

/// <summary>
/// The notices stream (design 8.19.3): everything to members and the team that is not a code or an account-security email, from
/// its own sender address (<c>Email:Notices:FromAddress</c>) and, when <c>Email:Notices:Smtp:Host</c> is set, its own SMTP account
/// (<c>Email:Notices:Smtp</c>); otherwise the common account. A flood of notices then cannot stop sign-in codes.
/// </summary>
internal sealed class NoticesStreamOptions
{
    public const string Section = "Email:Notices";

    public string FromAddress { get; set; } = "pranesimai@lt2.localhost";

    public string FromName { get; set; } = "Lietuva 2.0";

    public SmtpOptions? Smtp { get; set; }
}

/// <summary>
/// Sends email over SMTP with MailKit. Locally this delivers to the Mailpit container. A batch shares one connection per
/// stream, made when its first message is sent and made again if the server dropped it; notices and MP mail go on their own
/// streams (design 8.19.3), so a refusal, a quota or an outage there does not stop sign-in codes on the common one.
/// </summary>
internal sealed class SmtpEmailTransport(IOptions<SmtpOptions> options, IOptions<MpStreamOptions> mp, IOptions<NoticesStreamOptions> notices) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        await using var connection = new StreamConnections(options.Value, mp.Value, notices.Value);
        await connection.SendAsync(message, cancellationToken);
    }

    public Task<IEmailConnection> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEmailConnection>(new StreamConnections(options.Value, mp.Value, notices.Value));

    /// <summary>The codes stream's connection (the common account) and, for notices and MP mail, their own: each made on first use.</summary>
    private sealed class StreamConnections(SmtpOptions common, MpStreamOptions mp, NoticesStreamOptions notices) : IEmailConnection
    {
        private SmtpConnection? _common;
        private SmtpConnection? _notices;
        private SmtpConnection? _mp;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            switch (message.Stream)
            {
                case EmailStreams.Mp:
                    return (_mp ??= new SmtpConnection(Account(mp.Smtp))).SendAsync(message, mp.FromName, mp.FromAddress, cancellationToken);
                case EmailStreams.Notices:
                    return (_notices ??= new SmtpConnection(Account(notices.Smtp))).SendAsync(message, notices.FromName, notices.FromAddress, cancellationToken);
                default:
                    return (_common ??= new SmtpConnection(common)).SendAsync(message, common.FromName, common.FromAddress, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var connection in new[] { _common, _notices, _mp })
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }
            }
        }

        /// <summary>A stream's own SMTP account when its host is set, else the common one.</summary>
        private SmtpOptions Account(SmtpOptions? own) => string.IsNullOrWhiteSpace(own?.Host) ? common : own;
    }

    private sealed class SmtpConnection(SmtpOptions settings) : IAsyncDisposable
    {
        private readonly SmtpClient _client = new();

        public async Task SendAsync(EmailMessage message, string fromName, string fromAddress, CancellationToken cancellationToken)
        {
            using var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(fromName, fromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            foreach (var (name, value) in message.Headers ?? new Dictionary<string, string>())
            {
                mime.Headers.Add(name, value);
            }

            mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

            if (!_client.IsConnected)
            {
                await _client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
                if (!string.IsNullOrEmpty(settings.UserName))
                {
                    await _client.AuthenticateAsync(settings.UserName, settings.Password ?? "", cancellationToken);
                }
            }

            await _client.SendAsync(new SmtpSendRequest(mime), cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_client.IsConnected)
            {
                try
                {
                    await _client.DisconnectAsync(quit: true);
                }
#pragma warning disable CA1031 // Saying goodbye to a server that already went away changes nothing.
                catch (Exception ex) when (ex is IOException or MailKit.ProtocolException or MailKit.CommandException)
#pragma warning restore CA1031
                {
                }
            }

            _client.Dispose();
        }
    }
}

/// <summary>The default <see cref="IEmailConnection"/>: each message on its own, through <see cref="IEmailTransport.SendAsync"/>.</summary>
internal sealed class MessageByMessage(IEmailTransport transport) : IEmailConnection
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => transport.SendAsync(message, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
