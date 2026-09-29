using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Infrastructure.Storage;

namespace Data.MongoDB.Interfaces
{
    public interface ICommentable
    {
        MongoObjectId Id { get; set; }
        List<Comment> Comments { get; set; }
        string GetRelatedVersionNumber(string versionId);
        EntryTypes EntryType { get; }
        DateTime? ModificationDate { get; set; }
        int LastNumber { get; set; }
    }
}
