using System;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class PhoneNumber
    {
        public enum Types
        {
            Home = 1,
            Work = 2,
            Mobile = 3
        }

        public MongoObjectId Id { get; set; }
        public Types Type { get; set; }
        public string Phone { get; set; }

        public PhoneNumber()
        {
            Id = BsonObjectId.GenerateNewId();
        }
    }
}
