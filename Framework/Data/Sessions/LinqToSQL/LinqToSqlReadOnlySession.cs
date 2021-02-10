using System;
using System.Linq;
using System.Linq.Expressions;
using System.Data.Linq;

namespace Framework.Data.Sessions.LinqToSQL {
    public class LinqToSqlReadOnlySession:IReadOnlySession {
        protected DataContext _db;
        protected LinqToSqlReadOnlySession()
        {
        }

        public LinqToSqlReadOnlySession(DataContext db) {
            _db = db;

            //no need for object tracking here - it's read only
            _db.ObjectTrackingEnabled = false;
        }
        
        public virtual void Dispose() {
            _db.Dispose();
        }

        public T GetSingle<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new() {
            return GetTable<T>().SingleOrDefault(expression);
        }

        public T GetById<T>(object id) where T : class, new()
        {
            var metaTable = _db.Mapping.GetTable(typeof(T));
            var identities = metaTable.RowType.IdentityMembers;
            var key = string.Empty;
            if (identities.Count > 0)
            {
                key = identities[0].Name;
            }

            if (String.IsNullOrEmpty(key))
            {
                throw new MethodAccessException("Specified entity doesn't have associated key");
            }

            if (id == null)
            {
                throw new ArgumentNullException("id");
            }

            var t = Expression.Parameter(typeof(T), "t");
            var predicate = (Expression<Func<T, bool>>)Expression.Lambda(Expression.Equal(Expression.Property(t, key), Expression.Constant(id)), t);

            return GetAll<T>().Where(predicate).SingleOrDefault() ?? new T();
        }

        public IQueryable<T> GetAll<T>() where T : class, new() {
            return GetTable<T>().AsQueryable();
        }

        /// <summary>
        /// Gets the table provided by the type T and returns for querying
        /// </summary>
        protected Table<T> GetTable<T>() where T : class, new()
        {
            return _db.GetTable<T>();
        }
    }
}
