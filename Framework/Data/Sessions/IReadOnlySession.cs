using System;

namespace Framework.Data.Sessions
{
    public interface IReadOnlySession : IDisposable
    {

        T GetSingle<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new();
        System.Linq.IQueryable<T> GetAll<T>() where T : class, new();
        T GetById<T>(object id) where T : class, new();
    }
}
