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
    
    public partial class ChatMessage
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public System.DateTime Date { get; set; }
        public int UserId { get; set; }
        public Nullable<int> OtherUserId { get; set; }
        public bool HtmlEncoded { get; set; }
        public string GroupId { get; set; }
    
        public virtual User User { get; set; }
        public virtual User User1 { get; set; }
        public virtual ChatGroup ChatGroup { get; set; }
    }
}
