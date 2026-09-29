using System;
using System.Collections.Generic;
using Data.Enums;


namespace Bus.Commands
{
    public class OrganizationProjectCommand : ActionCommand
    {
        public string OrganizationId
        {
            get { return ObjectId; }
            set { ObjectId = value; }
        }
        public string ProjectId { get; set; }
        public string ToDoId { get; set; }
        public int UserDbId { get; set; }
        public string ProjectSubject { get; set; }
        public string ToDoSubject { get; set; }
        public string ToDoLink { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationLink { get; set; }
        public string CommentId { get; set; }
        public bool SendNotifications { get; set; }
        public string UserLink { get; set; }
        public string UserFullName { get; set; }
    }
}
