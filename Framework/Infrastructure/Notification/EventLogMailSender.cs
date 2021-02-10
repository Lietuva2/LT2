using System;
using System.Diagnostics;
using System.Security;
using System.Text;

namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// Writes emails to Event Log.
    /// </summary>
    public class EventLogSender : ISmsSender
    {
        /// <summary>
        /// Log name.
        /// </summary>
        public const string LogName = "NDNTSms";

        /// <summary>
        /// Sends the mail.
        /// </summary>
        /// <param name="message">The message to send.</param>
        public void Send(INotification message)
        {
            var sb = new StringBuilder();
            sb.AppendLine("To: " + message.ToAddress);
            sb.AppendLine("Body: " + message.Body);

            var log = new EventLog(LogName) {Source = LogName};
            log.WriteEntry(sb.ToString(), EventLogEntryType.Information);
        }
    }
}