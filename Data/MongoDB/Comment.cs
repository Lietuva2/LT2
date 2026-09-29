using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class Comment
    {
        public MongoObjectId Id { get; set; }
        public string Text { get; set; }
        public ForAgainst PositiveOrNegative { get; set; }
        public string UserFullName { get; set; }
        public MongoObjectId UserObjectId { get; set; } 
        public List<Comment> Comments { get; set; }

        /// <summary>
        /// Legacy - not used
        /// </summary>
        public int SupportingUserCount { get; set; }
        public List<MongoObjectId> SupportingUserIds { get; set; }
        public MongoObjectId RelatedVersionId { get; set; }
        public DateTime Date { get; set; }
        public bool IsHidden { get; set; }
        public Embed Embed { get; set; }
        public string Number { get; set; }
        public int LastNumber { get; set; }

        public Comment()
        {
            Comments = new List<Comment>();
            Id = BsonObjectId.GenerateNewId();
            SupportingUserIds = new List<MongoObjectId>();
        }
    }
}
