using System;
using System.Collections.Generic;
using Data.ViewModels.Base;

namespace Data.ViewModels.Organization.Project
{
    public class CommentModel
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string ProjectId { get; set; }
        public string ToDoId { get; set; }
        public string AuthorObjectId { get; set; }
        public string CommentText { get; set; }
        public string AuthorFullName { get; set; }
        public DateTime CommentDate { get; set; }
        public bool IsDeletable { get; set; }
        public List<UrlViewModel> Attachments { get; set; }
    }
}
