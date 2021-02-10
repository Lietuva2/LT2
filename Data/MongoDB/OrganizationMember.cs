using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Driver;

namespace Data.MongoDB
{
    public class OrganizationMember
    {
        public MongoObjectId OrganizationId { get; set; }
        public MongoObjectId UserId { get; set; }
        public string Position { get; set; }
    }
}
