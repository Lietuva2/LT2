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
    
    public partial class ActionType
    {
        public ActionType()
        {
            this.Actions = new HashSet<Action>();
        }
    
        public short Id { get; set; }
        public string Name { get; set; }
        public int Points { get; set; }
        public Nullable<int> Reputation { get; set; }
    
        public virtual ICollection<Action> Actions { get; set; }
    }
}
