using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Services.ModelServices;
using Web.Infrastructure.Attributes;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class ChatController : SiteBaseServiceController<ChatService>
    {
        [Authorize]
        public virtual ActionResult Index()
        {
            var model = Service.GetIndexModel();
            return View(model);
        }
    }
}
