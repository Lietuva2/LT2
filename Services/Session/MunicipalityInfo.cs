using System.Collections.Generic;
using Data.ViewModels.Account;
using Framework.Enums;

namespace Services.Session
{
    /// <summary>
    /// User information.
    /// </summary>
    public class MunicipalityInfo
    {
        /// <summary>
        /// Gets or sets the user ID.
        /// </summary>
        /// <value>The user ID.</value>
        public int Id { get; set; }

        public string Name { get; set; }

        public string ShortName { get; set; }

        public MunicipalityInfo()
        {
        }
    }
}
