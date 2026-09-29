using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Hyper.ComponentModel;

namespace Data.ViewModels.Idea
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class FinalIdeaModel
    {
        public string IdeaId { get; set; }
        public string FinalVersionId { get; set; }
        [AllowHtml]
        public string Resolution { get; set; }
        public int RequiredVotes { get; set; }
        public DateTime? Deadline { get; set; }
        public TimeSpan? DeadlineTime { get; set; }
        public InitiativeTypes? InitiativeType { get; set; }
        public bool Cancel { get; set; }
        public bool SendMail { get; set; }
        public string OfficialUrl { get; set; }
    }
}