using System.Configuration.Install;
using System.ServiceProcess;

namespace Scheduler.Base
{
    /// <summary>
    /// Server as a base class for scheduler installers as Windows Services.
    /// </summary>
    public class SchedulerInstallerBase : Installer
    {
        /// <summary>
        /// Service process installer.
        /// </summary>
        private ServiceProcessInstaller serviceProcessInstaller;
        
        /// <summary>
        /// Service installer.
        /// </summary>
        private ServiceInstaller serviceInstaller;

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerInstallerBase"/> class.
        /// </summary>
        /// <param name="name">The name of sheduler.</param>
        public SchedulerInstallerBase(string name)
            : base()
        {    
            this.serviceProcessInstaller = new ServiceProcessInstaller();
            this.serviceInstaller = new ServiceInstaller();

            this.serviceProcessInstaller.Account = ServiceAccount.LocalSystem;
            this.serviceProcessInstaller.Password = null;
            this.serviceProcessInstaller.Username = null;            

            this.serviceInstaller.DisplayName =
                this.serviceInstaller.ServiceName = name;

            this.serviceInstaller.StartType = ServiceStartMode.Automatic;
            
            this.Installers.AddRange(new Installer[] 
                {
                    this.serviceProcessInstaller,
                    this.serviceInstaller
                });
        }         
    }
}
