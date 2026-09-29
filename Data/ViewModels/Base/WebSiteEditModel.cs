using System.ComponentModel.DataAnnotations;
using Data.ViewModels.Base;

namespace Data.ViewModels.Base
{
    public class UrlEditModel : EditableListModel
    {
        public string Title { get; set; }
        [Required(ErrorMessageResourceType = typeof(Globalization.Resources.Voting.Resource), ErrorMessageResourceName = "UrlIsRequired")]
        public string Url { get; set; }

        public string ListName { get; set; }

        public UrlEditModel() : this("Urls")
        {
        }

        public UrlEditModel(string listName)
        {
            ListName = listName;
        }
    }
}
