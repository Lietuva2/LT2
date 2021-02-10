using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Data.Enums;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Base
{
    public class UserListModel
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string PersonCode { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsPublic { get; set; }
        public string SetIsPublicUrl { get; set; }
        public DateTime? Date { get; set; }
    }
}