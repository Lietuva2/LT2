using System.ComponentModel;
using Scheduler.Base;

namespace NDNT.EmailSender
{
    /// <summary>
    /// SVS scheduler installer.
    /// </summary>
    [RunInstaller(true)]
    public class EmailSchedulerInstaller : SchedulerInstallerBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmailSchedulerInstaller"/> class.
        /// </summary>
        public EmailSchedulerInstaller()
            : base(EmailScheduler.Name)
        {
        }
    }
}
