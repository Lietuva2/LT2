using System.Data.Entity;
using Framework.Data.Factories;

namespace Data.EF.Actions
{
    public class ActionsContextFactory : DbContextFactory<ActionsEntities>, IActionsContextFactory
    {
    }

    public interface IActionsContextFactory : IDbContextFactory<ActionsEntities>
    {
    }
}
