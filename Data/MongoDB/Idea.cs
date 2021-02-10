using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Data.Enums;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Base;
using Data.ViewModels.Problem;
using Framework.Infrastructure.Storage;
using Framework.Other;
using Hyper.ComponentModel;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.MongoDB
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class Idea : ICommentable
    {
        public MongoObjectId Id { get; set; }
        public string Subject { get; set; }
        public WikiTextWithHistory SummaryWiki { get; set; }
        [BsonIgnore]
        public string LastSummary
        {
            get { return SummaryWiki.CurrentVersion.Text; }
        }

        public MongoObjectId RelatedIssueId { get; set; } 
        public string DocumentUrl { get; set; }
        public List<Comment> Comments { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTime? ModificationDate { get; set; }
        public List<int> CategoryIds { get; set; }
        public int ViewsCount { get; set; }
        public string ShortLink { get; set; }
        public int LastNumber { get; set; }
        public string OfficialUrl { get; set; }

        [BsonIgnore]
        public bool IsClosed
        {
            get { return ActualState.In(IdeaStates.Closed, IdeaStates.Realized, IdeaStates.Rejected); }
        }

        public int VotesCount { get; set; }

        public IdeaStates ActualState
        {
            get
            {
                if (State == IdeaStates.Resolved && Deadline.HasValue && Deadline.Value < DateTime.Now)
                {
                    if (VotesCount >= RequiredVotes)
                    {
                        return IdeaStates.Realized;
                    }
                    else
                    {
                        return IdeaStates.Rejected;
                    }
                }

                return State;
            }
        }

        public IdeaStates State { get; set; }

        public string Aim { get; set; }
        public string Resolution { get; set; }
        public MongoObjectId ProjectId { get; set; }
        public string StateDescription { get; set; }
        public int? CityId { get; set; }
        public int? MunicipalityId { get; set; }
        public string OrganizationId { get; set; }
        public bool IsDraft { get; set; }
        public string FinalVersionId { get; set; }
        public List<Website> Urls { get; set; }
        public List<string> RelatedIdeas { get; set; }
        public List<string> Problems { get; set; }
        public bool SendMail { get; set; }
        public int? RequiredVotes { get; set; }
        public bool IsMailSent { get; set; }
        public bool AdditionalInfoRequiredForVoting { get { return InitiativeType.HasValue && InitiativeType.In(InitiativeTypes.CompulsoryReferendum, InitiativeTypes.AdvisoryReferendum, InitiativeTypes.Law); } }
        public bool DocumentNoRequiredForVoting { get { return AdditionalInfoRequiredForVoting && InitiativeType != InitiativeTypes.Law; } }
        public bool SaveAimAsProblem { get; set; }
        public DateTime? Deadline { get; set; }
        public EntryTypes EntryType { get { return EntryTypes.Idea; } }
        public InitiativeTypes? InitiativeType { get; set; }
        public bool IsImpersonal { get; set; }
        public string ResolvedByPersonCode { get; set; }
        public ObjectVisibility Visibility { get; set; }
        public bool IsPrivateToOrganization { get { return Visibility != ObjectVisibility.Public; } set { _isItPrivate = value; } }
        public bool _isItPrivate = false;
        public bool ConfirmedUsersVoting { get; set; }
        public bool ForbidPublicAlternativeIdeas { get; set; }
        public bool PromoteToFrontPage { get; set; }

        public override string ToString()
        {
            return Subject;
        }

        public Idea()
        {
            Comments = new List<Comment>();
            SummaryWiki = new WikiTextWithHistory();
            CategoryIds = new List<int>();
            Urls = new List<Website>();
            RelatedIdeas = new List<string>();
            IsDraft = false;
            Id = BsonObjectId.GenerateNewId();
            Visibility = ObjectVisibility.Public;
        }

        public string GetRelatedVersionNumber(string versionId)
        {
            if (versionId != null)
            {
                var version = SummaryWiki.Versions.Where(v => v.Id == versionId).SingleOrDefault();
                if (version != null)
                {
                    return version.Number.ToString();
                }
            }

            return null;
        }
    }
}
