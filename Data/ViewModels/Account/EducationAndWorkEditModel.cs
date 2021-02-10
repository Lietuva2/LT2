using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class EducationAndWorkEditModel
    {
        public string UserObjectId { get; set; }
        public List<PositionEditModel> Positions { get; set; }
        public List<EducationEditModel> Educations { get; set; }
        public List<MemberOfPartiesEditModel> MemberOfParties { get; set; }
        public IEnumerable<SelectListItem> Years { get; set; }
        public IEnumerable<SelectListItem> WorkYears { get; set; }
        public IEnumerable<SelectListItem> Months { get; set; }
        public string Summary { get; set; }
        public string Specialties { get; set; }
        public int EditIndex { get; set; }

        public EducationAndWorkEditModel()
        {
            Positions = new List<PositionEditModel>();
            Educations = new List<EducationEditModel>();
            MemberOfParties = new List<MemberOfPartiesEditModel>();
        }
    }
}
