using System;
using System.Net.Mail;
using Framework.Infrastructure.Logging;

namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// Generic email sender.
    /// </summary>
    public class SmsSender : ISmsSender
    {
        /// <summary>
        /// Generic logger.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MailSender"/> class.
        /// </summary>
        public SmsSender()
        {
            logger = new Log4NetLogger();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MailSender"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public SmsSender(ILogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Sends the mail asynchronously.
        /// </summary>
        /// <param name="message">The message to send.</param>
        public void Send(INotification message)
        {
            try
            {
                var msg = message;
                //gateway.Send(msg);
                logger.Information(FormatMessage(msg));
            }
            catch (System.Exception e)
            {
                logger.Error("SMS exception", e);
                throw new NotifyException("SMS sending failed", e);
            }
        }

        /// <summary>
        /// Formats the mail message.
        /// </summary>
        /// <param name="message">The mail message.</param>
        /// <returns>Formatted message.</returns>
        private string FormatMessage(INotification message)
        {
            return String.Format("SMS sent to: {0}\nBody: {1}", message.ToAddress, message.Body);
        }
    }
}