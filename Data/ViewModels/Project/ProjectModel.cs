using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web.Mvc;
using Hyper.ComponentModel;

namespace Data.ViewModels.Project
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class ProjectModel
    {
        public string Id { get; set; }
        public string IdeaId { get; set; }
        public string Subject { get; set; }
        
        public bool IsCurrentUserInvolved { get; set; }
        public bool IsPendingConfirmation { get; set; }
        public bool IsJoinable { get; set; }
    }
}