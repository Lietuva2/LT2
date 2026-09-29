using System;
using System.Linq;
using System.Text;


namespace Framework.Infrastructure.Logging
{
    /// <summary>
    /// General purpose source agnostic logger.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs the error using specified message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Error(string message);

        /// <summary>
        /// Logs the error with related exception using the specified message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        void Error(string message, Exception exception);

        /// <summary>
        /// Logs the error with related exception.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        void Error(Exception exception);

        /// <summary>
        /// Logs the warning using specified message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Warning(string message);

        /// <summary>
        /// Logs the warning with related exception using the specified message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        void Warning(string message, Exception exception);

        /// <summary>
        /// Logs the warning with related exception.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        void Warning(Exception exception);

        /// <summary>
        /// Logs the information using specified message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Information(string message);

        /// <summary>
        /// Logs the information with related exception using the specified message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        void Information(string message, Exception exception);

        /// <summary>
        /// Logs the information with related exception.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        void Information(Exception exception);
    }
}
