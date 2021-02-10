using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Framework.Data.Sessions
{
    public interface IDbSession : IReadOnlySession
    {
        void CommitChanges();
        void Delete<T>(Expression<Func<T, bool>> expression) where T : class, new();
        void Delete<T>(T item) where T : class, new();
        void Delete<T>(IEnumerable<T> itemsToDelete) where T : class, new();
        void DeleteAll<T>() where T : class, new();
        void Add<T>(T item) where T : class, new();
        void Add<T>(IEnumerable<T> items) where T : class, new();
        void Update<T>(T item) where T : class;
        void Cancel();
    }
}
