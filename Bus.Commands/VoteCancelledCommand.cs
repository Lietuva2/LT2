using Framework.Bus;

namespace Bus.Commands
{
    public class VoteCancelledCommand : Command
    {
        public string ObjectId { get; set; }
        public string UserId { get; set; }
    }
}
