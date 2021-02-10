using System;
using System.Net.Mail;
using Framework.Infrastructure.Logging;

namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// Generic email sender.
    /// </summary>
    public class MailSender : IMailSender
    {
        /// <summary>
        /// SMTP client.
        /// </summary>
        private static readonly SmtpClient smtp = new SmtpClient();

        /// <summary>
        /// Generic logger.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MailSender"/> class.
        /// </summary>
        public MailSender()
        {
            logger = new Log4NetLogger();
            IsBodyHtml = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MailSender"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public MailSender(ILogger logger)
        {
            this.logger = logger;
            IsBodyHtml = true;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is body HTML.
        /// </summary>
        /// <value>
        ///     <c>True</c> if this instance is body HTML; otherwise, <c>false</c>.
        /// </value>
        public bool IsBodyHtml { get; set; }

        /// <summary>
        /// Sends the mail asynchronously.
        /// </summary>
        /// <param name="message">The message to send.</param>
        public void Send(MailMessage message)
        {
            try
            {
                var msg = message;
                msg.IsBodyHtml = IsBodyHtml;
                smtp.Send(msg);
                //logger.Information(FormatMessage(message));
            }
            catch (System.Exception e)
            {
                logger.Error("SMTP exception", e);
                throw new NotifyException("Mail sending failed", e);
            }
        }

        /// <summary>
        /// Formats the mail message.
        /// </summary>
        /// <param name="message">The mail message.</param>
        /// <returns>Formatted message.</returns>
        private string FormatMessage(MailMessage message)
        {
            return String.Format("Email sent to: {0}\nSubject: {1}\nBody: {2}", message.To, message.Subject, message.Body);
        }
    }
}