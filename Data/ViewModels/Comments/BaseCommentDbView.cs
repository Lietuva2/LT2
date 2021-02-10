using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.ViewModels.Voting;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Comments
{
    public class BaseCommentDbView
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public DateTime Date { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public int? TypeId { get; set; }
        public int EntryTypeId { get; set; }
        public string ObjectId { get; set; }
        public int SupporterCount { get; set; }
        public bool IsLikedByCurrentUser { get; set; }
        public bool IsHidden { get; set; }
        public Data.EF.Voting.Embed Embed { get; set; }
        public string Number { get; set; }
    }
}
