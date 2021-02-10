using Data.Enums;

namespace Data.ViewModels.Base
{
    public class SubscribeModel
    {
        public string Id { get; set; }
        public bool Subscribed { get; set; }
        public EntryTypes? Type { get; set; }
    }
}
