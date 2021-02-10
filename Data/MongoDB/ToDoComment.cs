using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class ToDoComment
    {
        public MongoObjectId Id { get; set; }
        public string Text { get; set; }
        public string UserFullName { get; set; }
        public MongoObjectId UserObjectId { get; set; } 
        public DateTime Date { get; set; }
        public bool IsPrivate { get; set; }
        public List<Website> Attachments { get; set; }

        public ToDoComment()
        {
            Id = BsonObjectId.GenerateNewId();
            IsPrivate = false;
            Attachments = new List<Website>();
        }
    }
}
