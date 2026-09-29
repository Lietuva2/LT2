using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.MongoDB.Interfaces;
using Hyper.ComponentModel;
using Framework.Infrastructure.Storage;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class Problem : ICommentable
    {
        public MongoObjectId Id { get; set; }
        public string Text { get; set; }
        public List<Comment> Comments { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public DateTime Date { get; set; }
        public List<int> CategoryIds { get; set; }
        public int VotesCount { get; set; }
        public int? MunicipalityId { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public DateTime? ModificationDate { get; set; }
        public Embed Embed { get; set; }
        public bool IsPrivate { get; set; }
        public int LastNumber { get; set; }

        public EntryTypes EntryType
        {
            get { return EntryTypes.Problem; }
        }

        public Problem()
        {
            Comments = new List<Comment>();
            CategoryIds = new List<int>();
        }

        public string GetRelatedVersionNumber(string versionId)
        {
            return null;
        }
    }
}
