using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;

namespace Framework.Data.Sessions.MongoDB
{
    public class MongoSession : IDbSession
    {
        protected readonly MongoServer mongo;
        protected readonly MongoDatabase db;

        public MongoSession()
        {
            mongo = MongoServer.Create(ConfigurationManager.ConnectionStrings["MongoServer"].ConnectionString);
            db = mongo.GetDatabase(ConfigurationManager.ConnectionStrings["MongoDB"].ConnectionString);
        }

        public void CommitChanges()
        {
            //mongo isn't transactional in this way... it's all firehosed
        }

        public void Delete<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new()
        {
            var items = GetAll<T>().Where(expression);
            foreach (T item in items)
            {
                Delete(item);
            }
        }

        public void Delete<T>(T item) where T : class, new()
        {
            db.GetCollection<T>().Remove(Query.EQ("_id", item.ToBsonDocument().GetValue("_id")), SafeMode.True);
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
            db.DropCollection(typeof(T).Name);
        }

        public T GetSingle<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new()
        {
            return db.GetCollection<T>().AsQueryable().Where(expression).SingleOrDefault();
        }

        public T GetById<T>(object id) where T : class, new()
        {
            MongoObjectId objectId;

            if(id is MongoObjectId)
            {
                objectId = id as MongoObjectId;
            }
            else if(id is string)
            {
                objectId = id.ToString();
            }
            else
            {
                throw new Exception("id must be of type MongoObjectId or string");
            }

            return db.GetCollection<T>().FindOneById(objectId.BsonObjectId);
        }

        public IQueryable<T> GetAll<T>() where T : class, new()
        {
            return db.GetCollection<T>().AsQueryable();
        }

        public void Add<T>(T item) where T : class, new()
        {
            db.GetCollection<T>().Insert(item, SafeMode.True);
        }

        public void Add<T>(IEnumerable<T> items) where T : class, new()
        {
            foreach (T item in items)
            {
                Add(item);
            }
        }

        public void Update<T>(T item) where T : class
        {
            db.GetCollection<T>(item.GetType().Name).Update(Query.EQ("_id", item.ToBsonDocument().GetValue("_id")), global::MongoDB.Driver.Builders.Update.Replace(item), SafeMode.True);
        }

        public void Cancel()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}