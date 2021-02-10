using System;
using System.ComponentModel;
using Data.Enums;
using Data.ViewModels.Base;
using Hyper.ComponentModel;

namespace Data.ViewModels.Voting
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class VotingStatisticsViewModel
    {
        public string Id { get; set; }
        public ForAgainst? Vote { get; set; }
        public ForAgainst? AdditionalVote { get; set; }
        public string VotedString { get; set; }
        public int VotesCount { get; set; }
        public int? SupportPercentage { get; set; }
        public bool IsVotingFinished { get; set; }
        public string TimeLeft { get; set; }
        public DateTime? Deadline { get; set; }
        public int SupportingVotesCount { get; set; }
        public int NonSupportingVotesCount { get; set; }
        public int NeutralVotesCount { get; set; }
        public ForAgainst OfficialVote { get; set; }
        public ForAgainst Decision { get; set; }
        public SubscribeModel Subscribe { get; set; }
        public int SupportingAdditionalVotesCount { get; set; }
        public int NonSupportingAdditionalVotesCount { get; set; }
        public int NeutralAdditionalVotesCount { get; set; }
        public bool AllowNeutralVotes { get; set; }

        public VotingStatisticsViewModel()
        {
            Vote = ForAgainst.Neutral;
            OfficialVote = ForAgainst.Neutral;
            Decision = ForAgainst.Neutral;
        }

        public static implicit operator VotingStatisticsViewModel(VotingViewModel model)
        {
            return new VotingStatisticsViewModel
                       {
                           Id = model.Id,
                           IsVotingFinished = model.IsVotingFinished,
                           SupportPercentage = model.SupportPercentage,
                           Vote = model.Vote,
                           AdditionalVote = model.AdditionalVote,
                           VotedString = model.VotedString,
                           VotesCount = model.VotesCount,
                           TimeLeft = model.TimeLeft,
                           Deadline = model.Deadline,
                           SupportingVotesCount = model.SupportingVotesCount,
                           NonSupportingVotesCount = model.NonSupportingVotesCount,
                           NeutralVotesCount = model.NeutralVotesCount,
                           SupportingAdditionalVotesCount = model.SupportingAdditionalVotesCount,
                           NonSupportingAdditionalVotesCount = model.NonSupportingAdditionalVotesCount,
                           NeutralAdditionalVotesCount = model.NeutralAdditionalVotesCount,
                           OfficialVote = model.OfficialVote,
                           Decision = Data.MongoDB.Issue.GetDecision(model.SupportingVotesCount, model.NonSupportingVotesCount, model.OfficialVote),
                           AllowNeutralVotes = model.AllowNeutralVotes
                       };
        }
    }
}