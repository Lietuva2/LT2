using System;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class Website
    {
        public enum Types
        {
            Personal = 1,
            Company = 2,
            Blog = 3,
            Rss = 4,
            Portfolio = 5,
            FaceBook = 6,
            LinkedIn = 7,
            Tweeter = 8,
            Other = 9
        }

        public MongoObjectId Id { get; set; }
        public Types Type { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string IconUrl { get; set; }

        public Website()
        {
            Id = BsonObjectId.GenerateNewId();
        }
    }
}
