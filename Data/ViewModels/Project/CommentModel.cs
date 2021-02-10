using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Voting;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Project
{
    public class CommentModel
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string MileStoneId { get; set; }
        public string ToDoId { get; set; }
        public string AuthorObjectId { get; set; }
        public string CommentText { get; set; }
        public string AuthorFullName { get; set; }
        public DateTime CommentDate { get; set; }
        public bool IsDeletable { get; set; }
        public List<UrlViewModel> Attachments { get; set; }
    }
}
