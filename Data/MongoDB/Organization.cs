using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    public class Organization
    {
        public MongoObjectId Id { get; set; }
        public string Name { get; set; }
        public OrganizationTypes Type { get; set; }
        public List<Website> WebSites { get; set; }
        public string Description { get; set; }
        public string Vision { get; set; }
        public string Mission { get; set; }
        public string Goals { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public DateTime? FoundedOn { get; set; }
        public bool IsPrivate { get; set; }
        public string ShortLink { get; set; }
        public string CustomInvitationText { get; set; }
        public string CreatedByUserObjectId { get; set; }

        public MongoObjectId ProfilePictureId { get; set; }
        public MongoObjectId ProfilePictureThumbId { get; set; }
        public List<MongoObjectId> ProfilePictureHistory { get; set; }
        public List<OrganizationProject> Projects { get; set; }
        

        public Organization()
        {
            WebSites = new List<Website>();
            ProfilePictureHistory = new List<MongoObjectId>();
            Projects = new List<OrganizationProject>();
            IsPrivate = true;
            Id = BsonObjectId.GenerateNewId();
        }
    }
}