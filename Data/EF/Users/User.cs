//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Data.EF.Users
{
    using System;
    using System.Collections.Generic;
    
    public partial class User
    {
        public User()
        {
            this.BankAccountItems = new HashSet<BankAccountItem>();
            this.ChatClients = new HashSet<ChatClient>();
            this.UserAwards = new HashSet<UserAward>();
            this.UserCategories = new HashSet<UserCategory>();
            this.WebToPayLogs = new HashSet<WebToPayLog>();
            this.ChatMessages = new HashSet<ChatMessage>();
            this.ChatMessages1 = new HashSet<ChatMessage>();
            this.ChatGroupUsers = new HashSet<ChatGroupUser>();
            this.UserEmails = new HashSet<UserEmail>();
            this.BlackLists = new HashSet<BlackList>();
            this.BlackLists1 = new HashSet<BlackList>();
            this.ChatOpenDialogs = new HashSet<ChatOpenDialog>();
            this.ChatOpenDialogs1 = new HashSet<ChatOpenDialog>();
            this.ChatOpenGroups = new HashSet<ChatOpenGroup>();
        }
    
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string ObjectId { get; set; }
        public Nullable<long> FacebookId { get; set; }
        public short Role { get; set; }
        public int LanguageId { get; set; }
        public bool RequireChangePassword { get; set; }
        public bool Signed { get; set; }
        public bool IsAmbasador { get; set; }
        public Nullable<bool> PostPermissionGranted { get; set; }
        public string PersonCode { get; set; }
        public string AuthSource { get; set; }
        public bool RequireUniqueAuthentication { get; set; }
        public bool TutorialShown { get; set; }
        public System.DateTime RegisteredOn { get; set; }
        public Nullable<bool> PageLiked { get; set; }
        public bool IsPolitician { get; set; }
    
        public virtual ICollection<BankAccountItem> BankAccountItems { get; set; }
        public virtual ICollection<ChatClient> ChatClients { get; set; }
        public virtual Language Language { get; set; }
        public virtual ICollection<UserAward> UserAwards { get; set; }
        public virtual ICollection<UserCategory> UserCategories { get; set; }
        public virtual ICollection<WebToPayLog> WebToPayLogs { get; set; }
        public virtual ICollection<ChatMessage> ChatMessages { get; set; }
        public virtual ICollection<ChatMessage> ChatMessages1 { get; set; }
        public virtual ICollection<ChatGroupUser> ChatGroupUsers { get; set; }
        public virtual ICollection<UserEmail> UserEmails { get; set; }
        public virtual ICollection<BlackList> BlackLists { get; set; }
        public virtual ICollection<BlackList> BlackLists1 { get; set; }
        public virtual ICollection<ChatOpenDialog> ChatOpenDialogs { get; set; }
        public virtual ICollection<ChatOpenDialog> ChatOpenDialogs1 { get; set; }
        public virtual ICollection<ChatOpenGroup> ChatOpenGroups { get; set; }
    }
}