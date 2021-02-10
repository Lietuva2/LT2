using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.ViewModels.Base;
using Framework.Mvc.DataAnnotations;
using Framework.Strings;
using Hyper.ComponentModel;

namespace Data.ViewModels.Problem
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ProblemCreateEditModel
    {
        public string Id { get; set; }
        [Required]
        public string Text { get; set; }
        public List<SelectListItem> Organizations { get; set; }
        public List<int> CategoryIds { get; set; }
        public string OrganizationId { get; set; }
        public int? MunicipalityId { get; set; }
        public bool IsPrivate { get; set; }

        public bool IsEdit
        {
            get { return !Id.IsNullOrEmpty(); }
        }

        public ProblemCreateEditModel()
        {
        }
    }
}