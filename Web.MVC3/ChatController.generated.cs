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
    public partial class ChatController
    {
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ChatController() { }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected ChatController(Dummy d) { }

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
        public virtual Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToSuccessAction()
        {
            return new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToSuccessAction);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToFailureAction()
        {
            return new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToFailureAction);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult SaveAndRedirect()
        {
            return new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.SaveAndRedirect);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult DeleteAndRedirect()
        {
            return new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.DeleteAndRedirect);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult Back()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Back);
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ChatController Actions { get { return MVC.Chat; } }
        [GeneratedCode("T4MVC", "2.0")]
        public readonly string Area = "";
        [GeneratedCode("T4MVC", "2.0")]
        public readonly string Name = "Chat";
        [GeneratedCode("T4MVC", "2.0")]
        public const string NameConst = "Chat";

        static readonly ActionNamesClass s_actions = new ActionNamesClass();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionNamesClass ActionNames { get { return s_actions; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionNamesClass
        {
            public readonly string Index = "Index";
            public readonly string RedirectToSuccessAction = "RedirectToSuccessAction";
            public readonly string RedirectToFailureAction = "RedirectToFailureAction";
            public readonly string SaveAndRedirect = "SaveAndRedirect";
            public readonly string DeleteAndRedirect = "DeleteAndRedirect";
            public readonly string Back = "Back";
        }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionNameConstants
        {
            public const string Index = "Index";
            public const string RedirectToSuccessAction = "RedirectToSuccessAction";
            public const string RedirectToFailureAction = "RedirectToFailureAction";
            public const string SaveAndRedirect = "SaveAndRedirect";
            public const string DeleteAndRedirect = "DeleteAndRedirect";
            public const string Back = "Back";
        }


        static readonly ActionParamsClass_RedirectToSuccessAction s_params_RedirectToSuccessAction = new ActionParamsClass_RedirectToSuccessAction();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_RedirectToSuccessAction RedirectToSuccessActionParams { get { return s_params_RedirectToSuccessAction; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_RedirectToSuccessAction
        {
            public readonly string result = "result";
            public readonly string message = "message";
            public readonly string openDialog = "openDialog";
            public readonly string routeValueDictionary = "routeValueDictionary";
        }
        static readonly ActionParamsClass_RedirectToFailureAction s_params_RedirectToFailureAction = new ActionParamsClass_RedirectToFailureAction();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_RedirectToFailureAction RedirectToFailureActionParams { get { return s_params_RedirectToFailureAction; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_RedirectToFailureAction
        {
            public readonly string result = "result";
            public readonly string message = "message";
            public readonly string openDialog = "openDialog";
            public readonly string routeValueDictionary = "routeValueDictionary";
        }
        static readonly ActionParamsClass_SaveAndRedirect s_params_SaveAndRedirect = new ActionParamsClass_SaveAndRedirect();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_SaveAndRedirect SaveAndRedirectParams { get { return s_params_SaveAndRedirect; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_SaveAndRedirect
        {
            public readonly string save = "save";
            public readonly string result = "result";
            public readonly string routeValueDictionary = "routeValueDictionary";
        }
        static readonly ActionParamsClass_DeleteAndRedirect s_params_DeleteAndRedirect = new ActionParamsClass_DeleteAndRedirect();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_DeleteAndRedirect DeleteAndRedirectParams { get { return s_params_DeleteAndRedirect; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_DeleteAndRedirect
        {
            public readonly string delete = "delete";
            public readonly string result = "result";
            public readonly string routeValueDictionary = "routeValueDictionary";
        }
        static readonly ActionParamsClass_Back s_params_Back = new ActionParamsClass_Back();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_Back BackParams { get { return s_params_Back; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_Back
        {
            public readonly string returnTo = "returnTo";
            public readonly string defaultReturnTo = "defaultReturnTo";
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
                public readonly string Index = "Index";
            }
            public readonly string Index = "~/Views/Chat/Index.cshtml";
        }
    }

    [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
    public partial class T4MVC_ChatController : Web.Controllers.ChatController
    {
        public T4MVC_ChatController() : base(Dummy.Instance) { }

        [NonAction]
        partial void IndexOverride(T4MVC_System_Web_Mvc_ActionResult callInfo);

        [NonAction]
        public override System.Web.Mvc.ActionResult Index()
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Index);
            IndexOverride(callInfo);
            return callInfo;
        }

        [NonAction]
        partial void RedirectToSuccessActionOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Web.Mvc.ActionResult result, string message, bool openDialog);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToSuccessAction(System.Web.Mvc.ActionResult result, string message, bool openDialog)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToSuccessAction);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "result", result);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "message", message);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "openDialog", openDialog);
            RedirectToSuccessActionOverride(callInfo, result, message, openDialog);
            return callInfo;
        }

        [NonAction]
        partial void RedirectToSuccessActionOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Web.Mvc.ActionResult result, bool openDialog);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToSuccessAction(System.Web.Mvc.ActionResult result, bool openDialog)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToSuccessAction);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "result", result);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "openDialog", openDialog);
            RedirectToSuccessActionOverride(callInfo, result, openDialog);
            return callInfo;
        }

        [NonAction]
        partial void RedirectToFailureActionOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Web.Mvc.ActionResult result, string message, bool openDialog);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToFailureAction(System.Web.Mvc.ActionResult result, string message, bool openDialog)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToFailureAction);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "result", result);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "message", message);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "openDialog", openDialog);
            RedirectToFailureActionOverride(callInfo, result, message, openDialog);
            return callInfo;
        }

        [NonAction]
        partial void SaveAndRedirectOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Action save, System.Web.Mvc.ActionResult result);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult SaveAndRedirect(System.Action save, System.Web.Mvc.ActionResult result)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.SaveAndRedirect);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "save", save);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "result", result);
            SaveAndRedirectOverride(callInfo, save, result);
            return callInfo;
        }

        [NonAction]
        partial void DeleteAndRedirectOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Action delete, System.Web.Mvc.ActionResult result);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult DeleteAndRedirect(System.Action delete, System.Web.Mvc.ActionResult result)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.DeleteAndRedirect);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "delete", delete);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "result", result);
            DeleteAndRedirectOverride(callInfo, delete, result);
            return callInfo;
        }

        [NonAction]
        partial void BackOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string returnTo, string defaultReturnTo);

        [NonAction]
        public override System.Web.Mvc.ActionResult Back(string returnTo, string defaultReturnTo)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Back);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "returnTo", returnTo);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "defaultReturnTo", defaultReturnTo);
            BackOverride(callInfo, returnTo, defaultReturnTo);
            return callInfo;
        }

        [NonAction]
        partial void RedirectToSuccessActionOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Web.Routing.RouteValueDictionary routeValueDictionary, string message, bool openDialog);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToSuccessAction(System.Web.Routing.RouteValueDictionary routeValueDictionary, string message, bool openDialog)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToSuccessAction);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "routeValueDictionary", routeValueDictionary);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "message", message);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "openDialog", openDialog);
            RedirectToSuccessActionOverride(callInfo, routeValueDictionary, message, openDialog);
            return callInfo;
        }

        [NonAction]
        partial void RedirectToSuccessActionOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Web.Routing.RouteValueDictionary routeValueDictionary, bool openDialog);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToSuccessAction(System.Web.Routing.RouteValueDictionary routeValueDictionary, bool openDialog)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToSuccessAction);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "routeValueDictionary", routeValueDictionary);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "openDialog", openDialog);
            RedirectToSuccessActionOverride(callInfo, routeValueDictionary, openDialog);
            return callInfo;
        }

        [NonAction]
        partial void RedirectToFailureActionOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Web.Routing.RouteValueDictionary routeValueDictionary, string message, bool openDialog);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult RedirectToFailureAction(System.Web.Routing.RouteValueDictionary routeValueDictionary, string message, bool openDialog)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.RedirectToFailureAction);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "routeValueDictionary", routeValueDictionary);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "message", message);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "openDialog", openDialog);
            RedirectToFailureActionOverride(callInfo, routeValueDictionary, message, openDialog);
            return callInfo;
        }

        [NonAction]
        partial void SaveAndRedirectOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Action save, System.Web.Routing.RouteValueDictionary routeValueDictionary);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult SaveAndRedirect(System.Action save, System.Web.Routing.RouteValueDictionary routeValueDictionary)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.SaveAndRedirect);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "save", save);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "routeValueDictionary", routeValueDictionary);
            SaveAndRedirectOverride(callInfo, save, routeValueDictionary);
            return callInfo;
        }

        [NonAction]
        partial void DeleteAndRedirectOverride(T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult callInfo, System.Action delete, System.Web.Routing.RouteValueDictionary routeValueDictionary);

        [NonAction]
        public override Framework.Mvc.ActionResults.RedirectToRouteWithTempDataResult DeleteAndRedirect(System.Action delete, System.Web.Routing.RouteValueDictionary routeValueDictionary)
        {
            var callInfo = new T4MVC_Framework_Mvc_ActionResults_RedirectToRouteWithTempDataResult(Area, Name, ActionNames.DeleteAndRedirect);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "delete", delete);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "routeValueDictionary", routeValueDictionary);
            DeleteAndRedirectOverride(callInfo, delete, routeValueDictionary);
            return callInfo;
        }

    }
}

#endregion T4MVC
#pragma warning restore 1591, 3008, 3009, 0108, 0114
