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
    
    public partial class RelatedIdea
    {
        public string IdeaObjectId { get; set; }
        public string RelatedIdeaObjectId { get; set; }
        public string UserObjectId { get; set; }
    
        public virtual Idea Idea { get; set; }
        public virtual Idea Idea1 { get; set; }
    }
}
