//------------------------------------------------------------------------------
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
    using System.Collections.Generic;
    
    public partial class IssueCategory
    {
        public int IssueId { get; set; }
        public int CategoryId { get; set; }
    
        public virtual Issue Issue { get; set; }
    }
}
