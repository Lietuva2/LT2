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
    
    public partial class WebToPayLog
    {
        public int Id { get; set; }
        public string Firstname { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public Nullable<decimal> Amount { get; set; }
        public System.DateTime Date { get; set; }
        public string Currency { get; set; }
        public string Country { get; set; }
        public short Status { get; set; }
        public string Error { get; set; }
        public string RequestId { get; set; }
        public Nullable<int> UserId { get; set; }
        public string PayText { get; set; }
        public Nullable<int> GiftId { get; set; }
    
        public virtual Gift Gift { get; set; }
        public virtual User User { get; set; }
    }
}