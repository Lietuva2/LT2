using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Account
{
    public class SettingsModel
    {
        public string UserObjectId { get; set; }
        public Data.MongoDB.Settings Settings { get; set; }
    }
}
