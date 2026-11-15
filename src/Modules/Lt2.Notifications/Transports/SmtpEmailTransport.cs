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

/// <summary>
/// Sends email over SMTP with MailKit. Locally this delivers to the Mailpit container. A batch shares one connection, made
/// when its first message is sent and made again if the server dropped it.
/// </summary>
internal sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        await using var connection = new SmtpConnection(options.Value);
        await connection.SendAsync(message, cancellationToken);
    }

    public Task<IEmailConnection> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEmailConnection>(new SmtpConnection(options.Value));

    private sealed class SmtpConnection(SmtpOptions settings) : IEmailConnection
    {
        private readonly SmtpClient _client = new();

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            using var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

            if (!_client.IsConnected)
            {
                await _client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
                if (!string.IsNullOrEmpty(settings.UserName))
                {
                    await _client.AuthenticateAsync(settings.UserName, settings.Password ?? "", cancellationToken);
                }
            }

            await _client.SendAsync(mime, cancellationToken);
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
