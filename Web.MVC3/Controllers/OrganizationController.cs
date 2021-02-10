using System;
using System.Data;
using System.Data.Entity.Core;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Organization;
using Data.ViewModels.Organization.Project;
using Framework.Enums;
using Framework.Mvc.Filters;
using Framework.Strings;
using Globalization.Resources.Organization;
using Services.Enums;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class OrganizationController : SiteBaseServiceController<OrganizationService>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrganizationController"/> class.
        /// </summary>
        public OrganizationController()
        {
        }

        [HttpGet]
        public virtual ActionResult Index()
        {
            var model = Service.GetIndexModel();
            return View(model);
        }

        /// <summary>
        /// Detailses the specified user db id.
        /// </summary>
        /// <param name="objectId">The user object id.</param>
        /// <param name="name">The full name.</param>
        /// <param name="view">The view.</param>
        /// <param name="ideaSort">The idea sort.</param>
        /// <param name="issueSort">The issue sort.</param>
        /// <returns></returns>
        [HttpGet]
        public virtual ActionResult Details(string objectId, string name, OrganizationViews? view, IdeaListSorts? ideaSort, IssueListSorts? issueSort, ProblemListSorts? problemSort)
        {
            if (objectId.IsNullOrEmpty())
            {
                return RedirectToAction(MVC.Organization.Index());
            }

            OrganizationId = objectId;
            Session["IdeasList"] = null;
            Session["IssuesList"] = null;

            if(!view.HasValue)
            {
                view = OrganizationViews.Info;
            }

            //if (CurrentUser.Organizations != null && !(CurrentUser.Organizations.Where(o => o.OrganizationId == objectId).Select(o => o.IsMember).SingleOrDefault()))
            //{
            //    CurrentUser.Organizations = null;
            //}
            try
            {
                var userAccount = Service.GetOrganizationModel(objectId, view.Value, ideaSort, issueSort, problemSort);

                string expectedName = userAccount.Name.ToSeoUrl();
                string actualName = (name ?? "").ToLower();

                // permanently redirect to the correct URL
                if (expectedName != actualName)
                {
                    return RedirectToActionPermanent("Details", "Organization",
                                                     new
                                                         {
                                                             objectId = objectId,
                                                             name = expectedName,
                                                             view = view,
                                                             ideaSort = ideaSort,
                                                             issueSort = issueSort
                                                         });
                }

                LastListUrl = Request.Url;
                return View(MVC.Organization.Views.Details, userAccount);
            }
            catch (ObjectNotFoundException ex)
            {
                return HttpNotFound();
            }
        }

        public virtual ActionResult GetOrganizationInfo(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            var model = Service.GetOrganizationModel(objectId, OrganizationViews.Info, null, null, null);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.OrganizationInfo, model) });
        }

        public virtual ActionResult Info(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            var personalInfo = Service.GetInfo(objectId);
            personalInfo.IsEditable = true;
            return Json(RenderPartialViewToString(MVC.Organization.Views.Info, personalInfo));
        }

        public virtual ActionResult Contacts(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            var model = Service.GetContacts(objectId);
            model.IsEditable = true;
            return Json(RenderPartialViewToString(MVC.Organization.Views.Contacts, model));
        }

        [Authorize]
        public virtual ActionResult SaveInfo(InfoEditModel info)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(info.ObjectId, null, null, null, null, null));
            }

            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    Content = RenderPartialViewToString(MVC.Organization.Views.InfoEdit, info),
                    IsValid = ModelState.IsValid
                });
            }

            EnsureMember(info.ObjectId);
            var personalInfoView = Service.SaveInfo(info);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Organization.Views.Info, personalInfoView),
                IsValid = ModelState.IsValid
            });
        }

        [Authorize]
        public virtual ActionResult SaveContacts(ContactsEditModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(model.ObjectId, null, null, null, null, null));
            }

            EnsureMember(model.ObjectId);
            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    Content = RenderPartialViewToString(MVC.Organization.Views.ContactsEdit, model),
                    IsValid = ModelState.IsValid
                });
            }
            
            var view = Service.SaveContacts(model);

            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Organization.Views.Contacts, view),
                IsValid = ModelState.IsValid
            });
        }

        [Authorize]
        public virtual ActionResult EditInfo(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            EnsureMember(objectId);
            var personalInfo = Service.GetInfoForEdit(objectId);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.InfoEdit, personalInfo),
                        IsValid = ModelState.IsValid});
        }

        [Authorize]
        public virtual ActionResult EditContacts(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            EnsureMember(objectId);
            var model = Service.GetContactsForEdit(objectId);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Organization.Views.ContactsEdit, model),
                IsValid = ModelState.IsValid
            });
        }

        [HttpPost]
        public virtual ActionResult SaveProfilePicture(string objectId, HttpPostedFileBase hpf)
        {
            if (hpf.ContentLength == 0)
                return Details(objectId, null, OrganizationViews.Activity, null, null, null);

            BinaryReader b = new BinaryReader(hpf.InputStream);
            byte[] fileBytes = b.ReadBytes((int) hpf.ContentLength);

            Service.SaveProfilePicture(objectId, fileBytes, hpf.ContentType);
            
            return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
        }
        
        private void EnsureMember(string objectId)
        {
            if (false)
            {
                throw new UnauthorizedAccessException("UnauthorizedAccessException: You're not a member of this organization.");
            }
        }

        [Authorize]
        public virtual ActionResult Like(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            Service.Like(objectId);
            return Json(string.Empty);
        }

        [Authorize]
        public virtual ActionResult Unlike(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            Service.Unlike(objectId);
            return Json(string.Empty);
        }

        [Authorize]
        public virtual ActionResult Join(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            Service.Join(objectId);
            return Json(string.Empty);
        }

        [Authorize]
        public virtual ActionResult Leave(string objectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(objectId, null, null, null, null, null));
            }

            Service.Leave(objectId);
            return Json(string.Empty);
        }

        public virtual ActionResult ConfirmMember(string organizationId, string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(userObjectId, null, null, null, null, null));
            }

            Service.Confirm(organizationId, userObjectId);
            return Json(true);
        }

        public virtual ActionResult RejectMember(string organizationId, string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(userObjectId, null, null, null, null, null));
            }

            Service.Reject(organizationId, userObjectId);
            return Json(true);
        }

        [Authorize]
        [HttpGet]
        public virtual ActionResult Create()
        {
            if (!CurrentUser.IsUnique)
            {
                TempData[FailureMessageKey] = Resource.CannotCreate;
            }

            return View(MVC.Organization.Views.Create, Service.Create());
        }

        [Authorize]
        [HttpPost]
        public virtual ActionResult Create(OrganizationCreateModel model)
        {
            if (!CurrentUser.IsUnique)
            {
                return RedirectToAction(MVC.Organization.Create());
            }

            if(ModelState.IsValid)
            {
                var result = Service.Create(model);

                return RedirectToAction(MVC.Organization.Details(result.ObjectId, result.Name, OrganizationViews.Info, null, null, null));
            }

            return View(MVC.Organization.Views.Create, model);
        }

         [Authorize]
        public virtual ActionResult CancelMembersEdit(string organizationId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Members, null, null, null));
            }

            var model = Service.GetConfirmedMembers(organizationId);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.ConfirmedMembers, model) });
        }

        [Authorize]
         public virtual ActionResult SaveMembers(OrganizationViewModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(model.ObjectId, null, OrganizationViews.Members, null, null, null));
            }

            if (!model.Members.Any(m => m.Permission == UserRoles.Admin))
            {
                ModelState.AddModelError("", Resource.AdministratorRequired);
            }

            if (ModelState.IsValid)
            {
                var result = Service.SaveMembers(model);
                return Json(new {Content = RenderPartialViewToString(MVC.Organization.Views.ConfirmedMembers, result)});
            }

            var dbModel = Service.GetConfirmedMembers(model.ObjectId);
            model.Members = dbModel;
            return Json(new {Content = RenderPartialViewToString(MVC.Organization.Views.MembersEdit, model)});
        }

        [Authorize]
        public virtual ActionResult GetMembersEdit(string organizationId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Members, null, null, null));
            }
            
            var model = Service.GetConfirmedMembers(organizationId);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.MembersEdit, new OrganizationViewModel()
                                                                                                          {
                                                                                                              Members = model,
                                                                                                              ObjectId = organizationId
                                                                                                          }) });
        }

        /*********************************Projects*************************************/
        public virtual ActionResult AddProject(string organizationId, string projectName, bool isPrivate)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            if(string.IsNullOrEmpty(projectName))
            {
                ModelState.AddModelError("projectName", Globalization.Resources.Organization.Resource.NameIsRequired);
            }

            if(ModelState.IsValid)
            {
                var model = Service.AddProject(organizationId, projectName, isPrivate);
                return
                    Json(
                        new {Content = RenderPartialViewToString(MVC.Organization.Views.Project.ProjectListItem, model)});
            }

            return Json(new { error = GetErrorMessages()});
        }

        [HttpGet]
        public virtual ActionResult ToDos(string organizationId, string projectId)
        {
            if (string.IsNullOrEmpty(organizationId) || string.IsNullOrEmpty(projectId))
            {
                return HttpNotFound();
            }

            var model = Service.ToDos(organizationId, projectId);
            return View(MVC.Organization.Views.Project.ToDos, model);
        }

        public virtual ActionResult AddToDo(ProjectToDosModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(model.OrganizationId, null, OrganizationViews.Projects, null, null, null));
            }

            var item = Service.AddToDo(model);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.ToDoItem, item) });
        }

        public virtual ActionResult FinishTodo(string organizationId, string projectId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            var model = Service.FinishToDo(organizationId, projectId, id);
            if (model.IsFinished)
            {
                return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.FinishedToDoItem, model), IsFinished = model.IsFinished });
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.ToDoItem, model), IsFinished = model.IsFinished });
        }

        public virtual ActionResult DeleteToDo(string organizationId, string projectId, string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            var success = Service.DeleteToDo(organizationId, projectId, id);
            return Json(success);
        }

        public virtual ActionResult GetToDoEdit(string organizationId, string projectId,  string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            var model = Service.GetToDoEdit(organizationId, projectId, id);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.EditToDoItem, model) });
        }

        public virtual ActionResult EditToDo(EditToDoModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.ToDos(model.OrganizationId, model.ProjectId));
            }

            var viewModel = Service.EditToDo(model);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.ToDoItem, viewModel) });
        }

        public virtual ActionResult ReorderToDos(string organizationId, string projectId,  int startPos, int endPos)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            Service.ReorderToDos(organizationId, projectId, startPos, endPos);
            return Json(true);
        }

        [HttpGet]
        public virtual ActionResult Comments(string organizationId, string projectId, string toDoId)
        {
            var model = Service.GetComments(organizationId, projectId, toDoId);
            var tempModel = TempData["TempModel"] as CommentsModel;
            if (tempModel != null)
            {
                model.InsertComment = tempModel.InsertComment;
                model.InsertIsPrivate = tempModel.InsertIsPrivate;
                model.InsertSendNotifications = tempModel.InsertSendNotifications;
            }
            return View(MVC.Organization.Views.Project.Comments, model);
        }

        public virtual ActionResult AddComment(CommentsModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Comments(model.ToDo.OrganizationId, model.ToDo.ProjectId, model.ToDo.ToDoId));
            }

            var view = Service.AddComment(model.ToDo.OrganizationId, model.ToDo.ProjectId, model.ToDo.ToDoId, model.InsertComment, model.InsertIsPrivate, model.InsertSendNotifications, model.Attachments);

            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.Comment, view) });
        }

        public virtual ActionResult SaveTempModel(CommentsModel model)
        {
            TempData["TempModel"] = model;
            return Json(true);
        }

        public virtual ActionResult DeleteComment(string organizationId, string projectId, string toDoId, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Comments(organizationId, projectId, toDoId));
            }

            var result = Service.DeleteComment(organizationId, projectId, toDoId, commentId);
            return Json(result);
        }

        [ImportModelStateFromTempData]
        [HttpGet]
        public virtual ActionResult Settings(string organizationId, string projectId)
        {
            if (string.IsNullOrEmpty(organizationId) || string.IsNullOrEmpty(projectId))
            {
                return HttpNotFound();
            }

            var model = Service.GetSettings(organizationId, projectId);
            return View(MVC.Organization.Views.Project.Settings, model);
        }

        [ExportModelStateToTempData]
        [HttpPost]
        public virtual ActionResult Settings(SettingsModel model)
        {
            if (ModelState.IsValid)
            {
                Service.SaveSettings(model);
                return
                    RedirectToSuccessAction(
                        MVC.Organization.Settings(model.OrganizationId, model.Id));
            }

            return RedirectToAction(MVC.Organization.Settings(model.OrganizationId, model.Id));
        }
        [HttpPost, Authorize]
        public virtual ActionResult AddInvitedUser(InviteUserModel model)
        {
            if (model.UserId.HasValue || !model.InvitedUser.IsNullOrEmpty())
            {
                return Json(new {Content = RenderPartialViewToString(MVC.Shared.Views.InvitedUser, model)});
            }

            return Json(null);
        }

        [HttpPost, Authorize]
        public virtual ActionResult InviteUsers(OrganizationViewModel model)
        {
            var result = Service.InviteUsers(model);
            return RedirectToAction(MVC.Organization.Details(model.ObjectId, null, OrganizationViews.Members, null, null, null));
        }

        [HttpPost, Authorize]
        public virtual ActionResult ReInvite(string organizationId, string userEmail)
        {
            var result = Service.InviteUser(organizationId, userEmail);
            return Json(result);
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult RemoveMember(string organizationId, int? userId)
        {
            if (!IsAjaxRequest || !userId.HasValue)
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Members, null, null, null));
            }

            var result = Service.RemoveMember(organizationId, userId.Value);
            return Json(result);
        }

        [Authorize]
        public virtual ActionResult DeleteProject(string organizationId, string projectId)
        {
            if(Service.DeleteProject(organizationId, projectId))
            {
                return
                    RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            ModelState.AddModelError("", Resource.ProjectDeleteFailed);
            return Settings(organizationId, projectId);
        }

        public virtual ActionResult GetProjects(string organizationId, ProjectListViews view)
        {
            if(!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, OrganizationViews.Projects, null, null, null));
            }

            var model = Service.GetProjects(organizationId, view);

            return
                Json(
                    new
                        {
                            Content = RenderPartialViewToString(MVC.Organization.Views.Project.ProjectListContainer, model)
                        });
        }

        public virtual ActionResult Delete(string objectId)
        {
            Service.DeleteOrganization(objectId);
            return RedirectToAction(MVC.Organization.Index());
        }

        public virtual ActionResult TakeToDo(string organizationId, string projectId, string toDoId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.ToDos(organizationId, projectId));
            }

            var viewModel = Service.TakeToDo(organizationId, projectId, toDoId);
            return Json(new { Content = RenderPartialViewToString(MVC.Organization.Views.Project.ToDoItem, viewModel) });
        }

        public virtual ActionResult DeleteInvitedUser(string organizationId, string email)
        {
            return Json(Service.DeleteInvitedUser(organizationId, email));
        }

        public virtual ActionResult SetMemberVisibility(string organizationId, int userId, bool isPublic)
        {
            return Json(Service.SetMemberVisibility(organizationId, userId, isPublic));
        }

        public virtual ActionResult UpdateAllPictures()
        {
            Service.UpdateAllPictures();
            return RedirectToSuccessAction(MVC.Common.Start(), "Nuotraukos sukeltos sėkmingai");
        }
    }
}
