using System.ComponentModel.DataAnnotations;
using Data.MongoDB;
using Data.ViewModels.Base;

namespace Data.ViewModels.Base
{
    public class PhoneNumberEditModel : EditableListModel
    {
        public PhoneNumber.Types Type { get; set; }
        public string Number { get; set; }

        public string ListName { get; set; }

        public PhoneNumberEditModel() : this("Urls")
        {
        }

        public PhoneNumberEditModel(string listName)
        {
            ListName = listName;
        }
    }
}
