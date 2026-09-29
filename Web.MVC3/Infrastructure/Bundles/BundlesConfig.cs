using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Optimization;

namespace Web.Infrastructure.Bundles
{
    public static class BundleConfig
    {
        public static string[] CommonJsFiles = new[]
                                               {
                                                       Links.Scripts.Custom.jquery_custom_js,
                                                       Links.Scripts.Custom.custom_js,
                                                       Links.Scripts.jquery_endless_scroll_js,
                                                       Links.Scripts.ui_dropdownchecklist_js,
                                                       Links.Scripts.Custom.jquery_custom_voting_js,
                                                       Links.Scripts.Custom.openid_jquery_js,
                                                       Links.Scripts.Custom.openid_lt_js,
                                                       Links.Scripts.Custom.jquery_custom_read_js,
                                                       Links.Scripts.jquery_qtip_js,
                                                       Links.Scripts.jquery_titlealert_js,
                                                       Links.Scripts.jquery_stickem_js,
                                                       Links.Scripts.jquery_ui_totop_js
                                               };

        public static string[] ExtraJsFiles = new[]
                                               {
                                                   Links.Scripts.jquery_form_js,
                                                   Links.Scripts.jquery_inputmask_js,
                                                   Links.Scripts.jquery_supertextarea_js,
                                                   Links.Scripts.Globalization.jquery_ui_datepicker_lt_js,
                                                   Links.Scripts.Custom.jquery_custom_comments_js,
                                                   Links.Scripts.Custom.jquery_custom_edit_js,
                                                   Links.Scripts.Custom.jquery_custom_members_js,
                                                   Links.Scripts.Custom.jquery_custom_project_js,
                                                   Links.Scripts.Custom.jquery_custom_section_js,
                                                   Links.Scripts.underscore_js,
                                                   Links.Scripts.Custom.jquery_preview_full_js,
                                                   Links.Scripts.jquery_autosize_js,
                                                   Links.Scripts.jquery_cookie_js,
                                                   Links.Scripts.chosen_jquery_js
                                               };

        public static string[] JsLibraries = new[]
                                               {
                                                       Links.Scripts.jquery_1_8_3_js,
                                                       Links.Scripts.jquery_ui_1_8_23_js,
                                                       Links.Scripts.jquery_validate_js,
                                                       Links.Scripts.jquery_validate_unobtrusive_js,
                                                       Links.Scripts.md5_js
                                               };

        public static string[] CommonCssFiles = new[]
                                                    {
                                                        Links.Content.CSS.screen_css,
                                                        Links.Content.CSS.jquery_ui_1_8_11_css,
                                                        Links.Content.CSS.site_css,
                                                        Links.Content.CSS.openid_css,
                                                        Links.Content.CSS.jquery_qtip_css,
                                                        Links.Content.CSS.ui_dropdownchecklist_themeroller_css,
                                                        Links.Content.CSS.chat_css,
                                                        Links.Content.CSS.chosen_css,
                                                        Links.Content.CSS.ui_totop_css
                                                    };

        public static void RegisterBundles(BundleCollection bundles)
        {
            //bundles.Add(new DynamicFolderBundle("css", new CssMinify(), "*.css"));
            bundles.Add(GetStyleBundle("~/Content/CSS/css", CommonCssFiles));

            bundles.Add(GetScriptBundle("~/site/js", CommonJsFiles));
            bundles.Add(GetScriptBundle("~/extra/js", ExtraJsFiles));
            bundles.Add(GetScriptBundle("~/chat/js", new[] { Links.Scripts.Custom.jquery_chat_js }));
            bundles.Add(GetUnminifiedBundle("~/libraries/js", JsLibraries));
        }

        private static StyleBundle GetStyleBundle(string bundleName, params string[] cssFiles)
        {
            StyleBundle cssBundle = new StyleBundle(bundleName);

            foreach (var cssFile in cssFiles)
            {
                cssBundle.Include(cssFile.ToAppRelativeUrl());
            }
            cssBundle.Orderer = new CustomOrderer();
            return cssBundle;
        }

        private static ScriptBundle GetScriptBundle(string bundleName, params string[] jsFiles)
        {
            ScriptBundle jsBundle = new ScriptBundle(bundleName);

            foreach (var jsFile in jsFiles)
            {
                jsBundle.Include(jsFile.ToAppRelativeUrl());
            }
            jsBundle.Orderer = new CustomOrderer();
            return jsBundle;
        }

        private static Bundle GetUnminifiedBundle(string bundleName, params string[] files)
        {
            var bundle = new Bundle(bundleName);

            foreach (var file in files)
            {
                bundle.Include(file.ToAppRelativeUrl());
            }
            bundle.Orderer = new CustomOrderer();
            return bundle;
        }

        private static string ToAppRelativeUrl(this string url)
        {
            // create an absolute path for the application root
            return VirtualPathUtility.ToAppRelative(url);
        }
    }

    public class CustomOrderer : IBundleOrderer
    {
        public IEnumerable<BundleFile> OrderFiles(BundleContext context, IEnumerable<BundleFile> files)
        {
            return files.ToList();
        }
    }
}