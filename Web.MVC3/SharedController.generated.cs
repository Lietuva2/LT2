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
namespace T4MVC
{
    public class SharedController
    {

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
                public readonly string _Help = "_Help";
                public readonly string _Layout = "_Layout";
                public readonly string _Layout_NoMobile = "_Layout.NoMobile";
                public readonly string _LayoutFront = "_LayoutFront";
                public readonly string _LayoutFront_NoMobile = "_LayoutFront.NoMobile";
                public readonly string _Loader = "_Loader";
                public readonly string _Message = "_Message";
                public readonly string _ViewSwitcher = "_ViewSwitcher";
                public readonly string Attachment = "Attachment";
                public readonly string Dialog = "Dialog";
                public readonly string Embed = "Embed";
                public readonly string Error = "Error";
                public readonly string ExpandableTextBig = "ExpandableTextBig";
                public readonly string ExpandableTextMedium = "ExpandableTextMedium";
                public readonly string ExpandableTextSmall = "ExpandableTextSmall";
                public readonly string InvitedUser = "InvitedUser";
                public readonly string LikeFbPage = "LikeFbPage";
                public readonly string LoginFb = "LoginFb";
                public readonly string MailMessage = "MailMessage";
                public readonly string Municipalities = "Municipalities";
                public readonly string Share = "Share";
                public readonly string SimpleList = "SimpleList";
                public readonly string SimpleListContainer = "SimpleListContainer";
                public readonly string Subscribe = "Subscribe";
                public readonly string UnauthorizedError = "UnauthorizedError";
                public readonly string UserList = "UserList";
                public readonly string UserListContainer = "UserListContainer";
                public readonly string WebSite = "WebSite";
            }
            public readonly string _Help = "~/Views/Shared/_Help.cshtml";
            public readonly string _Layout = "~/Views/Shared/_Layout.cshtml";
            public readonly string _Layout_NoMobile = "~/Views/Shared/_Layout.NoMobile.cshtml";
            public readonly string _LayoutFront = "~/Views/Shared/_LayoutFront.cshtml";
            public readonly string _LayoutFront_NoMobile = "~/Views/Shared/_LayoutFront.NoMobile.cshtml";
            public readonly string _Loader = "~/Views/Shared/_Loader.cshtml";
            public readonly string _Message = "~/Views/Shared/_Message.cshtml";
            public readonly string _ViewSwitcher = "~/Views/Shared/_ViewSwitcher.cshtml";
            public readonly string Attachment = "~/Views/Shared/Attachment.cshtml";
            public readonly string Dialog = "~/Views/Shared/Dialog.cshtml";
            public readonly string Embed = "~/Views/Shared/Embed.cshtml";
            public readonly string Error = "~/Views/Shared/Error.cshtml";
            public readonly string ExpandableTextBig = "~/Views/Shared/ExpandableTextBig.cshtml";
            public readonly string ExpandableTextMedium = "~/Views/Shared/ExpandableTextMedium.cshtml";
            public readonly string ExpandableTextSmall = "~/Views/Shared/ExpandableTextSmall.cshtml";
            public readonly string InvitedUser = "~/Views/Shared/InvitedUser.cshtml";
            public readonly string LikeFbPage = "~/Views/Shared/LikeFbPage.cshtml";
            public readonly string LoginFb = "~/Views/Shared/LoginFb.cshtml";
            public readonly string MailMessage = "~/Views/Shared/MailMessage.cshtml";
            public readonly string Municipalities = "~/Views/Shared/Municipalities.cshtml";
            public readonly string Share = "~/Views/Shared/Share.cshtml";
            public readonly string SimpleList = "~/Views/Shared/SimpleList.cshtml";
            public readonly string SimpleListContainer = "~/Views/Shared/SimpleListContainer.cshtml";
            public readonly string Subscribe = "~/Views/Shared/Subscribe.cshtml";
            public readonly string UnauthorizedError = "~/Views/Shared/UnauthorizedError.cshtml";
            public readonly string UserList = "~/Views/Shared/UserList.cshtml";
            public readonly string UserListContainer = "~/Views/Shared/UserListContainer.cshtml";
            public readonly string WebSite = "~/Views/Shared/WebSite.cshtml";
            static readonly _DisplayTemplatesClass s_DisplayTemplates = new _DisplayTemplatesClass();
            public _DisplayTemplatesClass DisplayTemplates { get { return s_DisplayTemplates; } }
            [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
            public partial class _DisplayTemplatesClass
            {
                public readonly string DateTime = "DateTime";
            }
            static readonly _EditorTemplatesClass s_EditorTemplates = new _EditorTemplatesClass();
            public _EditorTemplatesClass EditorTemplates { get { return s_EditorTemplates; } }
            [GeneratedCode("T4MVC", "2.0"), DebuggerNonUserCode]
            public partial class _EditorTemplatesClass
            {
                public readonly string DateTime = "DateTime";
                public readonly string TimeSpan = "TimeSpan";
            }
        }
    }

}

#endregion T4MVC
#pragma warning restore 1591, 3008, 3009, 0108, 0114
