// <auto-generated />
// This file was generated by a T4 template.
// Don't change it directly as your change would get overwritten.  Instead, make changes
// to the .tt file (i.e. the T4 template) and save it to regenerate this file.

// Make sure the compiler doesn't complain about missing Xml comments and CLS compliance
// 0108: suppress "Foo hides inherited member Foo. Use the new keyword if hiding was intended." when a controller and its abstract parent are both processed
// 0114: suppress "Foo.BarController.Baz()' hides inherited member 'Qux.BarController.Baz()'. To make the current member override that implementation, add the override keyword. Otherwise add the new keyword." when an action (with an argument) overrides an action in a parent controller
#pragma warning disable 1591, 3008, 3009, 0108, 0114
#region T4MVC

using System;
using System.Diagnostics;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.Mvc.Ajax;
using System.Web.Mvc.Html;
using System.Web.Routing;
using T4MVC;
namespace Web.Controllers
{
    public partial class FileController
    {
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public FileController() { }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected FileController(Dummy d) { }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected RedirectToRouteResult RedirectToAction(ActionResult result)
        {
            var callInfo = result.GetT4MVCResult();
            return RedirectToRoute(callInfo.RouteValueDictionary);
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected RedirectToRouteResult RedirectToAction(Task<ActionResult> taskResult)
        {
            return RedirectToAction(taskResult.Result);
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected RedirectToRouteResult RedirectToActionPermanent(ActionResult result)
        {
            var callInfo = result.GetT4MVCResult();
            return RedirectToRoutePermanent(callInfo.RouteValueDictionary);
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected RedirectToRouteResult RedirectToActionPermanent(Task<ActionResult> taskResult)
        {
            return RedirectToActionPermanent(taskResult.Result);
        }

        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult GetProfilePicture()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetProfilePicture);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult GetProfilePictureThumb()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetProfilePictureThumb);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult GetOrganizationLogo()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetOrganizationLogo);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult GetOrganizationLogoThumb()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetOrganizationLogoThumb);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult GetUserPicture()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetUserPicture);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult GetOrganizationPicture()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetOrganizationPicture);
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public FileController Actions { get { return MVC.File; } }
        [GeneratedCode("T4MVC", "2.0")]
        public readonly string Area = "";
        [GeneratedCode("T4MVC", "2.0")]
        public readonly string Name = "File";
        [GeneratedCode("T4MVC", "2.0")]
        public const string NameConst = "File";

        static readonly ActionNamesClass s_actions = new ActionNamesClass();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionNamesClass ActionNames { get { return s_actions; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionNamesClass
        {
            public readonly string GetProfilePicture = "GetProfilePicture";
            public readonly string GetProfilePictureThumb = "GetProfilePictureThumb";
            public readonly string GetOrganizationLogo = "GetOrganizationLogo";
            public readonly string GetOrganizationLogoThumb = "GetOrganizationLogoThumb";
            public readonly string GetEmptyThumb = "GetEmptyThumb";
            public readonly string GetUserPicture = "GetUserPicture";
            public readonly string GetOrganizationPicture = "GetOrganizationPicture";
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionNameConstants
        {
            public const string GetProfilePicture = "GetProfilePicture";
            public const string GetProfilePictureThumb = "GetProfilePictureThumb";
            public const string GetOrganizationLogo = "GetOrganizationLogo";
            public const string GetOrganizationLogoThumb = "GetOrganizationLogoThumb";
            public const string GetEmptyThumb = "GetEmptyThumb";
            public const string GetUserPicture = "GetUserPicture";
            public const string GetOrganizationPicture = "GetOrganizationPicture";
        }


        static readonly ActionParamsClass_GetProfilePicture s_params_GetProfilePicture = new ActionParamsClass_GetProfilePicture();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_GetProfilePicture GetProfilePictureParams { get { return s_params_GetProfilePicture; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_GetProfilePicture
        {
            public readonly string userObjectId = "userObjectId";
        }
        static readonly ActionParamsClass_GetProfilePictureThumb s_params_GetProfilePictureThumb = new ActionParamsClass_GetProfilePictureThumb();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_GetProfilePictureThumb GetProfilePictureThumbParams { get { return s_params_GetProfilePictureThumb; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_GetProfilePictureThumb
        {
            public readonly string userObjectId = "userObjectId";
        }
        static readonly ActionParamsClass_GetOrganizationLogo s_params_GetOrganizationLogo = new ActionParamsClass_GetOrganizationLogo();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_GetOrganizationLogo GetOrganizationLogoParams { get { return s_params_GetOrganizationLogo; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_GetOrganizationLogo
        {
            public readonly string objectId = "objectId";
        }
        static readonly ActionParamsClass_GetOrganizationLogoThumb s_params_GetOrganizationLogoThumb = new ActionParamsClass_GetOrganizationLogoThumb();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_GetOrganizationLogoThumb GetOrganizationLogoThumbParams { get { return s_params_GetOrganizationLogoThumb; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_GetOrganizationLogoThumb
        {
            public readonly string objectId = "objectId";
        }
        static readonly ActionParamsClass_GetUserPicture s_params_GetUserPicture = new ActionParamsClass_GetUserPicture();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_GetUserPicture GetUserPictureParams { get { return s_params_GetUserPicture; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_GetUserPicture
        {
            public readonly string id = "id";
        }
        static readonly ActionParamsClass_GetOrganizationPicture s_params_GetOrganizationPicture = new ActionParamsClass_GetOrganizationPicture();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_GetOrganizationPicture GetOrganizationPictureParams { get { return s_params_GetOrganizationPicture; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_GetOrganizationPicture
        {
            public readonly string id = "id";
        }
        static readonly ViewsClass s_views = new ViewsClass();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ViewsClass Views { get { return s_views; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ViewsClass
        {
            static readonly _ViewNamesClass s_ViewNames = new _ViewNamesClass();
            public _ViewNamesClass ViewNames { get { return s_ViewNames; } }
            public class _ViewNamesClass
            {
            }
        }
    }

    [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
    public partial class T4MVC_FileController : Web.Controllers.FileController
    {
        public T4MVC_FileController() : base(Dummy.Instance) { }

        [NonAction]
        partial void GetProfilePictureOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string userObjectId);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetProfilePicture(string userObjectId)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetProfilePicture);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "userObjectId", userObjectId);
            GetProfilePictureOverride(callInfo, userObjectId);
            return callInfo;
        }

        [NonAction]
        partial void GetProfilePictureThumbOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string userObjectId);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetProfilePictureThumb(string userObjectId)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetProfilePictureThumb);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "userObjectId", userObjectId);
            GetProfilePictureThumbOverride(callInfo, userObjectId);
            return callInfo;
        }

        [NonAction]
        partial void GetOrganizationLogoOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string objectId);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetOrganizationLogo(string objectId)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetOrganizationLogo);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "objectId", objectId);
            GetOrganizationLogoOverride(callInfo, objectId);
            return callInfo;
        }

        [NonAction]
        partial void GetOrganizationLogoThumbOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string objectId);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetOrganizationLogoThumb(string objectId)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetOrganizationLogoThumb);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "objectId", objectId);
            GetOrganizationLogoThumbOverride(callInfo, objectId);
            return callInfo;
        }

        [NonAction]
        partial void GetEmptyThumbOverride(T4MVC_System_Web_Mvc_ActionResult callInfo);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetEmptyThumb()
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetEmptyThumb);
            GetEmptyThumbOverride(callInfo);
            return callInfo;
        }

        [NonAction]
        partial void GetUserPictureOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string id);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetUserPicture(string id)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetUserPicture);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "id", id);
            GetUserPictureOverride(callInfo, id);
            return callInfo;
        }

        [NonAction]
        partial void GetOrganizationPictureOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string id);

        [NonAction]
        public override System.Web.Mvc.ActionResult GetOrganizationPicture(string id)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.GetOrganizationPicture);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "id", id);
            GetOrganizationPictureOverride(callInfo, id);
            return callInfo;
        }

    }
}

#endregion T4MVC
#pragma warning restore 1591, 3008, 3009, 0108, 0114