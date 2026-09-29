using System;
using System.ComponentModel;
using Hyper.ComponentModel;

namespace Data.ViewModels.Organization.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ToDoModel
    {
        public string OrganizationId { get; set; }
        public string ToDoId { get; set; }
        public string ProjectId { get; set; }
        public string Subject { get; set; }
        public string ResponsibleUserId { get; set; }
        public string ResponsibleUserFullName { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? FinishDate { get; set; }
        public bool IsFinished { get; set; }
        public int? Position { get; set; }
        public int CommentsCount { get; set; }
        public bool IsEditable { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsLate { get; set; }
        public string CreatedByUserId { get; set; }
        public string CreatedByUserFullName { get; set; }

        public ToDoModel()
        {
        }
    }
}