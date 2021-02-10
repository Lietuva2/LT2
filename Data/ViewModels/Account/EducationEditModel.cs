using Data.ViewModels.Base;

namespace Data.ViewModels.Account
{
    public class EducationEditModel : EditableListModel
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
}
