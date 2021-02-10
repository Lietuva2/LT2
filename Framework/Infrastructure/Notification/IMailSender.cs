using System.Net.Mail;

namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// Represents generic email sender.
    /// </summary>
    public interface IMailSender
    {
        void Send(MailMessage message);
    }
}