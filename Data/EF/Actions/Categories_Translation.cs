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
    
    public partial class Categories_Translation
    {
        public int Id { get; set; }
        public short CategoryId { get; set; }
        public string LanguageCode { get; set; }
        public string Name { get; set; }
    
        public virtual Category Category { get; set; }
    }
}
