using System;
using System.Collections.Generic;

namespace Data.ViewModels.Chat
{
    public class ChatGroupModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public int MessageCount { get; set; }
        public DateTime Date { get; set; }
        public IEnumerable<ChatUser> Users { get; set; }

        public ChatGroupModel()
        {
            Users = new List<ChatUser>();
        }
    }
}