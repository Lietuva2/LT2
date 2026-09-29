using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.MongoDB;

namespace Data.ViewModels.Organization
{
    public class InfoEditModel
    {
        public string ObjectId { get; set; }
        [Required]
        public string Name { get; set; }
        public int Type { get; set; }
        public List<SelectListItem> Types { get; set; }
        public string Description { get; set; }
        public string Vision { get; set; }
        public string Mission { get; set; }
        public string Goals { get; set; }
        public DateTime? FoundedOn { get; set; }
    }
}
