using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.ViewModels.Base;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class JoinProjectModel
    {
        public string IdeaId { get; set; }
        public bool IsJoinable { get; set; }
    }
}