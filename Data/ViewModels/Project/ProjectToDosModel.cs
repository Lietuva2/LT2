using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web.Mvc;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ProjectToDosModel : ProjectModel
    {
        public List<ToDoModel> ToDos { get; set; }
        public List<ToDoModel> FinishedToDos { get; set; }
        public List<MileStoneModel> MileStones { get; set; }
        public List<MileStoneModel> FinishedMileStones { get; set; }
        public bool IsEditable { get; set; }
        [Required]
        public string InsertSubject {get; set;}
        public string InsertResponsibleUserId { get; set; }
        public DateTime? InsertDueDate { get; set; }
        public IList<SelectListItem> InsertResponsibleUsers { get; set; }
        public IList<SelectListItem> InsertMilestones { get; set; }
        public bool InsertToDoIsPrivate { get; set; }
        public string InsertMileStoneId { get; set; }
        public bool InsertSendNotifications { get; set; }
        public string StateDescription { get; set; }
        public string State { get; set; }

        public ProjectToDosModel()
        {
            ToDos = new List<ToDoModel>();
        }

        public bool AnyToDos()
        {
            var cnt = this.ToDos.Count;
            cnt += (from m in MileStones
                    from t in m.ToDos
                    select t).Count();
            return cnt > 0;
        }
    }
}