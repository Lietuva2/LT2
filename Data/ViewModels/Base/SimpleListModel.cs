using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Data.Enums;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Base
{
    public class SimpleListModel
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public DateTime Date { get; set; }
        public EntryTypes Type { get; set; }
    }
}