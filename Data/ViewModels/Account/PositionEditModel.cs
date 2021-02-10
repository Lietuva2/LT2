using Data.ViewModels.Base;
using Framework.Mvc.DataAnnotations;

namespace Data.ViewModels.Account
{
    public class PositionEditModel : EditableListModel
    {
        public string CompanyName { get; set; }
        public string Title { get; set; }
        [RequiredIf("CompanyName")]
        public int? StartYear { get; set; }
        public int? StartMonth { get; set; }
        [RequiredIfNot("IsCurrent")]
        public int? EndYear { get; set; }
        public int? EndMonth { get; set; }
        public string Description { get; set; }
        public bool IsCurrent { get; set; }
        public string IsCurrentClass
        {
            get
            {
                return IsCurrent ? "current" : "historic";
            }
        }

        public PositionEditModel()
        {
            IsCurrent = true;
        }
    }
}
