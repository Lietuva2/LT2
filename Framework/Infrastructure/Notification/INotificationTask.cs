using System.Net.Mail;

namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// Represents generic mailing task.
    /// </summary>
    public interface INotificationTask
    {
        /// <summary>
        /// Executes current task.
        /// </summary>
        MailMessage Execute(params string[] args);
    }
}