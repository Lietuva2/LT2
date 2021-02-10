namespace Data.ViewModels.Chat
{
    public class ChatUser
    {
        public string Id { get; set; }
        public int DbId { get; set; }
        public string Name { get; set; }
        public string ConnectionId { get; set; }
        public bool IsOnline { get; set; }
    }
}