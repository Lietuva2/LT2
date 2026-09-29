using System;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class PoliticalParty
    {
        public MongoObjectId Id { get; set; }
        public string PartyName { get; set; }
        public string PartyUrl { get; set; }
        public int? StartYear { get; set; }
        public int? StartMonth { get; set; }
        public int? EndYear { get; set; }
        public int? EndMonth { get; set; }
        public string Description { get; set; }
        public bool IsCurrent { get; set; }

        public PoliticalParty()
        {
            Id = BsonObjectId.GenerateNewId();
        }
    }
}
