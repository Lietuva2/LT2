using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CSharp.RuntimeBinder;

namespace Framework.Data.Sessions.InMemory {
    public class InMemorySession:IDbSession {

        IList<Object> _list;
        public InMemorySession() {
            _list = new List<Object>();
        }
        public void CommitChanges() {
            //nada
        }

        public void Delete<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T: class, new() {
            //cast it
            var items = GetAll<T>().Where(expression);
            for (int i = 0; i < items.Count(); i++) {
                var item = _list[i];
                if (item.GetType() == typeof(T)) {
                    Delete(item);
                }
            }
        }

        public void Delete<T>(T item) where T: class, new() {
            _list.Remove(item);
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

        public void DeleteAll<T>() where T: class, new()
        {
            foreach (var item in _list)
            {
                if (item.GetType() == typeof(T)) {
                    Delete(item);
                }
            }
        }

        public T GetSingle<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T: class, new() {
            return GetAll<T>().Where(expression).SingleOrDefault();
        }

        public T GetById<T>(object id) where T: class, new()
        {
            return this.GetSingle<T>(t => FilterOnId(t, id));
        }

        public IQueryable<T> GetAll<T>() where T: class, new() {
            var result = new List<T>();
            foreach (var item in _list) {
                if (item.GetType() == typeof(T)) {
                    result.Add(item as T);
                }
            }
            return result.AsQueryable();
        }

        public void Add<T>(T item) where T: class, new() {
            _list.Add(item);
        }

        public void Add<T>(IEnumerable<T> items) where T: class, new() {
            foreach (var item in items) {
                Add(item);
            }
        }

        public void Update<T>(T item) where T: class {
            //don't need to do nada here
        }

        public void Cancel()
        {
            throw new NotImplementedException();
        }


        public void Dispose()
        {
            _list.Clear();
            _list = null;
        }

        private bool FilterOnId(dynamic input, object id)
        {
            try
            {
                return input.Id == id;
            }
            catch (RuntimeBinderException)
            {
                try
                {
                    return input.ID == id;
                }
                catch (RuntimeBinderException)
                {
                    throw new MethodAccessException("Specified entity doesn't have the Id column");
                }
            }
        }
    }
}