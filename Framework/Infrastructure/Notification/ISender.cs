namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// Represents generic email sender.
    /// </summary>
    public interface ISender
    {
        /// <summary>
        /// Sends the mail.
        /// </summary>
        /// <param name="message">The message to send.</param>
        void Send(INotification message);
    }
}