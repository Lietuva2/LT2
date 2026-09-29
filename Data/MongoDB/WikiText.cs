using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class WikiText
    {
        public List<WikiTextVersion> Versions { get; set; }
        [BsonIgnore]
        public WikiTextVersion CurrentVersion
        {
            get { return Versions.Last(); }
        }

        public WikiText()
        {
            Versions = new List<WikiTextVersion>();
        }
    }
}
