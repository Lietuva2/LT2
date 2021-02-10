using System;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class WorkPosition
    {
        public MongoObjectId Id { get; set; }
        public string CompanyName { get; set; }
        public string Title { get; set; }
        public int? StartYear { get; set; }
        public int? StartMonth { get; set; }
        public int? EndYear { get; set; }
        public int? EndMonth { get; set; }
        public string Description { get; set; }
        public bool IsCurrent { get; set; }

        public WorkPosition()
        {
            Id = BsonObjectId.GenerateNewId();
        }
    }
}

