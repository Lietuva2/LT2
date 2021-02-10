using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class WikiTextWithHistory
    {
        public List<WikiTextVersionWithHistory> Versions { get; set; }
        [BsonIgnore]
        public WikiTextVersionWithHistory CurrentVersion
        {
            get { return Versions.Last(); }
        }

        public WikiTextWithHistory()
        {
            Versions = new List<WikiTextVersionWithHistory>();
        }
    }
}
