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
    public partial class SponsorController
    {
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public SponsorController() { }

        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        protected SponsorController(Dummy d) { }

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
        public virtual System.Web.Mvc.ActionResult Index()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Index);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult ImportBankAccountExcel()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.ImportBankAccountExcel);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult UpdateRelatedUser()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.UpdateRelatedUser);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult UpdateOperation()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.UpdateOperation);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult Donate()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Donate);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult Accept()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Accept);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult Cancel()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Cancel);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult Callback()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Callback);
        }
        [NonAction]
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public virtual System.Web.Mvc.ActionResult SaveGift()
        {
            return new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.SaveGift);
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
        public SponsorController Actions { get { return MVC.Sponsor; } }
        [GeneratedCode("T4MVC", "2.0")]
        public readonly string Area = "";
        [GeneratedCode("T4MVC", "2.0")]
        public readonly string Name = "Sponsor";
        [GeneratedCode("T4MVC", "2.0")]
        public const string NameConst = "Sponsor";

        static readonly ActionNamesClass s_actions = new ActionNamesClass();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionNamesClass ActionNames { get { return s_actions; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionNamesClass
        {
            public readonly string Index = "Index";
            public readonly string About = "About";
            public readonly string ImportBankAccountExcel = "ImportBankAccountExcel";
            public readonly string UpdateRelatedUser = "UpdateRelatedUser";
            public readonly string UpdateOperation = "UpdateOperation";
            public readonly string Donate = "Donate";
            public readonly string PaypalAccept = "PaypalAccept";
            public readonly string Accept = "Accept";
            public readonly string Cancel = "Cancel";
            public readonly string Callback = "Callback";
            public readonly string SaveGift = "SaveGift";
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
            public const string About = "About";
            public const string ImportBankAccountExcel = "ImportBankAccountExcel";
            public const string UpdateRelatedUser = "UpdateRelatedUser";
            public const string UpdateOperation = "UpdateOperation";
            public const string Donate = "Donate";
            public const string PaypalAccept = "PaypalAccept";
            public const string Accept = "Accept";
            public const string Cancel = "Cancel";
            public const string Callback = "Callback";
            public const string SaveGift = "SaveGift";
            public const string RedirectToSuccessAction = "RedirectToSuccessAction";
            public const string RedirectToFailureAction = "RedirectToFailureAction";
            public const string SaveAndRedirect = "SaveAndRedirect";
            public const string DeleteAndRedirect = "DeleteAndRedirect";
            public const string Back = "Back";
        }


        static readonly ActionParamsClass_Index s_params_Index = new ActionParamsClass_Index();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_Index IndexParams { get { return s_params_Index; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_Index
        {
            public readonly string pageNumber = "pageNumber";
            public readonly string accountId = "accountId";
            public readonly string webToPayPaging = "webToPayPaging";
            public readonly string pageSize = "pageSize";
        }
        static readonly ActionParamsClass_ImportBankAccountExcel s_params_ImportBankAccountExcel = new ActionParamsClass_ImportBankAccountExcel();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_ImportBankAccountExcel ImportBankAccountExcelParams { get { return s_params_ImportBankAccountExcel; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_ImportBankAccountExcel
        {
            public readonly string userObjectId = "userObjectId";
            public readonly string hpf = "hpf";
        }
        static readonly ActionParamsClass_UpdateRelatedUser s_params_UpdateRelatedUser = new ActionParamsClass_UpdateRelatedUser();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_UpdateRelatedUser UpdateRelatedUserParams { get { return s_params_UpdateRelatedUser; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_UpdateRelatedUser
        {
            public readonly string itemId = "itemId";
            public readonly string userId = "userId";
        }
        static readonly ActionParamsClass_UpdateOperation s_params_UpdateOperation = new ActionParamsClass_UpdateOperation();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_UpdateOperation UpdateOperationParams { get { return s_params_UpdateOperation; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_UpdateOperation
        {
            public readonly string itemId = "itemId";
            public readonly string operation = "operation";
        }
        static readonly ActionParamsClass_Donate s_params_Donate = new ActionParamsClass_Donate();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_Donate DonateParams { get { return s_params_Donate; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_Donate
        {
            public readonly string model = "model";
        }
        static readonly ActionParamsClass_Accept s_params_Accept = new ActionParamsClass_Accept();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_Accept AcceptParams { get { return s_params_Accept; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_Accept
        {
            public readonly string model = "model";
        }
        static readonly ActionParamsClass_Cancel s_params_Cancel = new ActionParamsClass_Cancel();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_Cancel CancelParams { get { return s_params_Cancel; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_Cancel
        {
            public readonly string model = "model";
        }
        static readonly ActionParamsClass_Callback s_params_Callback = new ActionParamsClass_Callback();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_Callback CallbackParams { get { return s_params_Callback; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_Callback
        {
            public readonly string model = "model";
        }
        static readonly ActionParamsClass_SaveGift s_params_SaveGift = new ActionParamsClass_SaveGift();
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public ActionParamsClass_SaveGift SaveGiftParams { get { return s_params_SaveGift; } }
        [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
        public class ActionParamsClass_SaveGift
        {
            public readonly string orderId = "orderId";
            public readonly string giftId = "giftId";
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
                public readonly string _DonateForm = "_DonateForm";
                public readonly string About = "About";
                public readonly string Accept = "Accept";
                public readonly string Cancel = "Cancel";
                public readonly string Donate = "Donate";
                public readonly string Index = "Index";
            }
            public readonly string _DonateForm = "~/Views/Sponsor/_DonateForm.cshtml";
            public readonly string About = "~/Views/Sponsor/About.cshtml";
            public readonly string Accept = "~/Views/Sponsor/Accept.cshtml";
            public readonly string Cancel = "~/Views/Sponsor/Cancel.cshtml";
            public readonly string Donate = "~/Views/Sponsor/Donate.cshtml";
            public readonly string Index = "~/Views/Sponsor/Index.cshtml";
        }
    }

    [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
    public partial class T4MVC_SponsorController : Web.Controllers.SponsorController
    {
        public T4MVC_SponsorController() : base(Dummy.Instance) { }

        [NonAction]
        partial void IndexOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, int? pageNumber, int? accountId, bool webToPayPaging, int pageSize);

        [NonAction]
        public override System.Web.Mvc.ActionResult Index(int? pageNumber, int? accountId, bool webToPayPaging, int pageSize)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Index);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "pageNumber", pageNumber);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "accountId", accountId);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "webToPayPaging", webToPayPaging);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "pageSize", pageSize);
            IndexOverride(callInfo, pageNumber, accountId, webToPayPaging, pageSize);
            return callInfo;
        }

        [NonAction]
        partial void AboutOverride(T4MVC_System_Web_Mvc_ActionResult callInfo);

        [NonAction]
        public override System.Web.Mvc.ActionResult About()
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.About);
            AboutOverride(callInfo);
            return callInfo;
        }

        [NonAction]
        partial void ImportBankAccountExcelOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string userObjectId, System.Web.HttpPostedFileBase hpf);

        [NonAction]
        public override System.Web.Mvc.ActionResult ImportBankAccountExcel(string userObjectId, System.Web.HttpPostedFileBase hpf)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.ImportBankAccountExcel);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "userObjectId", userObjectId);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "hpf", hpf);
            ImportBankAccountExcelOverride(callInfo, userObjectId, hpf);
            return callInfo;
        }

        [NonAction]
        partial void UpdateRelatedUserOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, int itemId, int userId);

        [NonAction]
        public override System.Web.Mvc.ActionResult UpdateRelatedUser(int itemId, int userId)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.UpdateRelatedUser);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "itemId", itemId);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "userId", userId);
            UpdateRelatedUserOverride(callInfo, itemId, userId);
            return callInfo;
        }

        [NonAction]
        partial void UpdateOperationOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, int itemId, string operation);

        [NonAction]
        public override System.Web.Mvc.ActionResult UpdateOperation(int itemId, string operation)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.UpdateOperation);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "itemId", itemId);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "operation", operation);
            UpdateOperationOverride(callInfo, itemId, operation);
            return callInfo;
        }

        [NonAction]
        partial void DonateOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, Data.ViewModels.Sponsor.DonateModel model);

        [NonAction]
        public override System.Web.Mvc.ActionResult Donate(Data.ViewModels.Sponsor.DonateModel model)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Donate);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "model", model);
            DonateOverride(callInfo, model);
            return callInfo;
        }

        [NonAction]
        partial void PaypalAcceptOverride(T4MVC_System_Web_Mvc_ActionResult callInfo);

        [NonAction]
        public override System.Web.Mvc.ActionResult PaypalAccept()
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.PaypalAccept);
            PaypalAcceptOverride(callInfo);
            return callInfo;
        }

        [NonAction]
        partial void AcceptOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, Data.ViewModels.Sponsor.WebToPayResponseModel model);

        [NonAction]
        public override System.Web.Mvc.ActionResult Accept(Data.ViewModels.Sponsor.WebToPayResponseModel model)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Accept);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "model", model);
            AcceptOverride(callInfo, model);
            return callInfo;
        }

        [NonAction]
        partial void CancelOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, Data.ViewModels.Sponsor.WebToPayResponseModel model);

        [NonAction]
        public override System.Web.Mvc.ActionResult Cancel(Data.ViewModels.Sponsor.WebToPayResponseModel model)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Cancel);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "model", model);
            CancelOverride(callInfo, model);
            return callInfo;
        }

        [NonAction]
        partial void CallbackOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, Data.ViewModels.Sponsor.WebToPayResponseModel model);

        [NonAction]
        public override System.Web.Mvc.ActionResult Callback(Data.ViewModels.Sponsor.WebToPayResponseModel model)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.Callback);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "model", model);
            CallbackOverride(callInfo, model);
            return callInfo;
        }

        [NonAction]
        partial void SaveGiftOverride(T4MVC_System_Web_Mvc_ActionResult callInfo, string orderId, int? giftId);

        [NonAction]
        public override System.Web.Mvc.ActionResult SaveGift(string orderId, int? giftId)
        {
            var callInfo = new T4MVC_System_Web_Mvc_ActionResult(Area, Name, ActionNames.SaveGift);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "orderId", orderId);
            ModelUnbinderHelpers.AddRouteValues(callInfo.RouteValueDictionary, "giftId", giftId);
            SaveGiftOverride(callInfo, orderId, giftId);
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