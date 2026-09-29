using Data.ViewModels.Base;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class InterestsEditModel : EditableListModel
    {
        public string UserObjectId { get; set; }
        public string Interests { get; set; }
        public string Groups { get; set; }
        public string Awards { get; set; }
        public string PoliticalViews { get; set; }
    }
}
