using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.MongoDB;
using Data.ViewModels.Base;
using Framework.Infrastructure.ValueInjections;
using Omu.ValueInjecter;

namespace Services.Classes
{
    public static class ListBinding
    {
        public static void BindTo<TSource, TTarget>(this List<TSource> source, List<TTarget> target) where TTarget : new()
        {
            BindTo(source, target, null);
        }

        public static void BindUrls(this List<UrlViewModel> source, List<Website> target)
        {
            var src = source;
            if (src != null)
            {
                src = source.Where(u => !string.IsNullOrEmpty(u.Url)).Distinct().ToList();
            }
            BindTo(src, target, item => item.Url = !item.Url.StartsWith("http") ? "http://" + item.Url : item.Url);
        }

        public static void BindUrls(this List<UrlEditModel> source, List<Website> target)
        {
            var src = source;
            if (src != null)
            {
                src = source.Where(u => !string.IsNullOrEmpty(u.Url)).Distinct().ToList();
            }
            BindTo(src, target, item => item.Url = !item.Url.StartsWith("http") ? "http://" + item.Url : item.Url);
        }

        public static void BindTo<TSource, TTarget>(this List<TSource> source, List<TTarget> target, Action<TTarget> action) where TTarget : new()
        {
            target.Clear();
            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                var itemToSave = new TTarget();
                itemToSave.InjectFrom<UniversalInjection>(item);
                action(itemToSave);

                target.Add(itemToSave);
            }
        }
    }
}
