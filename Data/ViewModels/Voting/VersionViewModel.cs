using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Comments;
using Framework.Lists;
using Hyper.ComponentModel;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Voting
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class VersionViewModel
    {
        public string Text { get; set; }
        public string Subject { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public bool IsForHistory { get; set; }
    }
}