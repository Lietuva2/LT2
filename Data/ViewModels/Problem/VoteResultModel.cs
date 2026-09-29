using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.EF.Voting;
using Data.Enums;
using Data.ViewModels.Base;

namespace Data.ViewModels.Problem
{
    public class VoteResultModel
    {
        public string Id { get; set; }
        public int VotesCount { get; set; }
        public ForAgainst CurrentVote { get; set; }
        public int? CurrentVoteId { get; set; }
        public ProblemSupporter CurrentSupporter { get; set; }
        public SubscribeModel Subscribe { get; set; }
    }
}
