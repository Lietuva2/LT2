<%@ Control Language="C#" Inherits="System.Web.Mvc.ViewUserControl<TimeSpan?>" %>
<%=Html.TextBox("", Model.HasValue ? Model.Value.ToString("g").Substring(0, Model.Value.ToString("g").LastIndexOf(":")).PadLeft(5,'0') : string.Empty, new { @class = "time" })%>
