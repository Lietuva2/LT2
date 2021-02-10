﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Data.EF.Voting
{
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    
    public partial class VotingEntities : DbContext
    {
        public VotingEntities()
            : base("name=VotingEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<Comment> Comments { get; set; }
        public virtual DbSet<CommentSupporter> CommentSupporters { get; set; }
        public virtual DbSet<Embed> Embeds { get; set; }
        public virtual DbSet<IdeaCategory> IdeaCategories { get; set; }
        public virtual DbSet<IdeaComment> IdeaComments { get; set; }
        public virtual DbSet<IdeaIssue> IdeaIssues { get; set; }
        public virtual DbSet<IdeaPriority> IdeaPriorities { get; set; }
        public virtual DbSet<Idea> Ideas { get; set; }
        public virtual DbSet<IdeaVersion> IdeaVersions { get; set; }
        public virtual DbSet<IdeaVersionView> IdeaVersionViews { get; set; }
        public virtual DbSet<IdeaVote> IdeaVotes { get; set; }
        public virtual DbSet<IssueCategory> IssueCategories { get; set; }
        public virtual DbSet<IssueComment> IssueComments { get; set; }
        public virtual DbSet<IssueVersion> IssueVersions { get; set; }
        public virtual DbSet<ProblemCategory> ProblemCategories { get; set; }
        public virtual DbSet<ProblemComment> ProblemComments { get; set; }
        public virtual DbSet<ProblemIdea> ProblemIdeas { get; set; }
        public virtual DbSet<ProblemIssue> ProblemIssues { get; set; }
        public virtual DbSet<Problem> Problems { get; set; }
        public virtual DbSet<ProblemSupporter> ProblemSupporters { get; set; }
        public virtual DbSet<RelatedIdea> RelatedIdeas { get; set; }
        public virtual DbSet<UserComment> UserComments { get; set; }
        public virtual DbSet<Vote> Votes { get; set; }
        public virtual DbSet<Issue> Issues { get; set; }
        public virtual DbSet<ShortLink> ShortLinks { get; set; }
    }
}