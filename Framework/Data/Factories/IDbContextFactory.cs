using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Data.Factories
{
    public interface IDbContextFactory<TContext> where TContext : DbContext, ICustomDbContext, new()
    {
        TContext CreateContext(bool saveChangesOnDispose = false, bool unique = false);
    }
}
