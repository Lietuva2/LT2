using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Data.Enums;
using Framework.Infrastructure.Storage;
using Framework.Lists;

namespace Data.ViewModels.Base
{
    public class UserListContainerModel
    {
        public ExpandableList<UserListModel> List { get; set; }
        public ActionResult ActionResult { get; set; }
    }
}