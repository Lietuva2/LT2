using System;
using Framework.Infrastructure.Logging;
using Ninject;

namespace Framework.Bus
{
    public abstract class BaseHandler
    {
        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        /// <value>The logger.</value>
        [Inject]
        public ILogger Logger { get; set; }
    }
}
