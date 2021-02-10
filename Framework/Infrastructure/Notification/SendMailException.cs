using System;
using System.Runtime.Serialization;

namespace Framework.Infrastructure.Notification
{
    /// <summary>
    /// The send email exception.
    /// </summary>
    public class NotifyException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotifyException"/> class.
        /// </summary>
        public NotifyException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotifyException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public NotifyException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotifyException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public NotifyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotifyException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"/> that contains contextual information about the source or destination.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// The <paramref name="info"/> parameter is null.
        /// </exception>
        /// <exception cref="T:System.Runtime.Serialization.SerializationException">
        /// The class name is null or <see cref="P:System.Exception.HResult"/> is zero (0).
        /// </exception>
        public NotifyException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}