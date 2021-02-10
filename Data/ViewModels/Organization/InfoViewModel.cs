using System;
using Data.MongoDB;

namespace Data.ViewModels.Organization
{
    public class InfoViewModel
    {
        public string ObjectId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public string Vision { get; set; }
        public string Mission { get; set; }
        public string Goals { get; set; }
        public string FoundedOn { get; set; }
        public bool IsFilled { get; set; }
        public bool IsEditable { get; set; }
    }
}
