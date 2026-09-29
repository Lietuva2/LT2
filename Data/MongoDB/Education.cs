using System;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;

namespace Data.MongoDB
{
    public class Education
    {
        public MongoObjectId Id { get; set; }
        public string Country { get; set; }
        public string SchoolName { get; set; }
        public string Degree { get; set; }
        public string FieldOfStudy { get; set; }
        public string YearFrom { get; set; }
        public string YearTo { get; set; }
        public string Activities { get; set; }
        public string Notes { get; set; }

        public Education()
        {
            Id = BsonObjectId.GenerateNewId();
        }
    }
}
