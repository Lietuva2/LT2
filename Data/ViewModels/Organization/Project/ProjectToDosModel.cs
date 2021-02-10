using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web.Mvc;
using Data.ViewModels.Project;
using Hyper.ComponentModel;

namespace Data.ViewModels.Organization.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ProjectToDosModel : ProjectBase
    {
        public List<ToDoModel> ToDos { get; set; }
        public List<ToDoModel> FinishedToDos { get; set; }
        [Required]
        public string InsertSubject {get; set;}
        public string InsertResponsibleUserId { get; set; }
        public DateTime? InsertDueDate { get; set; }
        public IList<SelectListItem> InsertResponsibleUsers { get; set; }
        public bool InsertToDoIsPrivate { get; set; }
        public bool InsertSendNotifications { get; set; }
        public bool IsPrivate { get; set; }
        public string StateDescription { get; set; }
        public string State { get; set; }

        public ProjectToDosModel()
        {
            ToDos = new List<ToDoModel>();
        }

        public bool AnyToDos()
        {
            var cnt = this.ToDos.Count;
            return cnt > 0;
        }
    }
}