
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Data.Enums;
using Framework.Infrastructure.Storage;
using Hyper.ComponentModel;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class OrganizationProject
    {
        public MongoObjectId Id { get; set; }
        public string Subject { get; set; }
        public List<ToDo> ToDos { get; set; }
        public bool IsPrivate { get; set; }
        public string StateDescription { get; set; }
        public OrganizationProjectStates State { get; set; }
        public DateTime ModificationDate { get; set; }

        [BsonIgnore]
        public Organization Organization { get; set; }

        public OrganizationProject()
        {
            ToDos = new List<ToDo>();
        }
    }
}
