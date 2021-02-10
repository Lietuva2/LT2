using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class MileStone
    {
        public MongoObjectId Id { get; set; }
        public string Subject { get; set; }
        public DateTime? Date { get; set; }
        public List<ToDo> ToDos { get; set; }
        [BsonIgnore]
        public string DateString
        {
            get { return this.Subject + " " + (this.Date.HasValue ? this.Date.Value.ToShortDateString() : string.Empty); }
        }

        public MileStone()
        {
            Id = BsonObjectId.GenerateNewId();
            ToDos = new List<ToDo>();
        }
    }
}
