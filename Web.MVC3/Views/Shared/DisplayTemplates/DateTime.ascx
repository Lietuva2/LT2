<%@ Control Language="C#" Inherits="System.Web.Mvc.ViewUserControl" %>
<%@ Import Namespace="Web.Helpers" %>
<%var date = (DateTime)Model; %>
<%=Html.h(date.ToShortDateString()) %>
