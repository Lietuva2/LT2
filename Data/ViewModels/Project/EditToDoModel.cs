using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class EditToDoModel
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        [Required]
        public string Subject { get; set; }
        public string ResponsibleUserId { get; set; }
        public IList<SelectListItem> ResponsibleUsers { get; set; }
        public DateTime? DueDate { get; set; }
        public bool IsPrivate { get; set; }
        public string MileStoneId { get; set; }
        public IList<SelectListItem> Milestones { get; set; }

        public EditToDoModel()
        {
        }
    }
}