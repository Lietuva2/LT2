using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class WikiTextVersionWithHistory : WikiTextVersion
    {
        [BsonIgnoreIfNull]
        public List<WikiTextVersion> History { get; set; }

        public WikiTextVersionWithHistory()
        {
            History = new List<WikiTextVersion>();
        }
    }
}
