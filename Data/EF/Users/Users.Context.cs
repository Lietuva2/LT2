﻿//------------------------------------------------------------------------------
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
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    
    public partial class UsersEntities : DbContext
    {
        public UsersEntities()
            : base("name=UsersEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<BankAccount> BankAccounts { get; set; }
        public virtual DbSet<BankAccountItem> BankAccountItems { get; set; }
        public virtual DbSet<ChatClient> ChatClients { get; set; }
        public virtual DbSet<City> Cities { get; set; }
        public virtual DbSet<Country> Countries { get; set; }
        public virtual DbSet<Gift> Gifts { get; set; }
        public virtual DbSet<Language> Languages { get; set; }
        public virtual DbSet<Municipality> Municipalities { get; set; }
        public virtual DbSet<Notification> Notifications { get; set; }
        public virtual DbSet<UniqueUser> UniqueUsers { get; set; }
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<UserAward> UserAwards { get; set; }
        public virtual DbSet<UserCategory> UserCategories { get; set; }
        public virtual DbSet<UserInvitation> UserInvitations { get; set; }
        public virtual DbSet<WebToPayLog> WebToPayLogs { get; set; }
        public virtual DbSet<ChatMessage> ChatMessages { get; set; }
        public virtual DbSet<ChatGroupUser> ChatGroupUsers { get; set; }
        public virtual DbSet<UserEmail> UserEmails { get; set; }
        public virtual DbSet<BlackList> BlackLists { get; set; }
        public virtual DbSet<ChatGroup> ChatGroups { get; set; }
        public virtual DbSet<Status> Status { get; set; }
        public virtual DbSet<ChatOpenDialog> ChatOpenDialogs { get; set; }
        public virtual DbSet<ChatOpenGroup> ChatOpenGroups { get; set; }
    }
}