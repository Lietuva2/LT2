using System.Data.Entity;
using Data.EF.Actions;
using Framework.Data.Factories;

namespace Data.EF.Users
{
    public class UsersContextFactory : DbContextFactory<UsersEntities>, IUsersContextFactory
    {
    }

    public interface IUsersContextFactory : IDbContextFactory<UsersEntities>
    {
    }
}
