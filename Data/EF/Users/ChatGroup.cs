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
    
    public partial class ChatGroup
    {
        public ChatGroup()
        {
            this.ChatGroupUsers = new HashSet<ChatGroupUser>();
            this.ChatOpenGroups = new HashSet<ChatOpenGroup>();
            this.ChatMessages = new HashSet<ChatMessage>();
        }
    
        public string Id { get; set; }
        public string Url { get; set; }
        public string Name { get; set; }
        public bool IsPrivate { get; set; }
        public string OrganizationId { get; set; }
    
        public virtual ICollection<ChatGroupUser> ChatGroupUsers { get; set; }
        public virtual ICollection<ChatOpenGroup> ChatOpenGroups { get; set; }
        public virtual ICollection<ChatMessage> ChatMessages { get; set; }
    }
}
