using System.Collections.Generic;

namespace Data.ViewModels.Chat
{
    public class ChatIndexModel
    {
        public List<ChatGroupModel> Groups { get; set; }
        public List<ChatUser> Users { get; set; }
    }
}