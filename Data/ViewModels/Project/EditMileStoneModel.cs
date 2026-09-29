using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class EditMileStoneModel
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        [Required]
        public string Subject { get; set; }
        public DateTime? Date { get; set; }

        public EditMileStoneModel()
        {
        }
    }
}