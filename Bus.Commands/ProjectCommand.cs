using System;
using System.Collections.Generic;
using Data.Enums;


namespace Bus.Commands
{
    public class ProjectCommand : ActionCommand
    {
        public string ProjectId
        {
            get { return ObjectId; }
            set { ObjectId = value; }
        }

        public string MileStoneId { get; set; }
        public string ToDoId { get; set; }
        public string ProjectSubject { get; set; }
        public string ToDoSubject { get; set; }
        public string ToDoLink { get; set; }
        public string CommentId { get; set; }
        public string UserLink { get; set; }
        public string UserFullName { get; set; }
        public bool SendNotifications { get; set; }

        public ProjectCommand()
        {
            IsPrivate = false;
        }
    }
}
