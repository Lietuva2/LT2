using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Linq;

namespace Framework.Data.Sessions.LinqToSQL
{
    public class LinqToSqlSession : LinqToSqlReadOnlySession, IDbSession
    {
        protected LinqToSqlSession(DataContext db)
        {
            _db = db;
        }

        public void CommitChanges()
        {
            _db.SubmitChanges();
        }
        
        public void Delete<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new()
        {

            var query = GetAll<T>().Where(expression);
            GetTable<T>().DeleteAllOnSubmit(query);
        }

        public void Delete<T>(T item) where T : class, new()
        {
            GetTable<T>().DeleteOnSubmit(item);
        }

        /// <summary>
        /// Deletes the specified items to delete.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="itemsToDelete">The items to delete.</param>
        public void Delete<T>(IEnumerable<T> itemsToDelete) where T : class, new()
        {
            foreach (var item in itemsToDelete)
            {
                Delete(item);
            }
        }

        public void DeleteAll<T>() where T : class, new()
        {
            var query = GetAll<T>();
            GetTable<T>().DeleteAllOnSubmit(query);
        }

        public void Add<T>(T item) where T : class, new()
        {
            GetTable<T>().InsertOnSubmit(item);
        }
        public void Add<T>(IEnumerable<T> items) where T : class, new()
        {
            GetTable<T>().InsertAllOnSubmit(items);
        }
        public void Update<T>(T item) where T : class
        {
            //nothing needed here
        }

        public void Cancel()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            CommitChanges();
            base.Dispose();
        }
    }
}
