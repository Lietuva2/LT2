using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace Framework.Mvc
{
    public class LocalizedWebFormViewEngine : WebFormViewEngine
    {
        public override ViewEngineResult FindPartialView(ControllerContext controllerContext, string partialViewName, bool useCache)
        {
            string localizedPartialViewName = partialViewName;
            if (!string.IsNullOrEmpty(partialViewName))
                localizedPartialViewName = InsertLanguageCode(partialViewName, Thread.CurrentThread.CurrentUICulture.Name);
            var result = base.FindPartialView(controllerContext, localizedPartialViewName, useCache);

            if (result.View == null)
            {
                localizedPartialViewName = InsertLanguageCode(partialViewName, Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName);
                result = base.FindPartialView(controllerContext, localizedPartialViewName, useCache);
            }

            if (result.View == null)
                result = base.FindPartialView(controllerContext, partialViewName, useCache);

            return result;
        }

        public override ViewEngineResult FindView(ControllerContext controllerContext, string viewName, string masterName, bool useCache)
        {
            string localizedViewName = viewName;
            if (!string.IsNullOrEmpty(viewName))
                localizedViewName += "." + Thread.CurrentThread.CurrentUICulture.Name;

            var result = base.FindView(controllerContext, localizedViewName, masterName, useCache);

            if (result.View == null)
            {
                localizedViewName = viewName + "." + Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
                result = base.FindView(controllerContext, localizedViewName, masterName, useCache);
            }

            if (result.View == null)
                result = base.FindView(controllerContext, viewName, masterName, useCache);

            return result;
        }

        private string InsertLanguageCode(string view, string language)
        {
            if (view.IndexOf(".") <= 0)
            {
                return view + "." + language;
            }

            return view.Insert(view.LastIndexOf("."), "." + language);
        }
    }
}
