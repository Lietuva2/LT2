using System;
using System.ServiceProcess;
using System.Threading;
using Framework.Infrastructure.Logging;
using log4net;
using log4net.Config;

namespace Scheduler.Base
{
    /// <summary>
    /// Serves as a base class for schedulers. Schedulers are implemented as Windows services.
    /// </summary>
    /// <typeparam name="T">Type.</typeparam>
    public class SchedulerBase<T> : ServiceBase
        where T : class
    {
        /// <summary>
        /// Default time span between executions.
        /// </summary>
        public const int DefaultTimeSpan = 600000;

        /// <summary>
        /// Scheduler logger.
        /// </summary>
        protected static readonly ILogger Log = Log4NetLogger.Configure();

        /// <summary>
        /// Mutext - if sheduler is working.
        /// </summary>
        private object isWorking; // mutex

        /// <summary>
        /// Timer.
        /// </summary>
        private Timer timer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerBase"/> class.
        /// </summary>
        public SchedulerBase()
            : base()
        {            
            isWorking = null;
            this.CanPauseAndContinue = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerBase"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        public SchedulerBase(string name)
            : this()
        {
            this.ServiceName = name;            
        }

        /// <summary>
        /// Gets the time span.
        /// </summary>
        /// <value>The time span.</value>
        public virtual int TimeSpan
        {
            get
            {
                return DefaultTimeSpan;
            }
        }

        /// <summary>
        /// When implemented in a derived class, executes when a Start command is sent to the service by the Service Control Manager (SCM) or when the operating system starts (for a service that starts automatically). Specifies actions to take when the service starts.
        /// </summary>
        /// <param name="args">Data passed by the start command.</param>
        protected override void OnStart(string[] args)
        {
            base.OnStart(args);
            Log.Information("Starting the service...");
            var timerCallback = new TimerCallback(EnterMutex);            
            timer = new Timer(timerCallback, isWorking, 0, TimeSpan);
        }

        /// <summary>
        /// When implemented in a derived class, executes when a Stop command is sent to the service by the Service Control Manager (SCM). Specifies actions to take when a service stops running.
        /// </summary>
        protected override void OnStop()
        {
            base.OnStop();
            Log.Information("Stopping the service...");

            // cleanup
            LogManager.Shutdown();
            if (timer != null)
            {
                timer.Dispose();
            }
        }

        /// <summary>
        /// Enters the mutex. Checks if job is not currently executed and starts a new one.
        /// Otherwise, returns.
        /// </summary>
        /// <param name="state">The state.</param>
        protected virtual void EnterMutex(object state)
        {
            if (isWorking == null)
            {
                isWorking = new object();

                try
                {                 
                    Execute();                    
                }
                finally
                {
                    isWorking = null;
                }
            }
        }

        /// <summary>
        /// Executes this instance.
        /// </summary>
        protected virtual void Execute()
        {
            //// execute logic goes there
        }
    }
}
