using System;

namespace Framework.Data.Exceptions
{
    /// <summary>
    /// SQL server delete conflict exception.
    /// </summary>
    [Serializable]
    public class SqlDeleteConflictException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SqlDeleteConflictException"/> class.
        /// </summary>
        /// <param name="originalException">The original exception.</param>
        public SqlDeleteConflictException(Exception originalException)
            : base(originalException.Message, originalException)
        {
        }
    }
}
