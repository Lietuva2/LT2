using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.Data.Factories
{
    public class DbContextFactory<TContext> : IDbContextFactory<TContext>
        where TContext : DbContext, ICustomDbContext, new()
    {
        private TContext context = null;

        public TContext CreateContext(bool saveChangesOnDispose = false, bool unique = false)
        {
            if (unique || context == null || context.Helper == null || context.Helper.IsDisposed)
            {
                context = new TContext();
                context.Helper = new ContextHelper(saveChangesOnDispose);
            }

            if(unique)
            {
                return context;
            }

            if (context.Helper != null && !context.Helper.SaveChangesOnDispose && saveChangesOnDispose)
            {
                context.Helper.SaveChangesOnDispose = true;
            }

            context.Helper.Register();

            return context;
        }
    }
}
