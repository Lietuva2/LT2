using System.Data.Entity;
using Data.EF.Users;
using Framework.Data.Factories;

namespace Data.EF.Voting
{
    public class VotingContextFactory : DbContextFactory<VotingEntities>, IVotingContextFactory
    {
    }

    public interface IVotingContextFactory : IDbContextFactory<VotingEntities>
    {
    }
}
