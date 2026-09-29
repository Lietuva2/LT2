using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class MileStoneEditModel : ProjectModel
    {
        [Required]
        public string InsertSubject { get; set; }
        public DateTime? InsertDate { get; set; }
        public List<MileStoneModel> MileStones { get; set; }
        public bool IsEditable { get; set; }

        public MileStoneEditModel()
        {
        }
    }
}

