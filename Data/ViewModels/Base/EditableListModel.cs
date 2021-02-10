using Framework.Strings;

namespace Data.ViewModels.Base
{
    public class EditableListModel
    {
        public string Id { get; set; }
        public bool IsNew { get { return Id.IsNullOrEmpty(); } }
        public bool IsDeleted { get; set; }
    }
}
