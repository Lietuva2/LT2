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
    
    public partial class Subscribtion
    {
        public int Id { get; set; }
        public string ObjectId { get; set; }
        public int UserId { get; set; }
        public bool Subscribed { get; set; }
        public Nullable<int> EntryTypeId { get; set; }
    }
}
