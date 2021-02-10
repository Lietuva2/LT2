using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Data.ViewModels.Base;

namespace Data.ViewModels.Project
{
    public class CommentsModel
    {
        public string IdeaSubject { get; set; }
        public IList<CommentModel> Comments { get; set; }
        public ToDoModel ToDo { get; set; }
        [Required]
        public string InsertComment { get; set; }

        public bool InsertIsPrivate { get; set; }
        public bool InsertSendNotifications { get; set; }
        public List<UrlViewModel> Attachments { get; set; }

        public bool IsEditable { get; set; }
        public bool IsTodoPrivate { get; set; }

        public CommentsModel()
        {
            Comments = new List<CommentModel>();
            Attachments = new List<UrlViewModel>();
        }
    }
}
