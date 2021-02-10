using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Linq;
using System.Transactions;
using Framework.Infrastructure.ValueInjections;
using Omu.ValueInjecter;

namespace Framework.Data.Sessions.EF
{
    public class EFSession : EFReadOnlySession, IDbSession
    {
        private bool cancelTransaction;

        public EFSession(ObjectContext context)
            : base(context)
        {
            cancelTransaction = false;
        }

        public override IQueryable<T> GetAll<T>()
        {
            return new ObjectQuery<T>(GetSetName<T>(), _context);
        }

        public void Cancel()
        {
            cancelTransaction = true;
        }

        public void CommitChanges()
        {
            _context.SaveChanges(SaveOptions.AcceptAllChangesAfterSave);
        }

        public void Delete<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new()
        {
            var items = GetAll<T>().Where(expression).ToList();
            Delete(items.AsEnumerable());
        }

        public void Delete<T>(T item) where T : class, new()
        {
            if (!IsAttached(item))
            {
                _context.Attach(item as IEntityWithKey);
            }

            _context.DeleteObject(item);
        }

        public void Delete<T>(IEnumerable<T> itemsToDelete) where T : class, new()
        {
            foreach(var item in itemsToDelete)
            {
                Delete(item);
            }
        }

        public void DeleteAll<T>() where T : class, new()
        {
            var query = GetAll<T>();
            foreach (var item in query)
            {
                Delete(item);
            }
        }

        public void Add<T>(T item) where T : class, new()
        {
            _context.AddObject(GetSetName<T>(), item);
        }
        public void Add<T>(IEnumerable<T> items) where T : class, new()
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }
        public void Update<T>(T item) where T : class
        {
            if (!(item is IEntityWithKey))
            {
                return;
            }

            if (!IsAttached(item))
            {
                //T newItem = new T();
                //newItem.InjectFrom<EfInjection>(item);
                _context.Attach(item as IEntityWithKey);
                _context.ObjectStateManager.ChangeObjectState(item, EntityState.Modified);
            }
        }

        public override void Dispose()
        {
            if (!cancelTransaction)
            {
                CommitChanges();
            }

            base.Dispose();
        }

        private string GetSetName<T>()
        {

            //If you get an error here it's because your namespace
            //for your EDM doesn't match the partial model class
            //to change - open the properties for the EDM FILE and change "Custom Tool Namespace"
            //Not - this IS NOT the Namespace setting in the EDM designer - that's for something
            //else entirely. This is for the EDMX file itself (r-click, properties)

            var entitySetProperty =
            _context.GetType().GetProperties()
               .Single(p => p.PropertyType.IsGenericType && typeof(IQueryable<>)
               .MakeGenericType(typeof(T)).IsAssignableFrom(p.PropertyType));

            return entitySetProperty.Name;
        }

        public bool IsAttached(object entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }
            ObjectStateEntry entry;
            if (_context.ObjectStateManager.TryGetObjectStateEntry(entity, out entry))
            {
                return (entry.State != EntityState.Detached);
            }
            return false;
        }
    }
}