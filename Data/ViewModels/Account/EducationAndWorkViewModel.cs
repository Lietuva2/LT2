using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class EducationAndWorkViewModel
    {
        public string UserObjectId { get; set; }
        public List<PositionVewModel> Positions { get; set; }
        public List<EducationViewModel> Educations { get; set; }
        public List<MemberOfPartiesViewModel> MemberOfParties { get; set; }
        public bool IsCurrentUser { get; set; }

        public bool IsFilled
        {
            get
            {
                return Educations.Count > 0 || Positions.Count > 0 || MemberOfParties.Count > 0 ||
                       !string.IsNullOrEmpty(Summary) || !string.IsNullOrEmpty(Specialties);
            }
        }

        public string Summary { get; set; }
        public string Specialties { get; set; }

        public EducationAndWorkViewModel()
        {
            Positions = new List<PositionVewModel>();
            Educations = new List<EducationViewModel>();
            MemberOfParties = new List<MemberOfPartiesViewModel>();
        }
    }

    public class PositionVewModel
    {
        public string CompanyName { get; set; }
        public string Title { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Description { get; set; }
        public bool IsCurrent { get; set; }
    }
    public class EducationViewModel
    {
        public string Country { get; set; }
        public string SchoolName { get; set; }
        public string Degree { get; set; }
        public string FieldOfStudy { get; set; }
        public string YearFrom { get; set; }
        public string YearTo { get; set; }
        public string Activities { get; set; }
        public string Notes { get; set; }
    }

    public class MemberOfPartiesViewModel
    {
        public string PartyName { get; set; }
        public string PartyUrl { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Description { get; set; }
        public bool IsCurrent { get; set; }
    }
}
