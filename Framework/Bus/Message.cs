using System;

namespace Framework.Bus
{
    public abstract class Message
    {
        protected Message()
        {
            MessageId = Guid.NewGuid();
            MessageDate = DateTime.Now;
        }

        /// <summary>
        /// Gets or set the command ID.
        /// </summary>
        /// <value>The command ID.</value>
        public Guid MessageId { get; set; }
        public DateTime MessageDate { get; set; }
    }
}
