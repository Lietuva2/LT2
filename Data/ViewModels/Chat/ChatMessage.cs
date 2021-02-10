using System;

namespace Data.ViewModels.Chat
{
    public class ChatMessageModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }
        public DateTime Date { get; set; }
        public string Message { get; set; }
        public string DateString { get { return Date.ToString("yyyy-MM-dd HH:mm:ss"); } }
    }
}