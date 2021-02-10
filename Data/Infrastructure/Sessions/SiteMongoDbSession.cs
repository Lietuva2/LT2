using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Framework.Data.Sessions.MongoDB;
using Framework.Infrastructure.Storage;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using System.Linq;
using Framework.Infrastructure.Storage;
using MongoDB.Driver.GridFS;

namespace Data.Infrastructure.Sessions
{
    public class SiteMongoDbSession : MongoSession, INoSqlSession
    {
        public void UpdateById<T>(MongoObjectId id, IMongoUpdate update)
        {
            db.GetCollection<T>().Update(Query.EQ("_id", id), update);
        }

        public void UpdateOne<T>(IMongoQuery find, IMongoUpdate update)
        {
            db.GetCollection<T>().Update(find, update);
        }

        public MongoCursor<T> Find<T>(IMongoQuery find)
        {
            return db.GetCollection<T>().Find(find);
        }

        public T FindByIdAndModify<T>(MongoObjectId id, IMongoUpdate update)
        {
            return db.GetCollection<T>().FindAndModify(Query.EQ("_id", id), SortBy.Ascending("_id"), update, true).GetModifiedDocumentAs<T>();
        }

        public T FindAndModify<T>(IMongoQuery find, IMongoUpdate update)
        {
            return db.GetCollection<T>().FindAndModify(find, SortBy.Ascending("_id"), update, true).GetModifiedDocumentAs<T>();
        }

        public IQueryable<T> GetAllIn<T>(string prop, params BsonValue[] inSet)
        {
            return db.GetCollection<T>().Find(Query.In(prop, inSet)).AsQueryable();
        }

        public IQueryable<T> GetAllNotIn<T>(string prop, params BsonValue[] inSet)
        {
            return db.GetCollection<T>().Find(Query.NotIn(prop, inSet)).AsQueryable();
        }

        public void CreateIndex<T>(string name, bool isUnique,params string[] keys)
        {
            var options = IndexOptions.SetName(name).SetUnique(isUnique);
            var indexKeys = IndexKeys.Descending(keys);
            db.GetCollection<T>().EnsureIndex(indexKeys, options);
        }

        public MongoObjectId SaveFile(string filename, byte[] fileBytes, string contentType)
        {
            Stream stream = new MemoryStream(fileBytes);
            var options = new MongoGridFSCreateOptions();
            options.ContentType = contentType;
            var file = db.GridFS.Upload(stream, filename, options);
            return (MongoObjectId)file.Id;
        }

        public FileViewModel GetFile<T>(MongoObjectId id)
        {
            var file = db.GridFS.FindOneById(id);
            if(file != null)
            {
                using (var stream = file.OpenRead())
                {
                   var bytes = new byte[stream.Length];
                   stream.Read(bytes, 0, (int)stream.Length);

                   return new FileViewModel
                   {
                       ContentType = file.ContentType,
                       File = bytes
                   };
                }
            }

            return null;
        }

        public MongoCollection<T> GetMongoCollection<T>()
        {
            return db.GetCollection<T>();
        }
    }
}