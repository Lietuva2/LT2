using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class WikiTextVersion
    {
        public MongoObjectId Id { get; set; }
        public string CreatorFullName { get; set; }
        public MongoObjectId CreatorObjectId { get; set; }
        public DateTime CreatedOn { get; set; }
        public string Text { get; set; }
        public string Subject { get; set; }
        public int Number { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }

        public int SupportingUserCount { get; set; }
        public List<MongoObjectId> SupportingUserIds { get; set; }
        public List<SupportingUser> SupportingUsers { get; set; }

        public List<Website> Attachments { get; set; }

        public WikiTextVersion()
        {
            Id = BsonObjectId.GenerateNewId();
            SupportingUserIds = new List<MongoObjectId>();
            SupportingUsers = new List<SupportingUser>();
            Attachments = new List<Website>();
        }
    }
}
