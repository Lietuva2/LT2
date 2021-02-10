using System;
using System.Data.Entity.Core.Objects;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.CSharp.RuntimeBinder;

namespace Framework.Data.Sessions.EF {
    public class EFReadOnlySession:IReadOnlySession {
        protected ObjectContext _context;
        public EFReadOnlySession(ObjectContext context) {
            _context = context;
        }
        
        public T GetSingle<T>(Expression<Func<T, bool>> expression) where T : class, new() {

            return GetAll<T>().Where(expression).SingleOrDefault();
        }

        public T GetById<T>(object id) where T : class, new()
        {
            throw new NotImplementedException();
        }

        public virtual IQueryable<T> GetAll<T>() where T : class, new() {
            return new ObjectQuery<T>(GetSetName<T>(), _context, MergeOption.NoTracking);
        }

        public virtual void Dispose()
        {
            _context.Dispose();
        }

        private string GetSetName<T>()
        {
            var entitySetProperty =
            _context.GetType().GetProperties()
               .Single(p => p.PropertyType.IsGenericType && typeof(IQueryable<>)
               .MakeGenericType(typeof(T)).IsAssignableFrom(p.PropertyType));

            return entitySetProperty.Name;
        }
    }
}