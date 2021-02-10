using System.Linq;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Organization;
using Data.ViewModels.Project;
using Framework.Mvc.Filters;
using Framework.Strings;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

//Controller for a Voting
namespace Web.Controllers
{
    [ValidateAntiForgeryTokenWrapper(HttpVerbs.Post)]
    public partial class ProjectController : SiteBaseServiceController<ProjectService>
    {
        public ProjectController()
        {
        }

        public virtual ActionResult ToDos(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                return HttpNotFound();
            }

            var model = Service.ToDos(projectId);
            var cnt = model.MileStones.Select(m => m.ToDos).Count();
            return View(model);
        }

        public virtual ActionResult AddToDo(ProjectToDosModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(model.Id));
            }

            if (ModelState.IsValid)
            {
                var item = Service.AddToDo(model);
                return
                    Json(
                        new
                            {
                                Content = RenderPartialViewToString(MVC.Project.Views.ToDoItem, item),
                                MileStoneId = item.MileStoneId
                            });
            }

            return Json(new {errors = GetErrorMessages()});
        }

        public virtual ActionResult FinishTodo(string projectId, string milestoneId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(projectId));
            }

            var model = Service.FinishToDo(projectId, milestoneId, id);
            if (model.IsFinished)
            {
                return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.FinishedToDoItem, model), IsFinished = model.IsFinished });
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.ToDoItem, model), IsFinished = model.IsFinished });
        }

        public virtual ActionResult DeleteToDo(string projectId, string milestoneId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(projectId));
            }

            var success = Service.DeleteToDo(projectId, milestoneId, id);
            return Json(success);
        }

        public virtual ActionResult GetToDoEdit(string projectId, string milestoneId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(projectId));
            }

            var model = Service.GetToDoEdit(projectId, milestoneId, id);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.EditToDoItem, model) });
        }

        public virtual ActionResult EditToDo(EditToDoModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(model.ProjectId));
            }

            var viewModel = Service.EditToDo(model);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.ToDoItem, viewModel) });
        }

        public virtual ActionResult ReorderToDos(string projectId, string milestoneId, int startPos, int endPos)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(projectId));
            }

            Service.ReorderToDos(projectId, milestoneId, startPos, endPos);
            return Json(true);
        }

        [HttpGet]
        public virtual ActionResult Comments(string projectId, string milestoneId, string toDoId)
        {
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(toDoId))
            {
                return HttpNotFound();
            }

            var model = Service.GetComments(projectId, milestoneId, toDoId);
            var tempModel = TempData["TempModel"] as CommentsModel;
            if (tempModel != null)
            {
                model.InsertComment = tempModel.InsertComment;
                model.InsertIsPrivate = tempModel.InsertIsPrivate;
                model.InsertSendNotifications = tempModel.InsertSendNotifications;
            }

            return View(model);
        }

        public virtual ActionResult AddComment(CommentsModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.Comments(model.ToDo.ProjectId, model.ToDo.MileStoneId, model.ToDo.ToDoId));
            }

            var view = Service.AddComment(model.ToDo.ProjectId, model.ToDo.MileStoneId, model.ToDo.ToDoId, model.InsertComment, model.InsertIsPrivate, model.InsertSendNotifications, model.Attachments);

            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.Comment, view) });
        }

        public virtual ActionResult SaveTempModel(CommentsModel model)
        {
            TempData["TempModel"] = model;
            return Json(true);
        }

        public virtual ActionResult DeleteComment(string projectId, string milestoneId, string toDoId, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.Comments(projectId, milestoneId, toDoId));
            }

            var result = Service.DeleteComment(projectId, milestoneId, toDoId, commentId);
            return Json(result);
        }

        [HttpGet]
        public virtual ActionResult Team(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                return HttpNotFound();
            }

            var model = Service.GetProjectTeam(projectId);
            return View(model);
        }

        public virtual ActionResult ConfirmMember(string projectId, string userId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.Team(projectId));
            }

            Service.ConfirmMember(projectId, userId);
            return Json(true);
        }

        public virtual ActionResult GetProjectMembersEdit(string projectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.Team(projectId));
            }

            var model = Service.GetProjectMembersEdit(projectId);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.ProjectMembersEdit, model) });
        }

        public virtual ActionResult CancelProjectMembersEdit(string projectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.Team(projectId));
            }

            var model = Service.GetProjectTeam(projectId);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.ProjectMembers, model) });
        }

        public virtual ActionResult SaveProjectMembers(ProjectTeamModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.Team(model.Id));
            }

            var result = Service.SaveProjectMembers(model);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.ProjectMembers, result) });
        }

        [HttpGet]
        public virtual ActionResult MileStones(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                return HttpNotFound();
            }

            var model = Service.MileStones(projectId);
            return View(model);
        }

        public virtual ActionResult AddMileStone(MileStoneEditModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.MileStones(model.Id));
            }

            if (ModelState.IsValid)
            {
                var item = Service.AddMileStone(model);
                return Json(new {Content = RenderPartialViewToString(MVC.Project.Views.MileStoneItem, item)});
            }

            return Json(new { errors = GetErrorMessages() });
        }

        public virtual ActionResult DeleteMileStone(string projectId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.MileStones(projectId));
            }

            var success = Service.DeleteMileStone(projectId, id);
            return Json(success);
        }

        public virtual ActionResult GetMileStoneEdit(string projectId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.MileStones(projectId));
            }

            var model = Service.GetMileStoneEdit(projectId, id);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.EditMileStoneItem, model) });
        }

        public virtual ActionResult EditMileStone(EditMileStoneModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.MileStones(model.Id));
            }

            var viewModel = Service.EditMileStone(model);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.MileStoneItem, viewModel) });
        }

        [ImportModelStateFromTempData]
        [HttpGet]
        public virtual ActionResult Settings(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                return HttpNotFound();
            }

            var model = Service.GetSettings(projectId);
            return View(model);
        }

        [ExportModelStateToTempData]
        [HttpPost]
        public virtual ActionResult Settings(SettingsModel model)
        {
            if (ModelState.IsValid)
            {
                Service.SaveSettings(model);
                return RedirectToSuccessAction(MVC.Project.Settings(model.Id));
            }

            return RedirectToAction(MVC.Project.Settings(model.Id));
        }

        public virtual ActionResult GetMyProjects(string userObjectid)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectid, null, null));
            }

            var model = Service.GetMyProjects(0, userObjectid);
            model.ActionResult = MVC.Project.GetNextMyProjectsPage(null, userObjectid);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        [AjaxOnly]
        public virtual ActionResult GetNextMyProjectsPage(int? pageIndex, string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetMyProjects(pageIndex.Value, userObjectId);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, model.List.List), model.List.HasMoreElements };
            return Json(json);
        }

        [HttpPost, Framework.Mvc.Filters.Authorize]
        public virtual ActionResult AddInvitedUser(InviteUserModel model)
        {
            if (model.UserId.HasValue || !model.InvitedUser.IsNullOrEmpty())
            {
                return Json(new { Content = RenderPartialViewToString(MVC.Shared.Views.InvitedUser, model) });
            }

            return Json(null);
        }

        [HttpPost, Framework.Mvc.Filters.Authorize]
        public virtual ActionResult InviteUsers(ProjectTeamModel model)
        {
            var result = Service.InviteUsers(model);
            return RedirectToAction(MVC.Project.Team(model.Id));
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult RemoveMember(string projectId, string memberId)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Project.Team(projectId));
            }

            var result = Service.RemoveMember(projectId, memberId);
            return Json(result);
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult ReInvite(string projectId, string userEmail)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Project.Team(projectId));
            }

            var result = Service.InviteUser(projectId, userEmail);
            return Json(result);
        }

        [HttpPost]
        public virtual ActionResult AddOrganizationMembers(string projectId)
        {
            var result = Service.AddOrganizationMembers(projectId);
            return RedirectToAction(MVC.Project.Team(projectId));
        }

        public virtual ActionResult TakeToDo(string projectId, string mileStoneId, string toDoId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Project.ToDos(projectId));
            }

            var viewModel = Service.TakeToDo(projectId, mileStoneId, toDoId);
            return Json(new { Content = RenderPartialViewToString(MVC.Project.Views.ToDoItem, viewModel) });
        }
    }
}
