using System;
using System.Collections.Generic;
using System.ComponentModel;
using Hyper.ComponentModel;
using Framework.Infrastructure.Storage;

namespace Data.MongoDB
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ToDo
    {
        public MongoObjectId Id { get; set; }
        public string Subject { get; set; }
        public string ResponsibleUserId { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? FinishDate { get; set; }
        public int? Position { get; set; }
        public bool IsPrivate { get; set; }
        public List<ToDoComment> Comments { get; set; }
        public string CreatedByUserId { get; set; }

        public ToDo()
        {
            Comments = new List<ToDoComment>();
        }
    }
}
