using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.MongoDB.Interfaces;
using Hyper.ComponentModel;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class Issue : ICommentable
    {
        public MongoObjectId Id { get; set; }
        public int DbId { get; set; }
        public string Subject { get; set; }
        public WikiText SummaryWiki { get; set; }
        [BsonIgnore]
        public string Summary
        {
            get { return SummaryWiki.CurrentVersion.Text; }
        }
        public DateTime? Deadline { get; set; }
        public string DocumentUrl { get; set; }
        public List<Comment> Comments { get; set; }
        public string RegisteredBy { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTime? ModificationDate { get; set; }
        public List<int> CategoryIds { get; set; }
        public int SupportingVotesCount { get; set; }
        public int NonSupportingVotesCount { get; set; }
        public int NeutralVotesCount { get; set; }
        public List<SupportingUser> AdditionalVotes { get; set; }
        public int ViewsCount { get; set; }
        public ForAgainst OfficialVote { get; set; }
        public int? CityId { get; set; }
        public int? MunicipalityId { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public string OfficialVotingDescription { get; set; }
        public List<Website> Urls { get; set; }
        public bool IsMailSent { get; set; }
        public bool AdditionalInfoRequiredForVoting { get; set; }
        public EntryTypes EntryType { get { return EntryTypes.Issue; } }
        public string ShortLink { get; set; }
        public int LastNumber { get; set; }
        public bool AllowSummaryWiki { get; set; }
        public bool AllowNeutralVotes { get; set; }
        public ObjectVisibility Visibility { get; set; }
        public bool IsPrivateToOrganization { get { return Visibility != ObjectVisibility.Public; } set { _isItPrivate = value; } }
        public bool _isItPrivate = false;

        [BsonIgnore]
        public bool IsClosed
        {
            get { return Deadline < DateTime.Now || OfficialVote != ForAgainst.Neutral; }
        }

        public override string ToString()
        {
            return Subject;
        }

        public Issue()
        {
            Comments = new List<Comment>();
            SummaryWiki = new WikiText();
            CategoryIds = new List<int>();
            OfficialVote = ForAgainst.Neutral;
            Urls = new List<Website>();
            Id = BsonObjectId.GenerateNewId();
            AdditionalVotes = new List<SupportingUser>();
            AllowSummaryWiki = true;
        }

        public static string GetSupportPercentageString(int supportingVotesCount, int nonSupportingVotesCount, string notVotedString)
        {
            return supportingVotesCount + nonSupportingVotesCount > 0
                       ? ((float)supportingVotesCount / (supportingVotesCount + nonSupportingVotesCount)).ToString("P")
                       : notVotedString;
        }

        public string GetSupportPercentageString(string notVotedString)
        {
            return GetSupportPercentageString(this.SupportingVotesCount, this.NonSupportingVotesCount, notVotedString);
        }

        public static int? GetSupportPercentage(int supportingVotesCount, int nonSupportingVotesCount)
        {
            return supportingVotesCount + nonSupportingVotesCount > 0
                ? (int?)(((float)supportingVotesCount / (supportingVotesCount + nonSupportingVotesCount)) * 100) : 0;
        }

        public int? GetSupportPercentage()
        {
            return GetSupportPercentage(this.SupportingVotesCount, this.NonSupportingVotesCount);
        }

        public static ForAgainst GetDecision(int supportingVotesCount, int nonSupportingVotesCount, ForAgainst officialVote)
        {
            if (officialVote == ForAgainst.Neutral)
            {
                return ForAgainst.Neutral;
            }

            if (supportingVotesCount > nonSupportingVotesCount && officialVote == ForAgainst.For)
            {
                return ForAgainst.For;
            }

            if (supportingVotesCount < nonSupportingVotesCount && officialVote == ForAgainst.Against)
            {
                return ForAgainst.For;
            }

            if (supportingVotesCount > nonSupportingVotesCount && officialVote == ForAgainst.Against)
            {
                return ForAgainst.Against;
            }

            if (supportingVotesCount < nonSupportingVotesCount && officialVote == ForAgainst.For)
            {
                return ForAgainst.Against;
            }

            return ForAgainst.Neutral;
        }

        public ForAgainst GetDecision()
        {
            return GetDecision(this.SupportingVotesCount, this.NonSupportingVotesCount, this.OfficialVote);
        }

        public string GetRelatedVersionNumber(string versionId)
        {
            return null;
        }
    }
}
