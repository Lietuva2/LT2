using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Sponsor;
using Framework.Enums;
using Framework.Strings;

using OfficeOpenXml;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class SponsorController : SiteBaseServiceController<SponsorService>
    {
        public virtual ActionResult Index(int? pageNumber, int? accountId, bool webToPayPaging = false, int pageSize = 20)
        {
            var model = Service.GetAccountModel(accountId.HasValue && !webToPayPaging ? pageNumber.Value : 1, accountId, webToPayPaging && pageNumber.HasValue ? pageNumber.Value : 1, pageSize);
            return View(model);
        }

        public virtual ActionResult About()
        {
            var model = Service.GetDonateModel();
            return View(MVC.Sponsor.Views.About, model);
        }

        [HttpPost]
        public virtual ActionResult ImportBankAccountExcel(string userObjectId, HttpPostedFileBase hpf)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                return RedirectToAction(MVC.Sponsor.Index());
            }

            if (hpf.ContentLength == 0)
                return Index();

            using (var package = new ExcelPackage(hpf.InputStream))
            {
                var sheet = package.Workbook.Worksheets.First();
                var model = new BankAccountModel.AccountModel();
                var list = new List<BankAccountItemModel>();
                for (int row = 18; row < sheet.Dimension.End.Row; row++)
                {
                    if(sheet.Cells[row, 1].Value == null)
                    {
                        continue;
                    }

                    long serialDate = long.Parse(sheet.Cells[row, 1].Value.ToString());
                    DateTime date = DateTime.FromOADate(serialDate);

                    list.Add(new BankAccountItemModel()
                    {
                        Date = date,
                        Operation = sheet.Cells[row, 2].Value.ToString(),
                        Expense = (double?)sheet.Cells[row, 3].Value,
                        Income = (double?)sheet.Cells[row, 4].Value
                    });
                }

                model.Items = list;
                model.AccountNo = sheet.Cells[13, 1].Value.ToString();
                model.Balance = (double)sheet.Cells[sheet.Dimension.End.Row, 4].Value;

                Service.ImportExcelData(model);
            }
            
            return RedirectToAction(MVC.Sponsor.Index());
        }

        public virtual ActionResult UpdateRelatedUser(int itemId, int userId)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                return Json(false);
            }

            return Json(Service.UpdateRelatedUser(itemId, userId));
        }

        public virtual ActionResult UpdateOperation(int itemId, string operation)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                return Json(false);
            }

            return Json(Service.UpdateOperation(itemId, operation));
        }

        [HttpPost]
        public virtual ActionResult Donate(DonateModel model)
        {
            if (!StringExtensions.IsValidLtPersonCode(model.PersonCode))
            {
                ModelState.AddModelError("PersonCode", "Neteisingas asmens kodas");
            }

            if (model.IsPersonCodeRequired && string.IsNullOrEmpty(model.PersonCode))
            {
                ModelState.AddModelError("PersonCode", "Įveskite asmens kodą");
            }

            if (ModelState.IsValid)
            {
                var result = Service.GetWebToPayModel(
                    model,
                    ConfigurationManager.AppSettings["WebToPay_Password"],
                    ConfigurationManager.AppSettings["WebToPay_ProjectId"],
                    Url.ActionAbsolute(MVC.Sponsor.Accept()),
                    Url.ActionAbsolute(MVC.Sponsor.Cancel()),
                    Url.ActionAbsolute(MVC.Sponsor.Callback()),
                    ConfigurationManager.AppSettings["WebToPay_PayText"],
                    ConfigurationManager.AppSettings["WebToPay_Test"]);

                if (!string.IsNullOrEmpty(model.PersonCode))
                {
                    Service.SavePersonCode(model.PersonCode);
                }

                return View(result);
            }

            return View(MVC.Sponsor.Views.About, model);
        }

        public virtual ActionResult PaypalAccept()
        {
            return View(MVC.Sponsor.Views.Accept);
        }

        public virtual ActionResult Accept(WebToPayResponseModel model)
        {
            if (Service.CheckResponse(model, ConfigurationManager.AppSettings["WebToPay_Password"]))
            {
                var result = Service.GetPaymentAcceptModel(model);
                if (result.PersonCodeStatus == 0)
                {
                    CurrentUser.VerificationPending = true;
                }

                return View(result);
            }

            return View(MVC.Sponsor.Views.Cancel);
        }

        public virtual ActionResult Cancel(WebToPayResponseModel model)
        {
            return View();
        }

        public virtual ActionResult Callback(WebToPayResponseModel model)
        {
            Logger.Information("Callback request: " + Request.Form);
            if (Service.CheckResponse(model, ConfigurationManager.AppSettings["WebToPay_Password"]))
            {
                Service.ProcessPayment(model);
                return Content("OK");
            }

            return Content("Error: SS1v2 validation failed");
        }

        public virtual ActionResult SaveGift(string orderId, int? giftId)
        {
            return Json(Service.SaveGift(orderId, giftId));
        }
    }
}
