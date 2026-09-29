using Framework.Bus;

namespace Bus.Commands
{
    public class IdeaVersionViewedCommand : Command
    {
        public string VersionId { get; set; }
        public int UserDbId { get; set; }

        public IdeaVersionViewedCommand()
        {
        }
    }
}
