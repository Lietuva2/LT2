//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Data.EF.Actions
{
    using System;
    using System.Collections.Generic;
    
    public partial class UserInterestingOrganization
    {
        public int UserId { get; set; }
        public string OrganizationId { get; set; }
        public bool IsMember { get; set; }
        public bool IsConfirmed { get; set; }
        public string Role { get; set; }
        public int Permission { get; set; }
        public bool IsPrivate { get; set; }
        public Nullable<int> InvitedByUserId { get; set; }
        public int VoteCount { get; set; }
    }
}
