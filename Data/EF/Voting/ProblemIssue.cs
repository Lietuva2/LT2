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
    
    public partial class ProblemIssue
    {
        public string ProblemId { get; set; }
        public string IssueId { get; set; }
        public string UserObjectId { get; set; }
    
        public virtual Problem Problem { get; set; }
    }
}
